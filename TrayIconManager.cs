using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindicatorPlus
{
    public class TrayIconManager : IDisposable
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private AppConfig _config;
        private WatermarkWindow _watermark;
        private DesktopWatcher _watcher;

        private IntPtr _lastHIcon = IntPtr.Zero;
        private int _currentDesktop = 1;
        private int _totalDesktops = 1;

        private sealed class MenuHostForm : Form
        {
            public MenuHostForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(1, 1);
                Opacity = 0;
                CreateControl();
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                    return cp;
                }
            }
        }

        private MenuHostForm _menuHostForm = null;
        private SettingsForm _settingsForm = null;
        private TaskbarTintOverlay _tintOverlay = null;
        private Timer _zOrderTimer = null;

        // Clean & Modern Menu items
        private ToolStripMenuItem _menuOpenSettings;
        private ToolStripMenuItem _menuHeaderStatus;
        private ToolStripMenuItem _menuRenameDesktop;
        private ToolStripMenuItem _menuWatermarkToggle;
        private ToolStripMenuItem _menuTaskbarTintToggle;
        private ToolStripMenuItem _menuLoopDesktop;
        private ToolStripMenuItem _menuResetPos;
        private ToolStripMenuItem _menuKillOldWindicator;

        public event Action ExitRequested;

        public TrayIconManager(AppConfig config, WatermarkWindow watermark, DesktopWatcher watcher, TaskbarTintOverlay tintOverlay = null)
        {
            _config = config;
            _watermark = watermark;
            _watcher = watcher;
            _tintOverlay = tintOverlay;

            InitializeContextMenu();

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                ContextMenuStrip = null, // Custom popup positioning
                Text = "Windicator+ 虚拟桌面指示器"
            };

            // Support both right-click and left-click menu invocation with smart upward positioning
            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
                {
                    ShowContextMenuAboveTaskbar();
                }
            };

            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                // Double click: quick open settings center
                OpenSettingsForm();
            };

            // Listen to window dragging position move
            _watermark.PositionMoved += (x, y) =>
            {
                _config.WatermarkPosition = "Custom";
                _config.CustomX = x;
                _config.CustomY = y;
                ConfigManager.Save(_config);
            };

            // Listen to taskbar watermark click to popup menu
            _watermark.RequestContextMenu += (s, e) =>
            {
                ShowContextMenuAboveTaskbar();
            };

            // UI 线程 Z 序协调定时器 (200ms)
            _zOrderTimer = new Timer();
            _zOrderTimer.Interval = 200;
            _zOrderTimer.Tick += (s, e) =>
            {
                ReconcileTaskbarOverlayOrder();
            };
            _zOrderTimer.Start();
        }

        private void ShowContextMenuAboveTaskbar()
        {
            if (_contextMenu == null || _contextMenu.IsDisposed) return;
            if (_contextMenu.Visible) return;

            CheckOldWindicatorStatus();
            UpdateDesktopHeader();

            Point cursor = Cursor.Position;
            Screen screen = Screen.FromPoint(cursor);
            Rectangle workArea = screen.WorkingArea;

            // Measure context menu preferred size
            Size menuSize = _contextMenu.GetPreferredSize(Size.Empty);

            // X Position: center around cursor or align nicely within screen bounds
            int popupX = cursor.X - (menuSize.Width / 2);
            if (popupX + menuSize.Width > workArea.Right - 8)
            {
                popupX = workArea.Right - menuSize.Width - 8;
            }
            if (popupX < workArea.Left + 8)
            {
                popupX = workArea.Left + 8;
            }

            // Y Position: POPUP PRECISELY ABOVE THE TASKBAR
            // workArea.Bottom is exactly the top edge of the Windows taskbar!
            int popupY = workArea.Bottom - menuSize.Height - 8;

            // In case taskbar is on top (rare)
            if (workArea.Top > 0 && cursor.Y < workArea.Top)
            {
                popupY = workArea.Top + 8;
            }

            try
            {
                if (_menuHostForm == null || _menuHostForm.IsDisposed)
                {
                    _menuHostForm = new MenuHostForm();
                }

                if (!_menuHostForm.Visible)
                {
                    _menuHostForm.Show();
                }

                SetForegroundWindow(_menuHostForm.Handle);
                _contextMenu.Show(_menuHostForm, _menuHostForm.PointToClient(new Point(popupX, popupY)));
            }
            catch
            {
                if (_watermark != null)
                {
                    _watermark.SetContextMenuOpen(false);
                }
                if (_menuHostForm != null && !_menuHostForm.IsDisposed)
                {
                    try { _menuHostForm.Hide(); } catch { }
                }
                throw;
            }
        }

        private void InitializeContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.RenderMode = ToolStripRenderMode.System;
            _contextMenu.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

            _contextMenu.Opening += (s, e) =>
            {
                try
                {
                    if (_watermark != null)
                    {
                        _watermark.SetContextMenuOpen(true);
                    }
                }
                catch
                {
                    if (_watermark != null)
                    {
                        _watermark.SetContextMenuOpen(false);
                    }
                    throw;
                }
            };

            _contextMenu.Opened += (s, e) =>
            {
                ReconcileTaskbarOverlayOrder();
                if (_menuHostForm != null && !_menuHostForm.IsDisposed && _menuHostForm.IsHandleCreated)
                {
                    try
                    {
                        _menuHostForm.BeginInvoke(new Action(() =>
                        {
                            ReconcileTaskbarOverlayOrder();
                        }));
                    }
                    catch { }
                }
            };

            _contextMenu.Closed += (s, e) =>
            {
                try
                {
                    if (_watermark != null)
                    {
                        _watermark.SetContextMenuOpen(false);
                    }
                }
                finally
                {
                    if (_menuHostForm != null && !_menuHostForm.IsDisposed)
                    {
                        try { _menuHostForm.Hide(); } catch { }
                    }
                }
                ReconcileTaskbarOverlayOrder();
                if (_menuHostForm != null && !_menuHostForm.IsDisposed && _menuHostForm.IsHandleCreated)
                {
                    try
                    {
                        _menuHostForm.BeginInvoke(new Action(() =>
                        {
                            ReconcileTaskbarOverlayOrder();
                        }));
                    }
                    catch { }
                }
            };

            // 1. Primary Settings Center Entry (Prominent)
            _menuOpenSettings = new ToolStripMenuItem("⚙️ 打开设置中心...", null, (s, e) => OpenSettingsForm())
            {
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204)
            };
            _contextMenu.Items.Add(_menuOpenSettings);

            // 2. Status Header
            _menuHeaderStatus = new ToolStripMenuItem("🖥️ 虚拟桌面: 初始化中...")
            {
                Enabled = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _contextMenu.Items.Add(_menuHeaderStatus);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 3. Quick Toggles
            _menuWatermarkToggle = new ToolStripMenuItem("✨ 显示悬浮水印", null, (s, e) =>
            {
                _config.WatermarkEnabled = !_config.WatermarkEnabled;
                _menuWatermarkToggle.Checked = _config.WatermarkEnabled;
                _watermark.SetWatermarkEnabled(_config.WatermarkEnabled);
                ConfigManager.Save(_config);
            })
            {
                Checked = _config.WatermarkEnabled
            };
            _contextMenu.Items.Add(_menuWatermarkToggle);

            _menuTaskbarTintToggle = new ToolStripMenuItem("🎨 任务栏专属氛围色", null, (s, e) =>
            {
                _config.TaskbarTintEnabled = !_config.TaskbarTintEnabled;
                _menuTaskbarTintToggle.Checked = _config.TaskbarTintEnabled;
                if (_tintOverlay != null)
                {
                    _tintOverlay.ApplyConfig(_config, _watcher != null ? _watcher.CurrentDesktop : 1);
                }
                ConfigManager.Save(_config);
            })
            {
                Checked = _config.TaskbarTintEnabled
            };
            _contextMenu.Items.Add(_menuTaskbarTintToggle);

            _menuLoopDesktop = new ToolStripMenuItem("🔁 虚拟桌面首尾循环切换", null, (s, e) =>
            {
                _config.DesktopLoopEnabled = !_config.DesktopLoopEnabled;
                _menuLoopDesktop.Checked = _config.DesktopLoopEnabled;
                ConfigManager.Save(_config);
            })
            {
                Checked = _config.DesktopLoopEnabled
            };
            _contextMenu.Items.Add(_menuLoopDesktop);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 4. Quick Actions
            _menuRenameDesktop = new ToolStripMenuItem("✏️ 命名当前桌面...", null, OnRenameCurrentDesktop);
            _contextMenu.Items.Add(_menuRenameDesktop);

            _menuResetPos = new ToolStripMenuItem("📍 恢复水印右下角位置", null, (s, e) =>
            {
                _config.CustomX = -1;
                _config.CustomY = -1;
                _config.WatermarkPosition = "BottomRight";
                _watermark.SetPosition("BottomRight");
                ConfigManager.Save(_config);
            });
            _contextMenu.Items.Add(_menuResetPos);

            // 5. Old Windicator helper (Only visible if running)
            _menuKillOldWindicator = new ToolStripMenuItem("🛑 关闭旧版 Windicator", null, (s, e) => KillOldWindicator());
            _contextMenu.Items.Add(_menuKillOldWindicator);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 6. Exit
            ToolStripMenuItem menuExit = new ToolStripMenuItem("❌ 退出 Windicator+", null, (s, e) =>
            {
                if (ExitRequested != null)
                {
                    ExitRequested();
                }
            });
            _contextMenu.Items.Add(menuExit);

            CheckOldWindicatorStatus();
        }

        private void OnRenameCurrentDesktop(object sender, EventArgs e)
        {
            string currentName = _config.GetDesktopName(_currentDesktop) ?? "";
            using (RenameDialog dlg = new RenameDialog(_currentDesktop, currentName))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _config.SetDesktopName(_currentDesktop, dlg.DesktopName);
                    ConfigManager.Save(_config);

                    _watermark.SetDesktopName(dlg.DesktopName);
                    UpdateDesktopHeader();
                }
            }
        }

        private void CheckOldWindicatorStatus()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("Windicator");
                _menuKillOldWindicator.Visible = (procs != null && procs.Length > 0);
            }
            catch { }
        }

        private void KillOldWindicator()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("Windicator");
                int count = 0;
                if (procs != null)
                {
                    foreach (var p in procs)
                    {
                        try { p.Kill(); count++; } catch { }
                    }
                }
                MessageBox.Show(
                    string.Format("已成功结束 {0} 个旧版 Windicator 进程！", count),
                    "Windicator+ 提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _menuKillOldWindicator.Visible = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("结束旧版 Windicator 失败: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void UpdateDesktop(int current, int total)
        {
            _currentDesktop = current;
            _totalDesktops = Math.Max(1, total);

            string customName = _config.GetDesktopName(_currentDesktop);
            _watermark.SetDesktop(current, total, customName);

            UpdateDesktopHeader();
            UpdateTrayIcon();
            CheckOldWindicatorStatus();
        }

        private void UpdateDesktopHeader()
        {
            string customName = _config.GetDesktopName(_currentDesktop);
            if (!string.IsNullOrEmpty(customName))
            {
                _menuHeaderStatus.Text = string.Format("🖥️ 当前: [{0}] 桌面 {1} / 共 {2} 个", customName, _currentDesktop, _totalDesktops);
                _notifyIcon.Text = string.Format("Windicator+ 虚拟桌面\n[{0}] 桌面 {1} (共 {2} 个)", customName, _currentDesktop, _totalDesktops);
            }
            else
            {
                _menuHeaderStatus.Text = string.Format("🖥️ 当前: 桌面 {0} / 共 {1} 个", _currentDesktop, _totalDesktops);
                _notifyIcon.Text = string.Format("Windicator+ 虚拟桌面\n当前: 桌面 {0} (共 {1} 个)", _currentDesktop, _totalDesktops);
            }
            if (_notifyIcon.Text.Length >= 64)
            {
                _notifyIcon.Text = string.Format("桌面 {0} / {1}", _currentDesktop, _totalDesktops);
            }

            _menuRenameDesktop.Text = string.Format("✏️ 命名当前桌面 ({0})...",
                string.IsNullOrEmpty(customName) ? ("桌面 " + _currentDesktop) : customName);

            _menuWatermarkToggle.Checked = _config.WatermarkEnabled;
            _menuLoopDesktop.Checked = _config.DesktopLoopEnabled;
        }

        public void UpdateTrayIcon()
        {
            int size = 32;
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);

                    Color bgColor;
                    Color fgColor;

                    if (_config.TrayStyle == "Blue")
                    {
                        bgColor = Color.FromArgb(0, 120, 215);
                        fgColor = Color.White;
                    }
                    else if (_config.TrayStyle == "Light")
                    {
                        bgColor = Color.FromArgb(252, 252, 254);
                        fgColor = Color.FromArgb(12, 14, 18);
                    }
                    else // Dark
                    {
                        bgColor = Color.FromArgb(26, 28, 34);
                        fgColor = Color.White;
                    }

                    Rectangle badgeRect = new Rectangle(0, 0, size, size);
                    int radius = 8;
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        int d = radius * 2;
                        path.AddArc(badgeRect.X, badgeRect.Y, d, d, 180, 90);
                        path.AddArc(badgeRect.Right - d, badgeRect.Y, d, d, 270, 90);
                        path.AddArc(badgeRect.Right - d, badgeRect.Bottom - d, d, d, 0, 90);
                        path.AddArc(badgeRect.X, badgeRect.Bottom - d, d, d, 90, 90);
                        path.CloseFigure();

                        using (SolidBrush bgBrush = new SolidBrush(bgColor))
                        {
                            g.FillPath(bgBrush, path);
                        }

                        Color borderColor = (_config.TrayStyle == "Light")
                            ? Color.FromArgb(205, 210, 220)
                            : Color.FromArgb(70, 255, 255, 255);
                        using (Pen p = new Pen(borderColor, 1.0f))
                        {
                            g.DrawPath(p, path);
                        }
                    }

                    string text = _currentDesktop.ToString();
                    float fontSize = (text.Length > 1) ? 17f : 25f;
                    using (Font font = new Font("Segoe UI Variable Display", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush fgBrush = new SolidBrush(fgColor))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;

                        RectangleF textRect = new RectangleF(0, -1, size, size);
                        g.DrawString(text, font, fgBrush, textRect, sf);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                Icon icon = Icon.FromHandle(hIcon);

                _notifyIcon.Icon = icon;

                if (_lastHIcon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(_lastHIcon);
                }
                _lastHIcon = hIcon;
            }
        }

        public void PrewarmSettingsForm()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_config, _watermark, this, _watcher, _tintOverlay);
                _settingsForm.CreateControl();
                IntPtr h = _settingsForm.Handle;
            }
        }

        public void OpenSettingsForm()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_config, _watermark, this, _watcher, _tintOverlay);
            }
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        public void Dispose()
        {
            if (_zOrderTimer != null)
            {
                _zOrderTimer.Stop();
                _zOrderTimer.Dispose();
                _zOrderTimer = null;
            }

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Close();
                _settingsForm.Dispose();
                _settingsForm = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_lastHIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_lastHIcon);
                _lastHIcon = IntPtr.Zero;
            }

            if (_menuHostForm != null)
            {
                if (!_menuHostForm.IsDisposed)
                {
                    _menuHostForm.Dispose();
                }
                _menuHostForm = null;
            }

            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }

        internal enum ZOrderRelation
        {
            Unknown,
            Above,
            Below
        }

        internal static ZOrderRelation GetZOrderRelation(IntPtr hWndA, IntPtr hWndB, int maxHops = 512)
        {
            if (hWndA == IntPtr.Zero || hWndB == IntPtr.Zero || hWndA == hWndB)
            {
                return ZOrderRelation.Unknown;
            }

            // Check if hWndA is above hWndB by walking up (GW_HWNDPREV) from hWndB
            int hops = 0;
            IntPtr cur = hWndB;
            while (hops < maxHops)
            {
                cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDPREV);
                if (cur == IntPtr.Zero) break;
                if (cur == hWndA)
                {
                    return ZOrderRelation.Above;
                }
                hops++;
            }

            // Check if hWndB is above hWndA by walking up (GW_HWNDPREV) from hWndA
            hops = 0;
            cur = hWndA;
            while (hops < maxHops)
            {
                cur = NativeMethods.GetWindow(cur, NativeMethods.GW_HWNDPREV);
                if (cur == IntPtr.Zero) break;
                if (cur == hWndB)
                {
                    return ZOrderRelation.Below;
                }
                hops++;
            }

            return ZOrderRelation.Unknown;
        }

        internal void ReconcileTaskbarOverlayOrder()
        {
            ReconcileTaskbarOverlayOrder(IntPtr.Zero);
        }

        internal void ReconcileTaskbarOverlayOrder(IntPtr shellOverride)
        {
            try
            {
                IntPtr hShell = shellOverride != IntPtr.Zero ? shellOverride : NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (hShell == IntPtr.Zero || !NativeMethods.IsWindow(hShell) || !NativeMethods.IsWindowVisible(hShell))
                {
                    return;
                }

                IntPtr hTint = IntPtr.Zero;
                if (_tintOverlay != null && !_tintOverlay.IsDisposed && _tintOverlay.Visible && _tintOverlay.IsHandleCreated)
                {
                    NativeMethods.RECT rc;
                    if (NativeMethods.GetWindowRect(_tintOverlay.Handle, out rc))
                    {
                        if ((rc.Right - rc.Left) > 1 && (rc.Bottom - rc.Top) > 1)
                        {
                            hTint = _tintOverlay.Handle;
                        }
                    }
                }

                IntPtr hWater = IntPtr.Zero;
                if (_watermark != null && !_watermark.IsDisposed && _watermark.Visible && _watermark.IsHandleCreated && _watermark.HasTaskbarIntent)
                {
                    NativeMethods.RECT rc;
                    if (NativeMethods.GetWindowRect(_watermark.Handle, out rc))
                    {
                        if ((rc.Right - rc.Left) > 1 && (rc.Bottom - rc.Top) > 1)
                        {
                            hWater = _watermark.Handle;
                        }
                    }
                }

                IntPtr hMenu = IntPtr.Zero;
                if (_contextMenu != null && !_contextMenu.IsDisposed && _contextMenu.Visible && _contextMenu.IsHandleCreated)
                {
                    NativeMethods.RECT rc;
                    if (NativeMethods.GetWindowRect(_contextMenu.Handle, out rc))
                    {
                        if ((rc.Right - rc.Left) > 1 && (rc.Bottom - rc.Top) > 1)
                        {
                            hMenu = _contextMenu.Handle;
                        }
                    }
                }

                // If none of our layers are active/visible, nothing to reconcile
                if (hTint == IntPtr.Zero && hWater == IntPtr.Zero && hMenu == IntPtr.Zero)
                {
                    return;
                }

                // 预期从顶到底：[Menu] > [Watermark] > [Tint] > [Shell]
                List<IntPtr> activeChain = new List<IntPtr>();
                if (hMenu != IntPtr.Zero) activeChain.Add(hMenu);
                if (hWater != IntPtr.Zero) activeChain.Add(hWater);
                if (hTint != IntPtr.Zero) activeChain.Add(hTint);
                activeChain.Add(hShell);

                // 检查所有相邻对：先遍历全部相邻对确保没有 Unknown，绝不能因首个 wrong 立即 break 漏掉后面的 Unknown
                bool orderCorrect = true;
                for (int i = 0; i < activeChain.Count - 1; i++)
                {
                    IntPtr higher = activeChain[i];
                    IntPtr lower = activeChain[i + 1];
                    ZOrderRelation rel = GetZOrderRelation(higher, lower, 512);
                    if (rel == ZOrderRelation.Unknown)
                    {
                        // 句柄不在同一链或越界，只要存在任一 unknown，返回不盲目 raise
                        return;
                    }
                    if (rel != ZOrderRelation.Above)
                    {
                        orderCorrect = false;
                    }
                }

                // 已满足 menu > watermark > tint > shell 时绝不 SetWindowPos
                if (orderCorrect)
                {
                    return;
                }

                // 失序时仅受影响层按 tint、watermark、可见 menu 顺序使用 HWND_TOPMOST+NOMOVE|NOSIZE|NOACTIVATE（不要 SHOWWINDOW），保证 menu 最高
                uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;

                if (hTint != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(hTint, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                }
                if (hWater != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(hWater, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                }
                if (hMenu != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(hMenu, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ReconcileTaskbarOverlayOrder] Exception: " + ex);
            }
        }
    }
}
