using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace IndepenDesk;

/// <summary>
/// "Nasıl kullanılır" penceresi: kısayollar, touchpad kurulum adımları ve
/// hareketleri gösteren döngülü animasyon (4 parmak sola/sağa/yukarı kaydırma).
/// </summary>
internal sealed class HelpForm : Form
{
    private static HelpForm? _open;

    public static void ShowHelp()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new HelpForm();
        _open.FormClosed += (_, _) => _open = null;
        _open.Show();
    }

    private HelpForm()
    {
        Text = L.T("help.title");
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 24, 28);
        ClientSize = new Size(720, 640);
        Font = new Font("Segoe UI", 9.5f);

        int y = 18;
        y = AddHeader(L.T("help.shortcuts.header"), y);
        y = AddBody(L.T("help.shortcuts.body").Replace("&&", "&"), y);

        y = AddHeader(L.T("help.demo.header"), y);
        var demo = new GesturePanel { Location = new Point(24, y), Size = new Size(672, 190) };
        Controls.Add(demo);
        y += 196;

        y = AddHeader(L.T("help.touchpad.header"), y);
        y = AddBody(L.T("help.touchpad.body").Replace("&&", "&"), y);

        var openBtn = new Button
        {
            Text = L.T("help.touchpad.open"),
            Location = new Point(24, y + 4),
            Size = new Size(260, 34),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(60, 110, 180)
        };
        openBtn.FlatAppearance.BorderSize = 0;
        openBtn.Click += (_, _) =>
            Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true });
        Controls.Add(openBtn);

        var closeBtn = new Button
        {
            Text = L.T("help.close"),
            Location = new Point(596, y + 4),
            Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(60, 60, 70)
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.Click += (_, _) => Close();
        Controls.Add(closeBtn);

        ClientSize = new Size(720, y + 54);
    }

    private int AddHeader(string text, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            ForeColor = Color.FromArgb(120, 170, 235),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            Location = new Point(22, y),
            AutoSize = true
        });
        return y + 30;
    }

    private int AddBody(string text, int y)
    {
        var lbl = new Label
        {
            Text = text,
            ForeColor = Color.FromArgb(215, 215, 225),
            Location = new Point(24, y),
            MaximumSize = new Size(672, 0),
            AutoSize = true
        };
        Controls.Add(lbl);
        return y + lbl.GetPreferredSize(new Size(672, 0)).Height + 14;
    }

    /// <summary>Touchpad hareketlerini döngü halinde canlandıran panel:
    /// 4 parmak sağa → masaüstü kayar; sola → geri; yukarı → genel bakış ızgarası.</summary>
    private sealed class GesturePanel : Panel
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
        private double _t; // 0..1 faz ilerlemesi
        private int _phase; // 0: sağa, 1: sola, 2: yukarı

        public GesturePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(30, 30, 36);
            _timer.Tick += (_, _) =>
            {
                _t += 0.012;
                if (_t >= 1.4) { _t = 0; _phase = (_phase + 1) % 3; } // 1.0 sonrası bekleme payı
                Invalidate();
            };
            _timer.Start();
            Disposed += (_, _) => _timer.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double p = Math.Min(1.0, _t);
            double ease = 1 - Math.Pow(1 - p, 3);

            // Sol: touchpad ve parmaklar
            var pad = new Rectangle(30, 30, 180, 130);
            using (var padBrush = new SolidBrush(Color.FromArgb(48, 48, 58)))
            using (var padPen = new Pen(Color.FromArgb(90, 90, 105), 2))
            {
                g.FillRoundedRectangleCompat(padBrush, pad, 16);
                g.DrawRoundedRectangleCompat(padPen, pad, 16);
            }

            // 4 parmak noktası: faza göre hareket yönü
            double dx = 0, dy = 0;
            switch (_phase)
            {
                case 0: dx = ease * 70; break;
                case 1: dx = -ease * 70; break;
                case 2: dy = -ease * 60; break;
            }
            using (var finger = new SolidBrush(Color.FromArgb(130, 185, 245)))
            using (var trail = new SolidBrush(Color.FromArgb(60, 130, 185, 245)))
                for (int i = 0; i < 4; i++)
                {
                    int fx = pad.Left + 45 + i * 26;
                    int fy = pad.Top + 70;
                    g.FillEllipse(trail, (int)(fx + dx * 0.55) - 8, (int)(fy + dy * 0.55) - 8, 16, 16);
                    g.FillEllipse(finger, (int)(fx + dx) - 8, (int)(fy + dy) - 8, 16, 16);
                }

            // Yön oku
            using (var arrowPen = new Pen(Color.FromArgb(130, 185, 245), 3) { EndCap = LineCap.ArrowAnchor })
            {
                int cx = pad.Left + 90, cy = pad.Top - 8;
                switch (_phase)
                {
                    case 0: g.DrawLine(arrowPen, cx - 30, cy, cx + 30, cy); break;
                    case 1: g.DrawLine(arrowPen, cx + 30, cy, cx - 30, cy); break;
                    case 2: g.DrawLine(arrowPen, pad.Right + 0, pad.Top + 95, pad.Right + 0, pad.Top + 35); break;
                }
            }

            // Sağ: mini monitör ve etki
            var mon = new Rectangle(280, 22, 220, 132);
            using (var monPen = new Pen(Color.FromArgb(120, 120, 135), 2))
            {
                g.DrawRoundedRectangleCompat(monPen, mon, 8);
                g.DrawLine(monPen, mon.Left + 85, mon.Bottom + 8, mon.Right - 85, mon.Bottom + 8);
            }
            var inner = Rectangle.Inflate(mon, -6, -6);
            g.SetClip(inner);

            if (_phase == 2)
            {
                // Genel bakış: ızgara halinde küçülen kartlar
                for (int i = 0; i < 4; i++)
                {
                    int gx = inner.Left + 12 + (i % 2) * 100;
                    int gy = inner.Top + 12 + (i / 2) * 58;
                    int w = (int)(84 * (0.55 + 0.45 * (1 - ease)));
                    int h = (int)(46 * (0.55 + 0.45 * (1 - ease)));
                    using var cardB = new SolidBrush(Color.FromArgb(64, 96, 138));
                    g.FillRoundedRectangleCompat(cardB, new Rectangle(gx, gy, Math.Max(w, 46), Math.Max(h, 25)), 6);
                }
            }
            else
            {
                // Masaüstü kayması: iki renkli sahne yana kayar
                int shift = (int)(ease * inner.Width) * (_phase == 0 ? -1 : 1);
                using var d1 = new SolidBrush(Color.FromArgb(64, 96, 138));
                using var d2 = new SolidBrush(Color.FromArgb(76, 128, 96));
                g.FillRectangle(d1, inner.Left + shift, inner.Top, inner.Width, inner.Height);
                g.FillRectangle(d2, inner.Left + shift + (_phase == 0 ? inner.Width : -inner.Width),
                    inner.Top, inner.Width, inner.Height);
                using var winB = new SolidBrush(Color.FromArgb(190, 205, 225));
                g.FillRoundedRectangleCompat(winB, new Rectangle(inner.Left + shift + 20, inner.Top + 22, 74, 50), 4);
            }
            g.ResetClip();

            // Alt yazı
            string caption = _phase == 2 ? L.T("help.demo.swipeUp") : L.T("help.demo.swipeLR");
            using var capFont = new Font("Segoe UI", 9.5f);
            using var capBrush = new SolidBrush(Color.FromArgb(200, 200, 212));
            g.DrawString(caption, capFont, capBrush, new Rectangle(10, Height - 26, Width - 20, 22),
                new StringFormat { Alignment = StringAlignment.Center });
        }
    }
}

/// <summary>GDI+ için yuvarlatılmış dikdörtgen yardımcıları.</summary>
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangleCompat(this Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = Rounded(r, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangleCompat(this Graphics g, Pen pen, Rectangle r, int radius)
    {
        using var path = Rounded(r, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
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
}
