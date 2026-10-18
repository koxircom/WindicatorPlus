using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindicatorPlus
{
    /// <summary>
    /// 轻量独立角标窗体。
    /// 遵循规格：
    /// 1. 每屏 >= 2 桌面时激活，count <= 1 隐藏。
    /// 2. WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW 全穿透无激活。
    /// 3. 不启动任何 15ms 监控或任务栏钩子，纯按需重绘。
    /// 4. 210x72 DIP 基础尺寸，右下 22 DIP 边距，基于 Shcore 动态多屏 DPI 与工作区钳位（支持负坐标）。
    /// 5. 圆角 Region 裁切避免四角遮挡，Accent 色彩条标识独立桌面，文字自适应防溢出且保留本地编号。
    /// </summary>
    public class IndependentBadgeForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
            public POINT(int x, int y) { this.x = x; this.y = y; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private IndepenDeskMonitorInfo _monitorInfo;
        private string _customName;
        private string _accentColorHex;
        private int _totalMonitors = 1;
        private AppConfig _config;
        private Screen _targetScreen;

        private Color _accentColor = Color.FromArgb(37, 99, 235);
        private int _titleFontSize = 14;
        private float _dpiScale = 1.0f;
        private string _renderedMainTitle = string.Empty;
        private string _renderedSubTitle = string.Empty;
        private Rectangle _lastBounds = Rectangle.Empty;

        public IndependentBadgeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            UpdateStyles();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                           | NativeMethods.WS_EX_TRANSPARENT
                           | NativeMethods.WS_EX_NOACTIVATE
                           | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public IndepenDeskMonitorInfo MonitorInfo
        {
            get { return _monitorInfo; }
        }

        public Screen TargetScreen
        {
            get { return _targetScreen; }
        }

        public float DpiScale
        {
            get { return _dpiScale; }
        }

        public static float GetScreenDpiScale(Screen screen)
        {
            if (screen == null) return 1.0f;
            try
            {
                POINT pt = new POINT(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
                IntPtr hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    int hr = GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                    if (hr == 0 && dpiX > 0)
                    {
                        return dpiX / 96.0f;
                    }
                }
            }
            catch
            {
                // PMv2 fallback: shcore 缺失或平台异常时降级
            }
            return 1.0f;
        }

        public static string FormatMainTitle(IndepenDeskMonitorInfo mon, string customName)
        {
            if (mon == null) return string.Empty;
            int localNum = mon.Index + 1;
            if (!string.IsNullOrEmpty(customName))
            {
                return string.Format("{0} {1}", localNum, customName.Trim());
            }
            return string.Format("桌面 {0}", localNum);
        }

        public static string FormatSubTitle(IndepenDeskMonitorInfo mon, int totalMonitors)
        {
            if (mon == null) return string.Empty;
            return string.Format("显示器 {0}/{1} · 本地 {2}/{3}",
                mon.Ordinal,
                Math.Max(1, totalMonitors),
                mon.Index + 1,
                mon.Count);
        }

        public static void CalculateTextLayout(Rectangle clientRect, float dpiScale, float scaleMult, SizeF titleMeasure, SizeF subMeasure,
            out Rectangle barRect, out Rectangle titleRect, out Rectangle subRect)
        {
            int padY = Math.Max(4, (int)Math.Ceiling(8 * dpiScale * scaleMult));
            int padX = Math.Max(4, (int)Math.Ceiling(10 * dpiScale * scaleMult));
            int barW = Math.Max(4, (int)Math.Ceiling(6 * dpiScale * scaleMult));
            int barSpacing = Math.Max(4, (int)Math.Ceiling(8 * dpiScale * scaleMult));

            barRect = new Rectangle(padX, padY, barW, Math.Max(10, clientRect.Height - padY * 2));

            int textLeft = barRect.Right + barSpacing;
            int textWidth = Math.Max(20, clientRect.Width - textLeft - padX);

            int titleH = (int)Math.Ceiling(titleMeasure.Height);
            int subH = (int)Math.Ceiling(subMeasure.Height);
            int vGap = Math.Max(2, (int)Math.Ceiling(3 * dpiScale));

            // 垂直居中排布文本区
            int totalTextH = titleH + vGap + subH;
            int startY = padY;
            if (clientRect.Height > totalTextH + padY * 2)
            {
                startY = (clientRect.Height - totalTextH) / 2;
            }

            titleRect = new Rectangle(textLeft, startY, textWidth, titleH);
            subRect = new Rectangle(textLeft, titleRect.Bottom + vGap, textWidth, subH);

            // 保证副标题底部不超出 clientRect
            if (subRect.Bottom > clientRect.Height - 2)
            {
                int overflow = subRect.Bottom - (clientRect.Height - 2);
                titleRect.Y = Math.Max(0, titleRect.Y - overflow);
                subRect.Y = Math.Max(titleRect.Bottom, subRect.Y - overflow);
            }
        }

        public static Rectangle CalculateBadgeBounds(Rectangle workingArea, float dpiScale, float scaleMult, SizeF titleMeasure, SizeF subMeasure)
        {
            if (dpiScale <= 0.1f) dpiScale = 1.0f;
            if (scaleMult <= 0.1f) scaleMult = 1.0f;

            int basePhysW = (int)Math.Ceiling(210 * dpiScale * scaleMult);
            int basePhysH = (int)Math.Ceiling(72 * dpiScale * scaleMult);

            int padY = Math.Max(4, (int)Math.Ceiling(8 * dpiScale * scaleMult));
            int padX = Math.Max(4, (int)Math.Ceiling(10 * dpiScale * scaleMult));
            int barW = Math.Max(4, (int)Math.Ceiling(6 * dpiScale * scaleMult));
            int barSpacing = Math.Max(4, (int)Math.Ceiling(8 * dpiScale * scaleMult));
            int vGap = Math.Max(2, (int)Math.Ceiling(3 * dpiScale));

            int neededH = padY + (int)Math.Ceiling(titleMeasure.Height) + vGap + (int)Math.Ceiling(subMeasure.Height) + padY;
            int maxTextW = (int)Math.Ceiling(Math.Max(titleMeasure.Width, subMeasure.Width));
            int neededW = padX + barW + barSpacing + maxTextW + padX + (int)Math.Ceiling(4 * dpiScale);

            int physWidth = Math.Max(basePhysW, neededW);
            int physHeight = Math.Max(basePhysH, neededH);
            int physMargin = (int)Math.Ceiling(22 * dpiScale * scaleMult);

            // 限制窗体不超过工作区最大可用尺寸
            if (physWidth > workingArea.Width) physWidth = workingArea.Width;
            if (physHeight > workingArea.Height) physHeight = workingArea.Height;

            // 默认对齐工作区右下角
            int x = workingArea.Right - physWidth - physMargin;
            int y = workingArea.Bottom - physHeight - physMargin;

            // 针对负坐标或超边界情况执行严格边界钳位
            if (x < workingArea.Left) x = workingArea.Left;
            if (x + physWidth > workingArea.Right) x = workingArea.Right - physWidth;

            if (y < workingArea.Top) y = workingArea.Top;
            if (y + physHeight > workingArea.Bottom) y = workingArea.Bottom - physHeight;

            return new Rectangle(x, y, physWidth, physHeight);
        }

        public void UpdateBadge(IndepenDeskMonitorInfo info, string customName, string accentColorHex, int totalMonitors, AppConfig config, Screen screen)
        {
            UpdateBadge(info, customName, accentColorHex, totalMonitors, config, screen, null, null);
        }

        public void UpdateBadge(IndepenDeskMonitorInfo info, string customName, string accentColorHex, int totalMonitors, AppConfig config, Screen screen, float? dpiOverride, Rectangle? workareaOverride)
        {
            _monitorInfo = info;
            _customName = customName;
            _accentColorHex = accentColorHex;
            _totalMonitors = Math.Max(1, totalMonitors);
            _config = config;
            _targetScreen = screen;

            if (_monitorInfo == null || _monitorInfo.Count <= 1 || (_targetScreen == null && !workareaOverride.HasValue))
            {
                if (Visible) Hide();
                return;
            }

            Rectangle workingArea = workareaOverride.HasValue
                ? workareaOverride.Value
                : _targetScreen.WorkingArea;

            // DPI 换算
            float dpiScale = dpiOverride.HasValue ? dpiOverride.Value : GetScreenDpiScale(_targetScreen);
            if (dpiScale < 0.5f) dpiScale = 1.0f;
            _dpiScale = dpiScale;

            // 解析强调色
            Color newAccent = Color.FromArgb(37, 99, 235);
            if (!string.IsNullOrEmpty(_accentColorHex))
            {
                try
                {
                    newAccent = ColorTranslator.FromHtml(_accentColorHex);
                }
                catch { }
            }

            // 字号（14..48 逻辑 px）与透明度
            int opacityVal = 85;
            int newFontSize = 14;
            float scaleMult = 1.0f;
            if (_config != null)
            {
                if (_config.WatermarkOpacity >= 5 && _config.WatermarkOpacity <= 100)
                {
                    opacityVal = _config.WatermarkOpacity;
                }
                if (_config.TitleFontSize >= 14 && _config.TitleFontSize <= 48)
                {
                    newFontSize = _config.TitleFontSize;
                }
                if (_config.WatermarkScale >= 70 && _config.WatermarkScale <= 180)
                {
                    scaleMult = _config.WatermarkScale / 100.0f;
                }
            }

            string mainTitle = FormatMainTitle(_monitorInfo, _customName);
            string subTitle = FormatSubTitle(_monitorInfo, _totalMonitors);

            // 统一使用 Pixel 字体真实测量尺寸
            float titlePx = newFontSize * dpiScale;
            float subPx = 10.5f * dpiScale;
            SizeF titleMeasure;
            SizeF subMeasure;
            using (Bitmap dummyBmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(dummyBmp))
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (Font tFont = new Font("Microsoft YaHei", titlePx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font sFont = new Font("Microsoft YaHei", subPx, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    titleMeasure = g.MeasureString(mainTitle, tFont);
                    subMeasure = g.MeasureString(subTitle, sFont);
                }
            }

            Rectangle bounds = CalculateBadgeBounds(workingArea, dpiScale, scaleMult, titleMeasure, subMeasure);
            double targetOpacity = Math.Max(0.10, Math.Min(1.0, opacityVal / 100.0));

            // 变更检测：只有必要几何或视觉要素变化时才执行更新重绘
            bool boundsChanged = bounds != _lastBounds;
            bool contentChanged = mainTitle != _renderedMainTitle ||
                                  subTitle != _renderedSubTitle ||
                                  newAccent != _accentColor ||
                                  newFontSize != _titleFontSize ||
                                  Math.Abs(Opacity - targetOpacity) > 0.01;

            if (boundsChanged || contentChanged)
            {
                _accentColor = newAccent;
                _titleFontSize = newFontSize;
                _renderedMainTitle = mainTitle;
                _renderedSubTitle = subTitle;
                _lastBounds = bounds;
                Opacity = targetOpacity;

                if (boundsChanged)
                {
                    SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    ApplyRoundedRegion(bounds.Width, bounds.Height, (int)(10 * dpiScale));
                }

                Invalidate();
            }
        }

        private void ApplyRoundedRegion(int width, int height, int radius)
        {
            if (width <= 0 || height <= 0) return;
            try
            {
                using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, width, height), radius))
                {
                    Region oldRegion = this.Region;
                    this.Region = new Region(path);
                    if (oldRegion != null)
                    {
                        oldRegion.Dispose();
                    }
                }
            }
            catch
            {
                // 若裁切异常保留矩形
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_monitorInfo == null || _monitorInfo.Count <= 1)
                return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle clientRect = ClientRectangle;
            if (clientRect.Width <= 0 || clientRect.Height <= 0) return;

            // 1. 卡片深色底色绘制
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(240, 20, 22, 28)))
            {
                g.FillRectangle(bgBrush, clientRect);
            }

            // 2. 边框微光感
            using (Pen borderPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, clientRect.Width - 1, clientRect.Height - 1);
            }

            // 3. 字体设置与真实布局测量
            float titlePx = _titleFontSize * _dpiScale;
            float subPx = 10.5f * _dpiScale;
            float scaleMult = (_config != null && _config.WatermarkScale >= 70 && _config.WatermarkScale <= 180)
                ? _config.WatermarkScale / 100.0f
                : 1.0f;

            using (Font titleFont = new Font("Microsoft YaHei", titlePx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font subFont = new Font("Microsoft YaHei", subPx, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF titleMeasure = g.MeasureString(_renderedMainTitle, titleFont);
                SizeF subMeasure = g.MeasureString(_renderedSubTitle, subFont);

                Rectangle barRect, titleRect, subRect;
                CalculateTextLayout(clientRect, _dpiScale, scaleMult, titleMeasure, subMeasure, out barRect, out titleRect, out subRect);

                // 4. 左侧 Accent 强调色块（带微圆角）
                using (SolidBrush barBrush = new SolidBrush(_accentColor))
                using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, 3))
                {
                    g.FillPath(barBrush, barPath);
                }

                // 5. 文字绘制：主标题加粗纯白，副标题清晰中灰，长名水平 Ellipsis 但垂直无裁切
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Near;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.FormatFlags = StringFormatFlags.NoWrap;

                    using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(250, 250, 250)))
                    {
                        g.DrawString(_renderedMainTitle, titleFont, titleBrush, titleRect, sf);
                    }

                    using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(160, 168, 180)))
                    {
                        g.DrawString(_renderedSubTitle, subFont, subBrush, subRect, sf);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.Region != null)
                {
                    try { this.Region.Dispose(); } catch { }
                    this.Region = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
