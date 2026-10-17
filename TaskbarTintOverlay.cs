using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindicatorPlus
{
    // Taskbar Tint Overlay: 100% mouse-penetrating (WS_EX_TRANSPARENT) layered window
    // that subtly tints the Windows 11 taskbar with the active virtual desktop's theme color.
    public class TaskbarTintOverlay : Form
    {
        private bool _enabled = true;
        private int _opacityPercent = 18; // 5 ~ 40
        private int _currentDesktop = 1;
        private AppConfig _config;

        private Rectangle _lastTaskbarRect = Rectangle.Empty;
        private Timer _syncTimer;
        private Func<Rectangle> _taskbarRectProvider = null;

        internal Func<Rectangle> TaskbarRectProvider
        {
            get { return _taskbarRectProvider; }
            set { _taskbarRectProvider = value; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                           | NativeMethods.WS_EX_TRANSPARENT
                           | NativeMethods.WS_EX_TOPMOST
                           | NativeMethods.WS_EX_TOOLWINDOW
                           | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public TaskbarTintOverlay(AppConfig config, int initialDesktop)
            : this(config, initialDesktop, null)
        {
        }

        internal TaskbarTintOverlay(AppConfig config, int initialDesktop, Func<Rectangle> taskbarRectProvider)
        {
            _taskbarRectProvider = taskbarRectProvider;
            _config = config ?? new AppConfig();
            _currentDesktop = initialDesktop > 0 ? initialDesktop : 1;
            _enabled = _config.TaskbarTintEnabled;
            _opacityPercent = Math.Max(5, Math.Min(40, _config.TaskbarTintOpacity));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            // Timer to follow taskbar geometry changes (resolution, multi-monitor, auto-hide)
            _syncTimer = new Timer();
            _syncTimer.Interval = 500;
            _syncTimer.Tick += (s, e) => CheckTaskbarGeometry();
            _syncTimer.Start();
        }

        public void ApplyConfig(AppConfig config, int currentDesktop)
        {
            _config = config ?? _config;
            _currentDesktop = currentDesktop > 0 ? currentDesktop : _currentDesktop;
            _enabled = _config.TaskbarTintEnabled;
            _opacityPercent = Math.Max(5, Math.Min(40, _config.TaskbarTintOpacity));

            if (_enabled)
            {
                if (!IsHandleCreated)
                {
                    CreateControl();
                }
                if (!Visible)
                {
                    Show();
                }
                if (IsHandleCreated && !IsDisposed)
                {
                    RenderTintOverlay();
                }
            }
            else
            {
                ClearLayeredWindow();
                Hide();
            }
        }

        internal void SetOverlayEnabled(bool enabled)
        {
            _config.TaskbarTintEnabled = enabled;
            ApplyConfig(_config, _currentDesktop);
        }

        public void UpdateDesktop(int currentDesktop)
        {
            if (_currentDesktop == currentDesktop) return;
            _currentDesktop = currentDesktop;
            if (IsHandleCreated && !IsDisposed && _enabled)
            {
                RenderTintOverlay();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RenderTintOverlay();
        }

        private void CheckTaskbarGeometry()
        {
            if (!_enabled || !IsHandleCreated || IsDisposed) return;

            Rectangle tbRect = GetTaskbarRect();
            if (tbRect != _lastTaskbarRect)
            {
                _lastTaskbarRect = tbRect;
                RenderTintOverlay();
            }
        }

        private Rectangle GetTaskbarRect()
        {
            if (_taskbarRectProvider != null)
            {
                return _taskbarRectProvider();
            }

            IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTaskbar != IntPtr.Zero)
            {
                NativeMethods.RECT rc;
                if (NativeMethods.GetWindowRect(hTaskbar, out rc))
                {
                    if (rc.Right > rc.Left && rc.Bottom > rc.Top)
                    {
                        return Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
                    }
                }
            }
            return Rectangle.Empty;
        }

        public void RenderTintOverlay()
        {
            if (!IsHandleCreated || IsDisposed) return;

            if (!_enabled)
            {
                ClearLayeredWindow();
                return;
            }

            Rectangle tbRect = GetTaskbarRect();
            if (tbRect.IsEmpty || tbRect.Width <= 0 || tbRect.Height <= 0)
            {
                ClearLayeredWindow();
                return;
            }

            _lastTaskbarRect = tbRect;

            string hexColor = _config.GetTaskbarTintColor(_currentDesktop);
            Color baseColor = ParseHexColor(hexColor, Color.FromArgb(124, 58, 237)); // Default violet

            byte alpha = (byte)Math.Max(10, Math.Min(120, (int)Math.Round(_opacityPercent * 2.55f)));

            int w = tbRect.Width;
            int h = tbRect.Height;

            Bitmap bmp = null;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B)))
                    {
                        g.FillRectangle(b, 0, 0, w, h);
                    }
                    g.Flush();
                }

                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.POINT ptDst = new NativeMethods.POINT(tbRect.Left, tbRect.Top);
                NativeMethods.SIZE size = new NativeMethods.SIZE(w, h);
                NativeMethods.POINT ptSrc = new NativeMethods.POINT(0, 0);

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                NativeMethods.UpdateLayeredWindow(
                    Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            catch { }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
                if (memDc != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(memDc);
                }
                if (screenDc != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
                if (bmp != null) bmp.Dispose();
            }
        }

        private void ClearLayeredWindow()
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            Bitmap bmp = null;
            try
            {
                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.POINT ptDst = new NativeMethods.POINT(Left, Top);
                NativeMethods.SIZE size = new NativeMethods.SIZE(1, 1);
                NativeMethods.POINT ptSrc = new NativeMethods.POINT(0, 0);

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 0;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                NativeMethods.UpdateLayeredWindow(
                    Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            catch { }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
                if (bmp != null)
                {
                    bmp.Dispose();
                }
                if (memDc != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(memDc);
                }
                if (screenDc != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        public static Color ParseHexColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string clean = hex.Trim().TrimStart('#');
            if (clean.Length == 6)
            {
                try
                {
                    int r = Convert.ToInt32(clean.Substring(0, 2), 16);
                    int g = Convert.ToInt32(clean.Substring(2, 2), 16);
                    int b = Convert.ToInt32(clean.Substring(4, 2), 16);
                    return Color.FromArgb(r, g, b);
                }
                catch { }
            }
            return fallback;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_syncTimer != null)
                {
                    _syncTimer.Stop();
                    _syncTimer.Dispose();
                    _syncTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
