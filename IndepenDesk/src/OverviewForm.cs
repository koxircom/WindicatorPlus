using System.Drawing.Drawing2D;

namespace IndepenDesk;

/// <summary>
/// Genel bakış (Mission Control benzeri): tüm monitörler ve masaüstleri kartlar halinde.
/// Pencereler tutamaçlı (⠿) kutucuklar olarak listelenir ve sürükle-bırak ile taşınır;
/// geçerli bırakma hedefleri sürükleme sırasında yeşil çerçeveyle vurgulanır.
/// </summary>
internal sealed class OverviewForm : Form
{
    private static OverviewForm? _open;
    public static bool IsOpen => _open != null;

    private readonly DesktopManager _mgr;
    private readonly ToolTip _tips = new() { InitialDelay = 400 };

    // Renk paleti
    private static readonly Color BgColor = Color.FromArgb(23, 23, 27);
    private static readonly Color CardBg = Color.FromArgb(38, 38, 45);
    private static readonly Color CardBorder = Color.FromArgb(58, 58, 68);
    private static readonly Color Accent = Color.FromArgb(75, 141, 224);   // aktif masaüstü
    private static readonly Color DropAccent = Color.FromArgb(70, 170, 110); // bırakma hedefi
    private static readonly Color ChipBg = Color.FromArgb(53, 53, 61);
    private static readonly Color ChipHover = Color.FromArgb(72, 72, 84);
    private static readonly Color ChipBorder = Color.FromArgb(80, 80, 94);
    private static readonly Color TextDim = Color.FromArgb(150, 150, 162);

    private sealed record WindowDrag(IntPtr Handle);
    private sealed record DesktopDrag(string Device, int LocalIndex);

    public static void Toggle(DesktopManager mgr)
    {
        if (_open != null) { _open.Close(); return; }
        var f = new OverviewForm(mgr);
        _open = f;
        f.FormClosed += (_, _) => _open = null;
        f.Show();
        f.Activate();
    }

    private OverviewForm(DesktopManager mgr)
    {
        _mgr = mgr;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = BgColor;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        Native.GetCursorPos(out var pt);
        var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(pt.X, pt.Y)) ?? Screen.PrimaryScreen!;
        var b = screen.WorkingArea;
        Bounds = new Rectangle(b.Left + b.Width / 12, b.Top + b.Height / 12,
                               b.Width * 10 / 12, b.Height * 10 / 12);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Deactivate += (_, _) => Close();

