using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WindicatorPlus
{
    // Modern iOS / Fluent Style Large Purple Toggle Switch
    public class DarkToggleSwitch : Control
    {
        private bool _checked = false;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler CheckedChanged;

        public DarkToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw, true);

            Size = new Size(54, 28); // Enlarged 40%
            MinimumSize = new Size(54, 28);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(54, 28);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = Width - 2;
            int h = Height - 2;
            int radius = h / 2;

            Color trackColor = _checked ? Color.FromArgb(139, 92, 246) : Color.FromArgb(46, 50, 62);
            using (GraphicsPath path = CreatePillPath(new Rectangle(1, 1, w, h), radius))
            {
                using (SolidBrush brush = new SolidBrush(trackColor))
                {
                    g.FillPath(brush, path);
                }

                if (!_checked)
                {
                    using (Pen pen = new Pen(Color.FromArgb(70, 75, 92), 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // Knob circle
            int knobSize = h - 6;
            int knobX = _checked ? (w - knobSize - 2) : 4;
            int knobY = 4;

            Rectangle knobRect = new Rectangle(knobX, knobY, knobSize, knobSize);
            using (SolidBrush knobBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(knobBrush, knobRect);
            }
        }

        private GraphicsPath CreatePillPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Aitiy Style Large Nav Button
    public class DarkNavButton : Control
    {
        private bool _isSelected = false;
        private bool _isHovered = false;

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    Invalidate();
                }
            }
        }

        public DarkNavButton(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);

            Text = text;
            Size = new Size(216, 46); // Enlarged
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular); // 12pt Large
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = 10;

            if (_isSelected)
            {
                using (GraphicsPath path = CreateRoundedRect(rect, radius))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(46, 36, 72)))
                    {
                        g.FillPath(bg, path);
                    }
                    using (Pen border = new Pen(Color.FromArgb(100, 75, 150), 1.2f))
                    {
                        g.DrawPath(border, path);
                    }
                }

                // Purple dot (8px)
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(167, 120, 255)))
                {
                    g.FillEllipse(dotBrush, 16, (Height - 8) / 2, 8, 8);
                }
            }
            else
            {
                if (_isHovered)
                {
                    using (GraphicsPath path = CreateRoundedRect(rect, radius))
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(32, 36, 48)))
                    {
                        g.FillPath(bg, path);
                    }
                }

                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(80, 85, 100)))
                {
                    g.FillEllipse(dotBrush, 16, (Height - 8) / 2, 8, 8);
                }
            }

            Color textColor = _isSelected ? Color.White : (_isHovered ? Color.FromArgb(240, 245, 255) : Color.FromArgb(160, 166, 180));
            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                g.DrawString(Text, Font, textBrush, new PointF(34, (Height - Font.Height) / 2.0f));
            }
        }

        private GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Modern Slim Dark ScrollBar (Deep charcoal & vibrant purple, eliminates eye-straining white scrollbars)
    public class ModernDarkScrollBar : Control
    {
        private int _min = 0;
        private int _max = 100;
        private int _value = 0;
        private int _largeChange = 10;
        private bool _isHover = false;
        private bool _isDragging = false;
        private int _dragStartY;
        private int _dragStartValue;

        public event EventHandler ValueChanged;

        public int Minimum { get { return _min; } set { _min = value; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = value; Invalidate(); } }
        public int LargeChange { get { return _largeChange; } set { _largeChange = value; Invalidate(); } }
        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_min, Math.Min(_max, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernDarkScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Width = 10;
            BackColor = Color.FromArgb(14, 15, 20);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _isHover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _isHover = false; Invalidate(); }

        private Rectangle GetThumbRect()
        {
            int range = _max - _min;
            if (range <= 0 || Height <= 0) return Rectangle.Empty;

            int trackH = Height;
            int thumbH = Math.Max(28, (int)((float)_largeChange / (range + _largeChange) * trackH));
            if (thumbH >= trackH) return Rectangle.Empty;

            int availTrack = trackH - thumbH;
            int thumbY = (int)((float)(_value - _min) / range * availTrack);

            return new Rectangle(2, thumbY, Width - 4, thumbH);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Rectangle thumb = GetThumbRect();
                if (thumb.Contains(e.Location))
                {
                    _isDragging = true;
                    _dragStartY = e.Y;
                    _dragStartValue = _value;
                }
                else
                {
                    int trackH = Height;
                    int range = _max - _min;
                    if (range > 0 && trackH > 0)
                    {
                        float ratio = (float)e.Y / trackH;
                        Value = _min + (int)(ratio * range);
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                int deltaY = e.Y - _dragStartY;
                int trackH = Height;
                Rectangle thumb = GetThumbRect();
                int availTrack = trackH - thumb.Height;
                if (availTrack > 0)
                {
                    int range = _max - _min;
                    int deltaVal = (int)((float)deltaY / availTrack * range);
                    Value = _dragStartValue + deltaVal;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle thumb = GetThumbRect();
            if (thumb.IsEmpty || thumb.Height <= 0) return;

            Color thumbColor;
            if (_isDragging) thumbColor = Color.FromArgb(167, 139, 250);
            else if (_isHover) thumbColor = Color.FromArgb(124, 58, 237);
            else thumbColor = Color.FromArgb(60, 65, 82);

            using (GraphicsPath path = new GraphicsPath())
            {
                int radius = Math.Max(2, thumb.Width / 2);
                int d = radius * 2;
                path.AddArc(thumb.X, thumb.Y, d, d, 180, 90);
                path.AddArc(thumb.Right - d, thumb.Y, d, d, 270, 90);
                path.AddArc(thumb.Right - d, thumb.Bottom - d, d, d, 0, 90);
                path.AddArc(thumb.X, thumb.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                using (SolidBrush b = new SolidBrush(thumbColor))
                {
                    g.FillPath(b, path);
                }
            }
        }
    }

    // High performance double buffered panel to eliminate flicker and residue
    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int totalHeight = 0;
            int maxWidth = 0;

            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int childH = c.Height;
                if (childH <= 0)
                {
                    childH = c.GetPreferredSize(proposedSize).Height;
                }
                totalHeight += childH;
                maxWidth = Math.Max(maxWidth, c.Width);
            }

            int h = totalHeight + Padding.Vertical;
            int w = proposedSize.Width > 0 ? proposedSize.Width : (maxWidth + Padding.Horizontal);
            return new Size(w, h);
        }
    }

    // Aitiy Style Spacious Rounded Card Panel
    public class DarkCardPanel : Panel
    {
        public DarkCardPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            BackColor = Color.FromArgb(24, 26, 33);
            Padding = new Padding(28, 24, 28, 24); // Generous padding
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int totalHeight = 0;
            int maxWidth = 0;

            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int childH = c.Height;
                if (childH <= 0)
                {
                    childH = c.GetPreferredSize(proposedSize).Height;
                }
                totalHeight += childH;
                maxWidth = Math.Max(maxWidth, c.Width);
            }

            int h = totalHeight + Padding.Vertical;
            int w = proposedSize.Width > 0 ? proposedSize.Width : (maxWidth + Padding.Horizontal);
            return new Size(w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = 12;

            using (GraphicsPath path = CreateRoundedRect(rect, radius))
            {
                using (SolidBrush bg = new SolidBrush(BackColor))
                {
                    g.FillPath(bg, path);
                }
                using (Pen border = new Pen(Color.FromArgb(44, 48, 62), 1.2f))
                {
                    g.DrawPath(border, path);
                }
            }
            base.OnPaint(e);
        }

        private GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
