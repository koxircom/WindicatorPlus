using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindicatorPlus
{
    public class SettingsForm : Form
    {
        public const string CURRENT_VERSION = "v1.3.0";


        private AppConfig _config;
        private WatermarkWindow _watermark;
        private TrayIconManager _trayManager;
        private DesktopWatcher _watcher;
        private TaskbarTintOverlay _tintOverlay;

        public bool SuppressPersistenceForTesting { get; set; }

        // Navigation
        private string _currentTab = "General";
        private readonly List<DarkNavButton> _navButtons = new List<DarkNavButton>();

        // Content Area Headers & Footers
        private Panel _rightContentPanel;
        private Panel _pageHost;
        private readonly Dictionary<Control, Panel> _pageScrollers = new Dictionary<Control, Panel>();
        private Label _lblPageTitle;
        private Label _btnReset;
        private Button _btnDone;

        // Views (Managed TableLayoutPanels with natural vertical flow)
        private Control _viewGeneral;
        private Control _viewWatermark;
        private Control _viewPosition;
        private Control _viewNames;
        private Control _viewSystem;
        private Control _viewAbout;

        // General Controls
        private DarkToggleSwitch _swWatermark;
        private DarkToggleSwitch _swAutoStart;
        private DarkToggleSwitch _swDesktopLoop;

        // Watermark Controls
        private TrackBar _tbScale;
        private Label _lblScaleVal;
        private TrackBar _tbOpacity;
        private Label _lblOpacityVal;
        private TextBox _txtTitleTpl;
        private TextBox _txtSubtitleTpl;
        private DarkToggleSwitch _swShowBigNum;
        private DarkToggleSwitch _swShowCard;
        private ComboBox _cbColorTheme;

        // Taskbar Tint Overlay Controls
        private DarkToggleSwitch _swTaskbarTint;
        private TrackBar _tbTintOpacity;
        private Label _lblTintOpacityVal;

        // Position & System Controls
        private ComboBox _cbPosition;
        private ComboBox _cbModifier;
        private ComboBox _cbTrayStyle;
        private readonly Dictionary<int, TextBox> _desktopTextBoxes = new Dictionary<int, TextBox>();

        private bool _isBindingUI = false;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED: 内核级全控件树离屏双缓冲合成，彻底杜绝自上而下阶梯式扫描与黑白线框撕裂
                return cp;
            }
        }

        public SettingsForm(AppConfig config, WatermarkWindow watermark, TrayIconManager trayManager, DesktopWatcher watcher, TaskbarTintOverlay tintOverlay = null)
        {
            _config = config ?? new AppConfig();
            _watermark = watermark;
            _trayManager = trayManager;
            _watcher = watcher;
            _tintOverlay = tintOverlay;
            SuppressPersistenceForTesting = false;

            InitializeAitiyUI();
            LoadConfigToUI();
        }

        private void InitializeAitiyUI()
        {
            Text = "Windicator+ — 设置中心";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;

            // Build at one 96-DPI design baseline; scale after every view exists.
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;

            BackColor = Color.FromArgb(17, 18, 23); // Deep charcoal
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular);
            TopMost = false;

            // Dynamically fit inside primary screen working area
            int targetW = 1760;
            int targetH = 1200;
            Size = new Size(targetW, targetH);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            // 1. Top Custom TitleBar (48px for high DPI headroom)
            Panel titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(17, 18, 23)
            };
            titleBar.MouseDown += TitleBar_MouseDown;

            Label lblTitle = new Label
            {
                Text = "Windicator+ — 设置中心",
                ForeColor = Color.FromArgb(150, 156, 172),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                Location = new Point(20, 12),
                AutoSize = true
            };
            lblTitle.MouseDown += TitleBar_MouseDown;
            titleBar.Controls.Add(lblTitle);

            Button btnClose = new Button
            {
                Text = "✕",
                Size = new Size(48, 48),
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(180, 185, 195),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 53, 69);
            btnClose.Click += (s, e) => Close();

            Button btnMin = new Button
            {
                Text = "—",
                Size = new Size(48, 48),
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(180, 185, 195),
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 38, 48);
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;

            titleBar.Controls.Add(btnMin);
            titleBar.Controls.Add(btnClose);

            // 2. Main Body Container
            Panel mainBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 15, 20)
            };

            // 2.1 Left Nav (230px wide)
            Panel leftNav = new Panel
            {
                Dock = DockStyle.Left,
                Width = 280,
                BackColor = Color.FromArgb(19, 21, 27),
                Padding = new Padding(12, 16, 12, 16),
                AutoScroll = false
            };

            FlowLayoutPanel navFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            AddNavGroupTitle("常规", navFlow);
            AddNavButton("常规设置", "General", navFlow);
            AddNavButton("水印外观与设计", "Watermark", navFlow);

            AddNavGroupTitle("布局与手势", navFlow);
            AddNavButton("停靠与拖动", "Position", navFlow);
            AddNavButton("桌面命名", "Names", navFlow);

            AddNavGroupTitle("系统与关于", navFlow);
            AddNavButton("托盘与启动", "System", navFlow);
            AddNavButton("关于与更新", "About", navFlow);

            leftNav.Controls.Add(navFlow);

            // 2.2 Right Area
            Panel rightArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 15, 20),
                Padding = new Padding(40, 28, 40, 28)
            };

            // Right Header (Title and Reset button in isolated table columns)
            TableLayoutPanel rightHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 0, 8)
            };
            rightHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rightHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rightHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblPageTitle = new Label
            {
                Text = "常规设置",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };

            _btnReset = new Label
            {
                Text = "恢复本页默认设置",
                ForeColor = Color.FromArgb(135, 142, 158),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(16, 8, 4, 0)
            };
            _btnReset.MouseEnter += (s, e) => _btnReset.ForeColor = Color.FromArgb(167, 139, 250);
            _btnReset.MouseLeave += (s, e) => _btnReset.ForeColor = Color.FromArgb(135, 142, 158);
            _btnReset.Click += (s, e) => ResetPageDefaults();

            rightHeader.Controls.Add(_lblPageTitle, 0, 0);
            rightHeader.Controls.Add(_btnReset, 1, 0);

            // Right Bottom Live Note Bar
            TableLayoutPanel rightBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 6, 0, 0)
            };
            rightBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rightBottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label lblLiveSave = new Label
            {
                Text = "⚡ 所有设置均实时生效并已自动保存",
                ForeColor = Color.FromArgb(167, 139, 250),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 0, 0)
            };

            _btnDone = new Button
            {
                Text = "完成 / 关闭",
                AutoSize = true,
                MinimumSize = new Size(130, 36),
                Padding = new Padding(16, 0, 16, 0),
                Height = 36,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(8, 0, 0, 0)
            };
            _btnDone.FlatAppearance.BorderSize = 0;
            _btnDone.Click += (s, e) => Close();

            rightBottom.Controls.Add(lblLiveSave, 0, 0);
            rightBottom.Controls.Add(_btnDone, 1, 0);

            // Right Scrollable Content Viewport
            _rightContentPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(14, 15, 20),
                Padding = new Padding(0, 6, 12, 6)
            };
            _rightContentPanel.HandleCreated += (s, e) =>
            {
                try
                {
                    NativeMethods.SetWindowTheme(_rightContentPanel.Handle, "DarkMode_Explorer", null);
                }
                catch { }
            };

            rightArea.Controls.Add(_rightContentPanel);
            rightArea.Controls.Add(rightBottom);
            rightArea.Controls.Add(rightHeader);

            mainBody.Controls.Add(rightArea);
            mainBody.Controls.Add(leftNav);

            Controls.Add(mainBody);
            Controls.Add(titleBar);

            // Build all six view models
            BuildGeneralView();
            BuildWatermarkView();
            BuildPositionView();
            BuildNamesView();
            BuildSystemView();
            BuildAboutView();

            // Each permanent page owns its scrollbar, so switching tabs cannot
            // change another page's width and recursively remeasure its children.
            _pageHost = _rightContentPanel;
            _pageHost.AutoScroll = false;
            _pageHost.Padding = Padding.Empty;
            Control[] allViews = { _viewAbout, _viewSystem, _viewNames, _viewPosition, _viewWatermark, _viewGeneral };
            foreach (Control view in allViews)
            {
                Panel scroller = new DoubleBufferedPanel {
                    Dock = DockStyle.Fill, AutoScroll = true,
                    Padding = new Padding(0, 6, 12, 6), BackColor = _pageHost.BackColor,
                    Visible = false
                };
                scroller.HandleCreated += (s, e) => NativeMethods.SetWindowTheme(scroller.Handle, "DarkMode_Explorer", null);
                view.Dock = DockStyle.Top;
                scroller.Controls.Add(view);
                _pageScrollers.Add(view, scroller);
                _pageHost.Controls.Add(scroller);
                WarmUpControlTree(scroller);
            }
            SwitchTab("General");

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }

        private const int WM_SETREDRAW = 0x000B;

        private void SuspendDrawing(Control control)
        {
            if (control != null && control.IsHandleCreated && control.Visible)
            {
                NativeMethods.SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void ResumeDrawing(Control control)
        {
            if (control != null && control.IsHandleCreated)
            {
                NativeMethods.SendMessage(control.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                control.Invalidate(true);
            }
        }

        private void WarmUpControlTree(Control c)
        {
            if (c == null) return;
            c.CreateControl();
            IntPtr h = c.Handle;
            foreach (Control child in c.Controls)
            {
                WarmUpControlTree(child);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                NativeMethods.SetWindowTheme(Handle, "DarkMode_Explorer", null);
            }
            catch { }
            try
            {
                if (_rightContentPanel != null && _rightContentPanel.IsHandleCreated)
                {
                    NativeMethods.SetWindowTheme(_rightContentPanel.Handle, "DarkMode_Explorer", null);
                }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                NativeMethods.SetWindowTheme(Handle, "DarkMode_Explorer", null);
                if (_rightContentPanel != null && _rightContentPanel.IsHandleCreated)
                {
                    NativeMethods.SetWindowTheme(_rightContentPanel.Handle, "DarkMode_Explorer", null);
                }
            }
            catch { }
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, work.Width - 48), Math.Min(Height, work.Height - 48));
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
            }
        }

        private void AddNavGroupTitle(string title, FlowLayoutPanel parent)
        {
            Label lbl = new Label
            {
                Text = title,
                ForeColor = Color.FromArgb(100, 108, 128),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(12, 14, 0, 6)
            };
            parent.Controls.Add(lbl);
        }

        private DarkNavButton AddNavButton(string text, string tabKey, FlowLayoutPanel parent)
        {
            DarkNavButton btn = new DarkNavButton(text)
            {
                Width = 252,
                Height = 52,
                Tag = tabKey,
                Margin = new Padding(0, 2, 0, 2)
            };
            btn.Click += (s, e) => SwitchTab(tabKey);
            _navButtons.Add(btn);
            parent.Controls.Add(btn);
            return btn;
        }

        private void SwitchTab(string tabKey)
        {
            _currentTab = tabKey;
            foreach (var btn in _navButtons)
            {
                btn.IsSelected = (btn.Tag as string == tabKey);
            }

            Control activeView = null;
            if (tabKey == "General")
            {
                _lblPageTitle.Text = "常规设置";
                activeView = _viewGeneral;
            }
            else if (tabKey == "Watermark")
            {
                _lblPageTitle.Text = "水印外观与设计";
                activeView = _viewWatermark;
            }
            else if (tabKey == "Position")
            {
                _lblPageTitle.Text = "停靠与拖动";
                activeView = _viewPosition;
            }
            else if (tabKey == "Names")
            {
                _lblPageTitle.Text = "桌面命名";
                BuildDesktopNameCards();
                activeView = _viewNames;
            }
            else if (tabKey == "System")
            {
                _lblPageTitle.Text = "托盘与启动";
                activeView = _viewSystem;
            }
            else if (tabKey == "About")
            {
                _lblPageTitle.Text = "关于与更新";
                activeView = _viewAbout;
            }

            if (activeView == null) return;
            bool drawingSuspended = _pageHost.IsHandleCreated && _pageHost.Visible;
            if (drawingSuspended) SuspendDrawing(_pageHost);
            _pageHost.SuspendLayout();
            try
            {
                Panel target = _pageScrollers[activeView];
                foreach (Panel page in _pageScrollers.Values)
                    if (page != target) page.Visible = false;
                _rightContentPanel = target;
                target.Visible = true;
            }
            finally
            {
                try { _pageHost.ResumeLayout(true); }
                finally { if (drawingSuspended) ResumeDrawing(_pageHost); }
            }
        }

        // ==========================================
        // Card and Layout Helpers
        // ==========================================
        private TableLayoutPanel CreateViewRoot()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 0, 16),
                Font = this.Font
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return root;
        }

        private DarkCardPanel CreateCard(string cardTitle)
        {
            DarkCardPanel card = new DarkCardPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                Margin = new Padding(0, 0, 0, 16),
                Padding = new Padding(24, 18, 24, 18),
                BackColor = Color.FromArgb(24, 26, 33)
            };

            if (!string.IsNullOrEmpty(cardTitle))
            {
                Label lbl = new Label
                {
                    Text = cardTitle,
                    Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(195, 200, 215),
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    Padding = new Padding(0, 0, 0, 14)
                };
                card.Controls.Add(lbl);
                lbl.BringToFront();
            }

            return card;
        }

        private void AddToCard(DarkCardPanel card, Control ctrl)
        {
            ctrl.Dock = DockStyle.Top;
            card.Controls.Add(ctrl);
            ctrl.BringToFront();
            if (ctrl.Height <= 0) ctrl.PerformLayout();
        }

        private void AddTwoColumnSwitches(
            DarkCardPanel card,
            string title1, string sub1, out DarkToggleSwitch sw1,
            string title2, string sub2, out DarkToggleSwitch sw2)
        {
            TableLayoutPanel rowTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 14)
            };
            rowTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rowTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // Cell 1
            Control cell1 = CreateSwitchCell(title1, sub1, out sw1);
            cell1.Margin = new Padding(0, 0, 12, 0);

            // Cell 2
            Control cell2 = CreateSwitchCell(title2, sub2, out sw2);
            cell2.Margin = new Padding(12, 0, 0, 0);

            rowTable.Controls.Add(cell1, 0, 0);
            rowTable.Controls.Add(cell2, 1, 0);

            AddToCard(card, rowTable);
        }

        private Control CreateSwitchCell(string title, string sub, out DarkToggleSwitch sw)
        {
            TableLayoutPanel cell = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 0, 8)
            };
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Panel textStack = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 10, 0)
            };

            Label lblSub = new Label
            {
                Text = sub,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(140, 146, 160),
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0)
            };
            textStack.Controls.Add(lblSub);

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 4)
            };
            textStack.Controls.Add(lblTitle);

            sw = new DarkToggleSwitch
            {
                Anchor = AnchorStyles.Right,
                Margin = new Padding(4, 4, 0, 4)
            };

            cell.Controls.Add(textStack, 0, 0);
            cell.Controls.Add(sw, 1, 0);
            return cell;
        }

        private TrackBar AddSliderRow(
            DarkCardPanel card,
            string title,
            string notice,
            int min,
            int max,
            int initialVal,
            string unit,
            out Label lblVal)
        {
            int clampedVal = Math.Max(min, Math.Min(max, initialVal));

            int noticeH = string.IsNullOrEmpty(notice) ? 0 : 26;
            Panel block = new DoubleBufferedPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 16)
            };

            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            header.Controls.Add(lblTitle, 0, 0);

            lblVal = new Label
            {
                Text = clampedVal + unit,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                AutoSize = true,
                Anchor = AnchorStyles.Right
            };
            header.Controls.Add(lblVal, 1, 0);

            block.Controls.Add(header);
            header.BringToFront();

            if (!string.IsNullOrEmpty(notice))
            {
                Label lblNotice = new Label
                {
                    Text = notice,
                    Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(167, 139, 250),
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    Padding = new Padding(0, 4, 0, 4)
                };
                block.Controls.Add(lblNotice);
                lblNotice.BringToFront();
            }

            TrackBar tb = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = clampedVal,
                TickFrequency = 5,
                AutoSize = false,
                Height = 42,
                BackColor = card.BackColor,
                TickStyle = TickStyle.None,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 6, 0, 4)
            };
            block.Controls.Add(tb);
            tb.BringToFront();

            AddToCard(card, block);
            return tb;
        }

        private TextBox AddInputRow(DarkCardPanel card, string labelText, string initialText)
        {
            Panel block = new DoubleBufferedPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 14)
            };

            Label lbl = new Label
            {
                Text = labelText,
                Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, 6)
            };
            block.Controls.Add(lbl);
            lbl.BringToFront();

            TextBox txt = new TextBox
            {
                Text = initialText,
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Color.FromArgb(17, 18, 23),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Regular)
            };
            block.Controls.Add(txt);
            txt.BringToFront();

            AddToCard(card, block);
            return txt;
        }

        private ComboBox AddComboBoxRow(DarkCardPanel card, string labelText, string subNotice, string[] items, int width)
        {
            Panel block = new DoubleBufferedPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 14)
            };

            Label lbl = new Label
            {
                Text = labelText,
                Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, subNotice != null ? 3 : 6)
            };
            block.Controls.Add(lbl);
            lbl.BringToFront();

            if (!string.IsNullOrEmpty(subNotice))
            {
                Label lblSub = new Label
                {
                    Text = subNotice,
                    Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(140, 146, 160),
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    Padding = new Padding(0, 0, 0, 6)
                };
                block.Controls.Add(lblSub);
                lblSub.BringToFront();
            }

            Panel cbWrapper = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.Transparent
            };

            ComboBox cb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                Height = 32,
                BackColor = Color.FromArgb(24, 26, 33),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
                Location = new Point(0, 0)
            };
            cb.ItemHeight = cb.Font.Height + 8;
            cb.DrawItem += (sender, e) =>
            {
                Color bg = (e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(57, 43, 82) : Color.FromArgb(24, 26, 33);
                using (Brush brush = new SolidBrush(bg)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font, e.Bounds, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            cb.Items.AddRange(items);

            cbWrapper.Controls.Add(cb);
            block.Controls.Add(cbWrapper);
            cbWrapper.BringToFront();

            AddToCard(card, block);
            return cb;
        }

        // ==========================================
        // 1. General View
        // ==========================================
        private void BuildGeneralView()
        {
            TableLayoutPanel root = CreateViewRoot();

            // Card 1: Core functions
            DarkCardPanel cardCore = CreateCard("核心功能");
            DarkToggleSwitch swTransparentDummy;
            AddTwoColumnSwitches(cardCore,
                "屏幕置顶水印", "在屏幕最顶层显示\n当前桌面序号与名称", out _swWatermark,
                "鼠标日常完全穿透", "日常 100% 击穿水印\n绝不阻碍任何鼠标操作", out swTransparentDummy);

            swTransparentDummy.Checked = true;
            swTransparentDummy.Enabled = false;

            _swWatermark.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.WatermarkEnabled = _swWatermark.Checked;
                if (_watermark != null) _watermark.SetWatermarkEnabled(_config.WatermarkEnabled);
                SafeSaveConfig();
            };

            AddTwoColumnSwitches(cardCore,
                "开机自动启动", "开机自动在系统托盘\n后台静默常驻", out _swAutoStart,
                "虚拟桌面首尾循环", "在 1 号桌面左切跳至末尾\n末尾右切回 1", out _swDesktopLoop);

            _swAutoStart.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.AutoStart = _swAutoStart.Checked;
                SafeSetAutoStart(_config.AutoStart);
                SafeSaveConfig();
            };

            _swDesktopLoop.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.DesktopLoopEnabled = _swDesktopLoop.Checked;
                SafeSaveConfig();
            };

            root.Controls.Add(cardCore);

            // Card 2: Desktop Interaction
            DarkCardPanel cardWin = CreateCard("桌面与窗口交互");
            Label lblWinDesc = new Label
            {
                Text = "• 按住快捷键 (默认 Ctrl) 鼠标划入悬浮窗即可随心拖拽到屏幕任意角落；\n• 支持靠近屏幕四周或任务栏小箭头 20px 时智能磁吸贴边，日常 100% 穿透到底层应用。",
                ForeColor = Color.FromArgb(170, 176, 192),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            AddToCard(cardWin, lblWinDesc);
            root.Controls.Add(cardWin);

            _viewGeneral = root;
        }

        // ==========================================
        // 2. Watermark Designer View
        // ==========================================
        private void BuildWatermarkView()
        {
            TableLayoutPanel root = CreateViewRoot();

            // Card 1: Scale & Opacity
            DarkCardPanel cardScaleOpacity = CreateCard("水印尺寸与透明度设计 (实时联动)");

            _tbScale = AddSliderRow(
                cardScaleOpacity,
                "水印显示尺寸缩放",
                "尺寸同时用于任务栏；任务栏只显示数字和主标题，副标题/底板仅桌面",
                75,
                160,
                _config.WatermarkScale,
                "%",
                out _lblScaleVal);

            _tbScale.Scroll += (s, e) =>
            {
                if (_isBindingUI) return;
                int val = _tbScale.Value;
                _lblScaleVal.Text = val + "%";
                _config.WatermarkScale = val;
                ApplyDesignerChanges();
            };

            _tbOpacity = AddSliderRow(
                cardScaleOpacity,
                "全局 Alpha 透明度",
                null,
                10,
                95,
                _config.WatermarkOpacity,
                "%",
                out _lblOpacityVal);

            _tbOpacity.Scroll += (s, e) =>
            {
                if (_isBindingUI) return;
                int val = _tbOpacity.Value;
                _lblOpacityVal.Text = val + "%";
                _config.WatermarkOpacity = val;
                if (_watermark != null) _watermark.SetOpacity(val);
                SafeSaveConfig();
            };

            // Card 2: Template
            DarkCardPanel cardTemplate = CreateCard("水印文字与内容模板设计 (打字即时在屏幕上呈现)");

            Label lblTplInfo = new Label
            {
                Text = "★ 动态变量语法：{n} = 当前桌面序号 (动态变动), {total} = 桌面总数, {name} = 桌面别名\n除变量以外输入的任何文字均作为【固定内容】原样呈现。",
                ForeColor = Color.FromArgb(167, 139, 250),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };
            AddToCard(cardTemplate, lblTplInfo);

            _txtTitleTpl = AddInputRow(cardTemplate, "主标题模板：", _config.TitleTemplate ?? "{name}");
            _txtTitleTpl.TextChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.TitleTemplate = _txtTitleTpl.Text;
                ApplyDesignerChanges();
            };

            _txtSubtitleTpl = AddInputRow(cardTemplate, "副标题模板：", _config.SubtitleTemplate ?? "虚拟桌面 {n} (共 {total} 个)");
            _txtSubtitleTpl.TextChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.SubtitleTemplate = _txtSubtitleTpl.Text;
                ApplyDesignerChanges();
            };

            // Preset Buttons Flow
            FlowLayoutPanel presetFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 6),
                Padding = new Padding(0)
            };

            Label lblPresets = new Label
            {
                Text = "快速套用：",
                ForeColor = Color.FromArgb(135, 142, 158),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            presetFlow.Controls.Add(lblPresets);

            Button btnP1 = CreateSmallPresetBtn("桌面 {n}", (s, e) => { _txtTitleTpl.Text = "桌面 {n}"; _txtSubtitleTpl.Text = "虚拟桌面 ({n}/{total})"; });
            Button btnP2 = CreateSmallPresetBtn("{name}", (s, e) => { _txtTitleTpl.Text = "{name}"; _txtSubtitleTpl.Text = "虚拟桌面 {n} (共 {total} 个)"; });
            Button btnP3 = CreateSmallPresetBtn("Work-{n}", (s, e) => { _txtTitleTpl.Text = "Work-{n}"; _txtSubtitleTpl.Text = "Total {total} Desktops"; });

            presetFlow.Controls.Add(btnP1);
            presetFlow.Controls.Add(btnP2);
            presetFlow.Controls.Add(btnP3);

            AddToCard(cardTemplate, presetFlow);

            // Card 3: Visual Elements & Colors
            DarkCardPanel cardVisual = CreateCard("视觉元素与显目色彩配色");

            AddTwoColumnSwitches(cardVisual,
                "显示左侧超大数字", "开启后左侧呈现\n超大粗体序号", out _swShowBigNum,
                "显示磨砂半透明底板", "关闭后为极简纯字悬浮\n(副标题/底板仅桌面)", out _swShowCard);

            _swShowBigNum.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.ShowBigNumber = _swShowBigNum.Checked;
                ApplyDesignerChanges();
            };

            _swShowCard.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.ShowBackgroundCard = _swShowCard.Checked;
                ApplyDesignerChanges();
            };

            _cbColorTheme = AddComboBoxRow(cardVisual, "色彩与显目度方案：", null, new string[] {
                "🌟 高对比极亮纯白 (超级显目，推荐)",
                "💜 炫彩霓虹紫 (Aitiy 风格)",
                "💠 冰川荧光青 (极具现代感)",
                "🌑 经典低调微暗"
            }, 460);

            _cbColorTheme.SelectedIndexChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                string[] themes = { "WhiteHighlight", "NeonPurple", "CyanGlow", "DarkSubtle" };
                if (_cbColorTheme.SelectedIndex >= 0 && _cbColorTheme.SelectedIndex < themes.Length)
                {
                    _config.WatermarkColorTheme = themes[_cbColorTheme.SelectedIndex];
                    ApplyDesignerChanges();
                }
            };

            root.Controls.Add(cardVisual);
            root.Controls.Add(cardTemplate);
            root.Controls.Add(cardScaleOpacity);

            _viewWatermark = root;
        }

        private Button CreateSmallPresetBtn(string text, EventHandler onClick)
        {
            Button btn = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(90, 30),
                Padding = new Padding(12, 0, 12, 0),
                Height = 30,
                BackColor = Color.FromArgb(30, 34, 46),
                ForeColor = Color.FromArgb(210, 215, 230),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 4)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(65, 70, 85);
            btn.Click += onClick;
            return btn;
        }

        private void ApplyDesignerChanges()
        {
            if (_watermark != null)
            {
                _watermark.SetDesignerProps(
                    _config.WatermarkScale,
                    _config.TitleTemplate,
                    _config.SubtitleTemplate,
                    _config.ShowBigNumber,
                    _config.ShowBackgroundCard,
                    _config.WatermarkColorTheme);
            }
            SafeSaveConfig();
        }

        // ==========================================
        // 3. Position View
        // ==========================================
        private void BuildPositionView()
        {
            TableLayoutPanel root = CreateViewRoot();

            DarkCardPanel cardPos = CreateCard("停靠位置预设与磁吸管理");

            _cbPosition = AddComboBoxRow(
                cardPos,
                "预设停靠方位",
                "点击切换立即平滑移动至对应屏幕方位，支持任务栏小箭头旁强磁吸附",
                new string[] {
                    "屏幕右下角 (默认)",
                    "屏幕右上角",
                    "屏幕左下角",
                    "屏幕顶部居中"
                },
                460);

            _cbPosition.SelectedIndexChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                string[] poses = { "BottomRight", "TopRight", "BottomLeft", "TopCenter" };
                if (_cbPosition.SelectedIndex >= 0 && _cbPosition.SelectedIndex < poses.Length)
                {
                    string pos = poses[_cbPosition.SelectedIndex];
                    _config.WatermarkPosition = pos;
                    if (_watermark != null) _watermark.SetPosition(pos);
                    SafeSaveConfig();
                }
            };

            Button btnResetPos = new Button
            {
                Text = "↩️ 恢复右下角默认位置",
                AutoSize = true,
                MinimumSize = new Size(250, 42),
                Padding = new Padding(14, 6, 14, 6),
                BackColor = Color.FromArgb(34, 38, 50),
                ForeColor = Color.FromArgb(210, 215, 230),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 0, 8)
            };
            btnResetPos.FlatAppearance.BorderColor = Color.FromArgb(60, 65, 80);
            btnResetPos.Click += (s, e) =>
            {
                _config.CustomX = -1;
                _config.CustomY = -1;
                _config.WatermarkPosition = "BottomRight";
                _cbPosition.SelectedIndex = 0;
                if (_watermark != null) _watermark.SetPosition("BottomRight");
                SafeSaveConfig();
            };
            AddToCard(cardPos, btnResetPos);
            root.Controls.Add(cardPos);

            DarkCardPanel cardMod = CreateCard("自由拖动触发快捷键");

            _cbModifier = AddComboBoxRow(
                cardMod,
                "拖拽修饰快捷键",
                "按住此键鼠标划入悬浮窗才可拖动，松开立即恢复穿透",
                new string[] {
                    "按住 Ctrl 键 (推荐)",
                    "按住 Alt 键",
                    "按住 Shift 键"
                },
                460);

            _cbModifier.SelectedIndexChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                string key = "Control";
                if (_cbModifier.SelectedIndex == 1) key = "Alt";
                else if (_cbModifier.SelectedIndex == 2) key = "Shift";

                _config.DragModifierKey = key;
                if (_watermark != null) _watermark.SetModifierKey(key);
                SafeSaveConfig();
            };

            root.Controls.Add(cardMod);

            // 3. Taskbar Ambient Tint Card
            DarkCardPanel cardTint = CreateCard("任务栏专属半透明微光氛围色 (随虚拟桌面变色)");

            DarkToggleSwitch swTintDummy;
            AddTwoColumnSwitches(
                cardTint,
                "任务栏专属氛围色",
                "为当前桌面镀上专属半透明色彩，100% 鼠标完全击穿穿透",
                out _swTaskbarTint,
                "开机自动呈现",
                "随桌面切换毫秒级平滑变色，透出 Windows 11 暗黑质感",
                out swTintDummy);
            swTintDummy.Visible = false;

            _swTaskbarTint.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.TaskbarTintEnabled = _swTaskbarTint.Checked;
                if (_tintOverlay != null)
                {
                    _tintOverlay.ApplyConfig(_config, _watcher != null ? _watcher.CurrentDesktop : 1);
                }
                SafeSaveConfig();
            };

            _tbTintOpacity = AddSliderRow(
                cardTint,
                "氛围色浓度 (微透饱和度)",
                "推荐 12%~20%，透出底层图标与开始菜单，氛围高级且绝不刺眼遮挡",
                5, 40, _config.TaskbarTintOpacity, "%",
                out _lblTintOpacityVal);

            _tbTintOpacity.Scroll += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.TaskbarTintOpacity = _tbTintOpacity.Value;
                if (_lblTintOpacityVal != null) _lblTintOpacityVal.Text = _tbTintOpacity.Value + "%";
                if (_tintOverlay != null)
                {
                    _tintOverlay.ApplyConfig(_config, _watcher != null ? _watcher.CurrentDesktop : 1);
                }
                SafeSaveConfig();
            };

            root.Controls.Add(cardTint);
            _viewPosition = root;
        }

        // ==========================================
        // 4. Desktop Names View
        // ==========================================
        private void BuildNamesView()
        {
            TableLayoutPanel root = CreateViewRoot();
            _viewNames = root;
            BuildDesktopNameCards();
        }

        private static readonly string[] PresetPalette = new string[]
        {
            "#2563EB", // 深海蓝
            "#7C3AED", // 赛博紫
            "#059669", // 极光绿
            "#D97706", // 暖琥珀
            "#DC2626", // 玫瑰红
            "#0891B2", // 薄荷青
            "#475569"  // 莫兰灰
        };

        private static readonly string[] PresetPaletteNames = new string[]
        {
            "深海蓝",
            "赛博紫",
            "极光绿",
            "暖琥珀",
            "玫瑰红",
            "薄荷青",
            "莫兰灰"
        };

        private static int GetPaletteIndex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            for (int i = 0; i < PresetPalette.Length; i++)
            {
                if (PresetPalette[i].Equals(hex, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private static string GetColorDisplayName(string hex)
        {
            int idx = GetPaletteIndex(hex);
            return PresetPaletteNames[idx];
        }

        private void BuildDesktopNameCards()
        {
            TableLayoutPanel root = _viewNames as TableLayoutPanel;
            if (root == null) return;

            int total = Math.Max(1, _watcher != null ? _watcher.TotalDesktops : 3);
            if (_desktopTextBoxes.Count == total && root.Controls.Count > 0)
            {
                // Already built, just synchronize current values without recreating controls
                for (int i = 1; i <= total; i++)
                {
                    if (_desktopTextBoxes.ContainsKey(i))
                    {
                        string currentVal = _config.GetDesktopName(i) ?? "";
                        if (_desktopTextBoxes[i].Text != currentVal)
                        {
                            _desktopTextBoxes[i].Text = currentVal;
                        }
                    }
                }
                return;
            }

            root.Controls.Clear();
            _desktopTextBoxes.Clear();
            DarkCardPanel card = CreateCard(string.Format("已检测到 {0} 个虚拟桌面 (打字实时呈现)", total));

            Label lblNotice = new Label
            {
                Text = "提示：尺寸同时用于任务栏；任务栏只显示数字和主标题，副标题/底板仅桌面。",
                ForeColor = Color.FromArgb(167, 139, 250),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            };
            AddToCard(card, lblNotice);

            TableLayoutPanel namesGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            namesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            namesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            namesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            for (int i = 1; i <= total; i++)
            {
                int dIdx = i;
                Label lbl = new Label
                {
                    Text = string.Format("桌面 {0}:", dIdx),
                    Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(205, 210, 225),
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 8, 14, 10)
                };

                Panel txtWrapper = new DoubleBufferedPanel
                {
                    Dock = DockStyle.Fill,
                    Height = 44,
                    BackColor = Color.FromArgb(17, 18, 23),
                    Padding = new Padding(12, 10, 12, 10),
                    Margin = new Padding(0, 6, 0, 8)
                };
                txtWrapper.Paint += (s, e) =>
                {
                    using (Pen p = new Pen(Color.FromArgb(52, 58, 74), 1.2f))
                    {
                        e.Graphics.DrawRectangle(p, 0, 0, txtWrapper.Width - 1, txtWrapper.Height - 1);
                    }
                };

                TextBox txt = new TextBox
                {
                    Text = _config.GetDesktopName(dIdx) ?? "",
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(17, 18, 23),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.None,
                    Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular)
                };

                txt.TextChanged += (s, e) =>
                {
                    if (_isBindingUI) return;
                    _config.SetDesktopName(dIdx, txt.Text.Trim());
                    SafeSaveConfig();

                    if (_watcher != null && _watcher.CurrentDesktop == dIdx)
                    {
                        if (_watermark != null) _watermark.SetDesktopName(txt.Text.Trim());
                        if (_trayManager != null) _trayManager.UpdateDesktop(_watcher.CurrentDesktop, _watcher.TotalDesktops);
                    }
                };
                txtWrapper.Controls.Add(txt);

                string curColor = _config.GetTaskbarTintColor(dIdx);
                Color clr = TaskbarTintOverlay.ParseHexColor(curColor, Color.FromArgb(124, 58, 237));
                string hexLabel = string.IsNullOrEmpty(curColor) ? "#7C3AED" : curColor.ToUpper();

                Button btnColor = new Button
                {
                    Text = string.Format("● {0}", hexLabel),
                    Width = 160,
                    Height = 44,
                    BackColor = Color.FromArgb(40, clr.R, clr.G, clr.B),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(14, 6, 0, 8)
                };
                btnColor.FlatAppearance.BorderColor = clr;
                btnColor.FlatAppearance.BorderSize = 2;

                int capturedIdx = dIdx;
                btnColor.Click += (s, e) =>
                {
                    using (ColorDialog dlg = new ColorDialog())
                    {
                        dlg.FullOpen = true; // 直接展开自定义调色盘
                        dlg.AnyColor = true;
                        string oldHex = _config.GetTaskbarTintColor(capturedIdx);
                        dlg.Color = TaskbarTintOverlay.ParseHexColor(oldHex, Color.FromArgb(124, 58, 237));

                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            Color nextClr = dlg.Color;
                            string nextHex = string.Format("#{0:X2}{1:X2}{2:X2}", nextClr.R, nextClr.G, nextClr.B);
                            _config.SetTaskbarTintColor(capturedIdx, nextHex);

                            btnColor.Text = string.Format("● {0}", nextHex);
                            btnColor.BackColor = Color.FromArgb(40, nextClr.R, nextClr.G, nextClr.B);
                            btnColor.FlatAppearance.BorderColor = nextClr;

                            SafeSaveConfig();
                            if (_tintOverlay != null)
                            {
                                _tintOverlay.ApplyConfig(_config, _watcher != null ? _watcher.CurrentDesktop : 1);
                            }
                        }
                    }
                };

                namesGrid.Controls.Add(lbl, 0, i - 1);
                namesGrid.Controls.Add(txtWrapper, 1, i - 1);
                namesGrid.Controls.Add(btnColor, 2, i - 1);
                _desktopTextBoxes[dIdx] = txt;
            }

            AddToCard(card, namesGrid);
            namesGrid.PerformLayout();
            card.PerformLayout();
            root.Controls.Add(card);
        }

        // ==========================================
        // 5. System View
        // ==========================================
        private void BuildSystemView()
        {
            TableLayoutPanel root = CreateViewRoot();

            DarkCardPanel cardTray = CreateCard("系统托盘图标配色");

            _cbTrayStyle = AddComboBoxRow(
                cardTray,
                "托盘主题配色",
                "点击立即切换系统托盘中的超大号数字徽章配色",
                new string[] {
                    "浅色底黑字 (Light 醒目白底推荐)",
                    "深色底白字 (Dark 经典黑底)",
                    "Windows Fluent 蓝底白字"
                },
                460);

            _cbTrayStyle.SelectedIndexChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                string[] styles = { "Light", "Dark", "Blue" };
                if (_cbTrayStyle.SelectedIndex >= 0 && _cbTrayStyle.SelectedIndex < styles.Length)
                {
                    string style = styles[_cbTrayStyle.SelectedIndex];
                    _config.TrayStyle = style;
                    if (_trayManager != null) _trayManager.UpdateTrayIcon();
                    SafeSaveConfig();
                }
            };

            root.Controls.Add(cardTray);

            DarkCardPanel cardAuto = CreateCard("开机自启设置");
            DarkToggleSwitch swSysAuto;
            Control autoCell = CreateSwitchCell(
                "开机自动在任务栏常驻",
                "自动写入注册表 Run 键，开机直接启动增强版",
                out swSysAuto);
            swSysAuto.Checked = _config.AutoStart;
            swSysAuto.CheckedChanged += (s, e) =>
            {
                if (_isBindingUI) return;
                _config.AutoStart = swSysAuto.Checked;
                if (_swAutoStart != null) _swAutoStart.Checked = swSysAuto.Checked;
                SafeSetAutoStart(_config.AutoStart);
                SafeSaveConfig();
            };

            AddToCard(cardAuto, autoCell);
            root.Controls.Add(cardAuto);

            _viewSystem = root;
        }

        // ==========================================
        // 6. About View
        // ==========================================
        private void BuildAboutView()
        {
            TableLayoutPanel root = CreateViewRoot();

            // Card 1: Version & Genuine Status
            DarkCardPanel cardVer = CreateCard("版本与最新更新状态");

            Label lblVerTitle = new Label
            {
                Text = "当前软件版本：Windicator+ " + CURRENT_VERSION,
                Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            AddToCard(cardVer, lblVerTitle);

            Label lblVerStatus = new Label
            {
                Text = "● 未接入在线自动更新服务 (绿色单文件免安装)",
                ForeColor = Color.FromArgb(160, 166, 180),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };
            AddToCard(cardVer, lblVerStatus);

            Button btnCheckUpdate = new Button
            {
                Text = "🔍 检查更新说明",
                AutoSize = true,
                MinimumSize = new Size(210, 42),
                Padding = new Padding(14, 6, 14, 6),
                BackColor = Color.FromArgb(34, 38, 50),
                ForeColor = Color.FromArgb(220, 225, 240),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 4)
            };
            btnCheckUpdate.FlatAppearance.BorderColor = Color.FromArgb(70, 75, 95);
            btnCheckUpdate.Click += (s, e) =>
            {
                MessageBox.Show(
                    "当前版本未接入在线自动更新服务。\n\n如需获取最新发布或查看更新说明，请访问 GitHub 官方仓库 Releases 页面手动下载最新单文件程序。",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };
            AddToCard(cardVer, btnCheckUpdate);

            root.Controls.Add(cardVer);

            // Card 2: Git Repository
            DarkCardPanel cardGit = CreateCard("开源项目与代码仓库");

            Label lblGitTitle = new Label
            {
                Text = "Windicator+ 开源项目仓库",
                Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            AddToCard(cardGit, lblGitTitle);

            Label lblGitUrl = new Label
            {
                Text = _config.GitHubRepoUrl ?? "https://github.com/koxircom/WindicatorPlus",
                Font = new Font("Consolas", 11.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            AddToCard(cardGit, lblGitUrl);

            Button btnOpenGit = new Button
            {
                Text = "🌐 打开 GitHub 仓库主页",
                AutoSize = true,
                MinimumSize = new Size(250, 42),
                Padding = new Padding(14, 6, 14, 6),
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 4)
            };
            btnOpenGit.FlatAppearance.BorderSize = 0;
            btnOpenGit.Click += (s, e) =>
            {
                string targetUrl = _config.GitHubRepoUrl ?? "https://github.com/koxircom/WindicatorPlus";
                try { Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show("打开浏览器失败: " + ex.Message); }
            };
            AddToCard(cardGit, btnOpenGit);

            root.Controls.Add(cardGit);

            // Card 3: License
            DarkCardPanel cardLic = CreateCard("开源协议与致谢");
            Label lblLicText = new Label
            {
                Text = "• 本软件遵循 MIT License 开源协议发布，纯绿色单文件免安装，永久免费。\n• 基于 Wielding/Windicator 深度重构与增强，并融入 Aitiy 现代暗黑美学。",
                ForeColor = Color.FromArgb(150, 156, 172),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            AddToCard(cardLic, lblLicText);
            root.Controls.Add(cardLic);

            _viewAbout = root;
        }

        // ==========================================
        // Safe Persistence & Data Binding
        // ==========================================
        private void SafeSaveConfig()
        {
            if (SuppressPersistenceForTesting) return;
            ConfigManager.Save(_config);
        }

        private void SafeSetAutoStart(bool enable)
        {
            if (SuppressPersistenceForTesting) return;
            ConfigManager.SetAutoStart(enable);
        }

        private void LoadConfigToUI()
        {
            _isBindingUI = true;
            try
            {
                // Watermark Controls
                if (_swWatermark != null) _swWatermark.Checked = _config.WatermarkEnabled;
                if (_swAutoStart != null) _swAutoStart.Checked = _config.AutoStart;
                if (_swDesktopLoop != null) _swDesktopLoop.Checked = _config.DesktopLoopEnabled;

                // Taskbar Tint Controls
                if (_swTaskbarTint != null) _swTaskbarTint.Checked = _config.TaskbarTintEnabled;
                if (_tbTintOpacity != null)
                {
                    _tbTintOpacity.Value = Math.Max(5, Math.Min(40, _config.TaskbarTintOpacity));
                    if (_lblTintOpacityVal != null) _lblTintOpacityVal.Text = _tbTintOpacity.Value + "%";
                }

                // Scale
                if (_tbScale != null)
                {
                    _tbScale.Value = Math.Max(75, Math.Min(160, _config.WatermarkScale));
                    if (_lblScaleVal != null) _lblScaleVal.Text = _tbScale.Value + "%";
                }

                // Opacity
                if (_tbOpacity != null)
                {
                    _tbOpacity.Value = Math.Max(10, Math.Min(95, _config.WatermarkOpacity));
                    if (_lblOpacityVal != null) _lblOpacityVal.Text = _tbOpacity.Value + "%";
                }

                // Templates
                if (_txtTitleTpl != null) _txtTitleTpl.Text = _config.TitleTemplate ?? "{name}";
                if (_txtSubtitleTpl != null) _txtSubtitleTpl.Text = _config.SubtitleTemplate ?? "虚拟桌面 {n} (共 {total} 个)";
                if (_swShowBigNum != null) _swShowBigNum.Checked = _config.ShowBigNumber;
                if (_swShowCard != null) _swShowCard.Checked = _config.ShowBackgroundCard;

                // Color Theme
                if (_cbColorTheme != null)
                {
                    if (_config.WatermarkColorTheme == "NeonPurple") _cbColorTheme.SelectedIndex = 1;
                    else if (_config.WatermarkColorTheme == "CyanGlow") _cbColorTheme.SelectedIndex = 2;
                    else if (_config.WatermarkColorTheme == "DarkSubtle") _cbColorTheme.SelectedIndex = 3;
                    else _cbColorTheme.SelectedIndex = 0; // WhiteHighlight
                }

                // Position
                if (_cbPosition != null)
                {
                    if (_config.WatermarkPosition == "TopRight") _cbPosition.SelectedIndex = 1;
                    else if (_config.WatermarkPosition == "BottomLeft") _cbPosition.SelectedIndex = 2;
                    else if (_config.WatermarkPosition == "TopCenter") _cbPosition.SelectedIndex = 3;
                    else _cbPosition.SelectedIndex = 0;
                }

                // Modifier
                if (_cbModifier != null)
                {
                    if (_config.DragModifierKey == "Alt") _cbModifier.SelectedIndex = 1;
                    else if (_config.DragModifierKey == "Shift") _cbModifier.SelectedIndex = 2;
                    else _cbModifier.SelectedIndex = 0;
                }

                // Tray Style
                if (_cbTrayStyle != null)
                {
                    if (_config.TrayStyle == "Dark") _cbTrayStyle.SelectedIndex = 1;
                    else if (_config.TrayStyle == "Blue") _cbTrayStyle.SelectedIndex = 2;
                    else _cbTrayStyle.SelectedIndex = 0; // Light
                }
            }
            finally
            {
                _isBindingUI = false;
            }
        }

        private void ResetPageDefaults()
        {
            _isBindingUI = true;
            try
            {
                if (_currentTab == "General")
                {
                    _config.WatermarkEnabled = true;
                    _config.DesktopLoopEnabled = true;
                    if (_swWatermark != null) _swWatermark.Checked = true;
                    if (_swDesktopLoop != null) _swDesktopLoop.Checked = true;
                    if (_watermark != null) _watermark.SetWatermarkEnabled(true);
                }
                else if (_currentTab == "Watermark")
                {
                    _config.WatermarkScale = 100;
                    _config.WatermarkOpacity = 40;
                    _config.TitleTemplate = "{name}";
                    _config.SubtitleTemplate = "虚拟桌面 {n} (共 {total} 个)";
                    _config.ShowBigNumber = true;
                    _config.ShowBackgroundCard = true;
                    _config.WatermarkColorTheme = "WhiteHighlight";

                    if (_tbScale != null) { _tbScale.Value = 100; _lblScaleVal.Text = "100%"; }
                    if (_tbOpacity != null) { _tbOpacity.Value = 40; _lblOpacityVal.Text = "40%"; }
                    if (_txtTitleTpl != null) _txtTitleTpl.Text = "{name}";
                    if (_txtSubtitleTpl != null) _txtSubtitleTpl.Text = "虚拟桌面 {n} (共 {total} 个)";
                    if (_swShowBigNum != null) _swShowBigNum.Checked = true;
                    if (_swShowCard != null) _swShowCard.Checked = true;
                    if (_cbColorTheme != null) _cbColorTheme.SelectedIndex = 0;

                    ApplyDesignerChanges();
                }
                else if (_currentTab == "Position")
                {
                    _config.WatermarkPosition = "BottomRight";
                    _config.CustomX = -1;
                    _config.CustomY = -1;
                    _config.DragModifierKey = "Control";
                    if (_cbPosition != null) _cbPosition.SelectedIndex = 0;
                    if (_cbModifier != null) _cbModifier.SelectedIndex = 0;
                    if (_watermark != null)
                    {
                        _watermark.SetPosition("BottomRight");
                        _watermark.SetModifierKey("Control");
                    }
                }
                else if (_currentTab == "System")
                {
                    _config.TrayStyle = "Light";
                    if (_cbTrayStyle != null) _cbTrayStyle.SelectedIndex = 0;
                    if (_trayManager != null) _trayManager.UpdateTrayIcon();
                }
                SafeSaveConfig();
            }
            finally
            {
                _isBindingUI = false;
            }
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);

            // Scale unmounted views so they stay in sync with Form's DPI
            Control[] views = new Control[] { _viewGeneral, _viewWatermark, _viewPosition, _viewNames, _viewSystem, _viewAbout };
            foreach (var v in views)
            {
                if (v != null && (_rightContentPanel == null || !_rightContentPanel.Controls.Contains(v)))
                {
                    v.Scale(factor);
                }
            }

            // Fix WinForms framework bug: TableLayoutPanel multiplies Percent ColumnStyles during Scale
            ResetTableLayoutPercentages(this);
            foreach (var v in views)
            {
                if (v != null) ResetTableLayoutPercentages(v);
            }
        }

        private void ResetTableLayoutPercentages(Control parent)
        {
            if (parent is TableLayoutPanel)
            {
                TableLayoutPanel tlp = (TableLayoutPanel)parent;
                if (tlp.ColumnCount == 2 && tlp.ColumnStyles.Count >= 2)
                {
                    if (tlp.ColumnStyles[0].SizeType == SizeType.Percent && tlp.ColumnStyles[1].SizeType == SizeType.Percent)
                    {
                        tlp.ColumnStyles[0].Width = 50F;
                        tlp.ColumnStyles[1].Width = 50F;
                    }
                    else if (tlp.ColumnStyles[0].SizeType == SizeType.Percent && tlp.ColumnStyles[1].SizeType == SizeType.AutoSize)
                    {
                        tlp.ColumnStyles[0].Width = 100F;
                    }
                }
                else if (tlp.ColumnCount == 1 && tlp.ColumnStyles.Count >= 1)
                {
                    if (tlp.ColumnStyles[0].SizeType == SizeType.Percent)
                    {
                        tlp.ColumnStyles[0].Width = 100F;
                    }
                }
            }

            foreach (Control c in parent.Controls)
            {
                ResetTableLayoutPercentages(c);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(Color.FromArgb(48, 52, 65), 1.0f))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