        BuildUi();
    }

    private void BuildUi()
    {
        Controls.Clear();

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(22, 16, 22, 16)
        };
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = L.T("ov.title"),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 2)
        });
        root.Controls.Add(new Label
        {
            Text = L.T("ov.legend"),
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 14)
        });

        foreach (var mon in _mgr.GetLayout())
        {
            root.Controls.Add(new Label
            {
                Text = "🖥  " + L.F("ov.monitor", mon.Ordinal),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(4, 10, 0, 4)
            });

            var row = new BufferedPanel
            {
                AutoSize = true,
                AllowDrop = true,
                Padding = new Padding(4),
                Margin = new Padding(0, 0, 0, 8)
            };
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Location = new Point(4, 4)
            };
            row.Controls.Add(flow);

            bool rowHover = false;
            row.Paint += (_, e) =>
            {
                if (!rowHover) return;
                using var pen = new Pen(DropAccent, 2) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(pen, 1, 1, row.Width - 3, row.Height - 3);
            };
            row.DragEnter += (_, e) =>
            {
                if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d && d.Device != mon.Device)
                {
                    e.Effect = DragDropEffects.Move;
                    rowHover = true;
                    row.Invalidate();
                }
            };
            row.DragLeave += (_, _) => { rowHover = false; row.Invalidate(); };
            row.DragDrop += (_, e) =>
            {
                rowHover = false;
                if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d && d.Device != mon.Device)
                {
                    _mgr.MoveDesktopToMonitor(d.Device, d.LocalIndex, mon.Device);
                    BuildUi();
                }
            };
            root.Controls.Add(row);

            foreach (var desk in mon.Desktops)
                flow.Controls.Add(BuildDesktopCard(mon, desk));

            if (mon.Desktops.Count < DesktopManager.MaxDesktopsPerMonitor)
                flow.Controls.Add(BuildAddCard(mon.Device));
        }
    }

    // ---------- masaüstü kartı ----------

    private Control BuildDesktopCard(MonitorEntry mon, DesktopEntry desk)
    {
        var card = new BufferedPanel
        {
            Size = new Size(276, 216),
            BackColor = CardBg,
            Margin = new Padding(7),
            AllowDrop = true,
            Cursor = Cursors.Hand
        };

        bool dropHover = false;
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color border = dropHover ? DropAccent : desk.IsCurrent ? Accent : CardBorder;
            using var pen = new Pen(border, dropHover || desk.IsCurrent ? 2.5f : 1.5f);
            using var path = RoundedRect(new Rectangle(1, 1, card.Width - 3, card.Height - 3), 10);
            e.Graphics.DrawPath(pen, path);

            if (dropHover)
            {
                using var hint = new Font("Segoe UI", 10f, FontStyle.Bold);
                using var brush = new SolidBrush(DropAccent);
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                e.Graphics.DrawString(L.T("ov.drop"), hint, brush,
                    new Rectangle(0, card.Height - 28, card.Width, 24), sf);
            }
        };

        // Başlık: kartı monitörler arası taşımak için sürükleme tutamacıdır
        var headerLbl = new Label
        {
            Text = L.F("ov.desktop", desk.GlobalNumber),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(10, 9),
            AutoSize = true,
            Cursor = Cursors.SizeAll,
            BackColor = Color.Transparent
        };
        _tips.SetToolTip(headerLbl, L.T("ov.tip.header"));
        headerLbl.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                headerLbl.DoDragDrop(new DataObject(new DesktopDrag(mon.Device, desk.LocalIndex)), DragDropEffects.Move);
        };
        card.Controls.Add(headerLbl);

        if (desk.IsCurrent)
        {
            var badge = new Label
            {
                Text = L.T("ov.active"),
                ForeColor = Color.White,
                BackColor = Accent,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true,
                Padding = new Padding(5, 2, 5, 2)
            };
            card.Controls.Add(badge);
            badge.Location = new Point(card.Width - badge.PreferredSize.Width - 12, 10);
        }

        int y = 40;
        const int chipH = 30;
        int maxRows = (216 - 48) / (chipH + 4);
        foreach (var (win, i) in desk.Windows.Select((w, i) => (w, i)))
        {
            if (i >= maxRows - 1 && desk.Windows.Count > maxRows)
            {
                card.Controls.Add(new Label
                {
                    Text = L.F("ov.more", desk.Windows.Count - i),
                    ForeColor = TextDim,
                    Location = new Point(12, y + 4),
                    AutoSize = true,
                    BackColor = Color.Transparent
                });
                break;
            }
            card.Controls.Add(BuildWindowChip(win, new Point(10, y)));
            y += chipH + 4;
        }

        if (desk.Windows.Count == 0)
            card.Controls.Add(new Label
            {
                Text = L.T("ov.empty"),
                ForeColor = TextDim,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                Location = new Point(12, y + 4),
                AutoSize = true,
                BackColor = Color.Transparent
            });

        card.Click += (_, _) => { _mgr.SwitchTo(mon.Device, desk.LocalIndex); Close(); };

        card.DragEnter += (_, e) =>
        {
            if (e.Data?.GetData(typeof(WindowDrag)) is WindowDrag)
            {
                e.Effect = DragDropEffects.Move;
                dropHover = true;
                card.Invalidate();
            }
        };
        card.DragLeave += (_, _) => { dropHover = false; card.Invalidate(); };
        card.DragDrop += (_, e) =>
        {
            dropHover = false;
            if (e.Data?.GetData(typeof(WindowDrag)) is WindowDrag w)
            {
                _mgr.MoveWindowToDesktop(w.Handle, mon.Device, desk.LocalIndex);
                BuildUi();
            }
        };
        return card;
    }

    // ---------- pencere kutucuğu ----------

    private Control BuildWindowChip(WindowEntry win, Point location)
    {
        var chip = new BufferedPanel
        {
            Location = location,
            Size = new Size(256, 30),
            BackColor = ChipBg,
            Cursor = Cursors.SizeAll
        };
        chip.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(ChipBorder, 1);
            using var path = RoundedRect(new Rectangle(0, 0, chip.Width - 1, chip.Height - 1), 7);
            e.Graphics.DrawPath(pen, path);
        };

        var grip = new Label
        {
            Text = "⠿",
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 11f),
            Location = new Point(7, 6),
            AutoSize = true,
            BackColor = Color.Transparent,
            Cursor = Cursors.SizeAll
        };
        chip.Controls.Add(grip);

        int textX = 26;
        var icon = Native.GetWindowSmallIcon(win.Handle);
        if (icon != null)
        {
            chip.Controls.Add(new PictureBox
            {
                Image = icon.ToBitmap(),
                Size = new Size(16, 16),
                Location = new Point(26, 7),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Transparent,
                Enabled = false
            });
            textX = 48;
        }

        var titleLbl = new Label
        {
            Text = win.Title.Length > 30 ? win.Title[..30] + "…" : win.Title,
            ForeColor = Color.FromArgb(225, 225, 235),
            Font = new Font("Segoe UI", 9.2f),
            Location = new Point(textX, 7),
            Size = new Size(chip.Width - textX - 6, 18),
            BackColor = Color.Transparent,
            Cursor = Cursors.SizeAll
        };
        chip.Controls.Add(titleLbl);

        _tips.SetToolTip(chip, L.T("ov.tip.window"));
        _tips.SetToolTip(titleLbl, L.T("ov.tip.window"));

        void Hover(bool on) => chip.BackColor = on ? ChipHover : ChipBg;
        void OnDown(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                chip.DoDragDrop(new DataObject(new WindowDrag(win.Handle)), DragDropEffects.Move);
        }
        void OnUp(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                ShowWindowMenu(win, chip);
        }

        foreach (Control c in new Control[] { chip, grip, titleLbl })
        {
            c.MouseEnter += (_, _) => Hover(true);
            c.MouseLeave += (_, _) => Hover(false);
            c.MouseDown += OnDown;
            c.MouseUp += OnUp;
        }
        return chip;
    }

    /// <summary>Pencereye sağ tık: her monitörün her masaüstüne taşıma menüsü
    /// (görev çubuğu menüsü genişletilemediği için karşılığı burasıdır).</summary>
    private void ShowWindowMenu(WindowEntry win, Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var mon in _mgr.GetLayout())
            foreach (var desk in mon.Desktops)
            {
                string label = L.F("ov.menu.move", mon.Ordinal, desk.GlobalNumber) +
                               (desk.IsCurrent ? L.T("ov.menu.activeSuffix") : "");
                var (device, local) = (mon.Device, desk.LocalIndex);
                menu.Items.Add(label, null, (_, _) =>
                {
                    _mgr.MoveWindowToDesktop(win.Handle, device, local);
                    BuildUi();
                });
            }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("ov.menu.goto"), null, (_, _) =>
        {
            Close();
            Native.SetForegroundWindow(win.Handle);
        });
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    // ---------- yeni masaüstü kartı ----------

    private Control BuildAddCard(string device)
    {
        var card = new BufferedPanel
        {
            Size = new Size(72, 216),
            BackColor = BgColor,
            Margin = new Padding(7),
            Cursor = Cursors.Hand
        };
        bool hover = false;
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(hover ? Accent : CardBorder, 1.5f) { DashStyle = DashStyle.Dash };
            using var path = RoundedRect(new Rectangle(1, 1, card.Width - 3, card.Height - 3), 10);
            e.Graphics.DrawPath(pen, path);
            using var font = new Font("Segoe UI", 20f);
            using var brush = new SolidBrush(hover ? Accent : TextDim);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("+", font, brush, card.ClientRectangle, sf);
        };
        _tips.SetToolTip(card, L.T("ov.tip.add"));
        card.MouseEnter += (_, _) => { hover = true; card.Invalidate(); };
        card.MouseLeave += (_, _) => { hover = false; card.Invalidate(); };
        card.Click += (_, _) => { _mgr.CreateDesktopAndSwitch(device); BuildUi(); };
        return card;
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }
}
