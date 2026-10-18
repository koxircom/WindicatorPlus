using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindicatorPlus
{
    public class WatermarkWindow : Form
    {
        private int _currentDesktop = 1;
        private int _totalDesktops = 1;
        private string _desktopName = null;
        private int _opacityPercent = 40;
        private string _style = "Card";
        private string _position = "BottomRight";
        private int _customX = -1;
        private int _customY = -1;
        private string _modifierKey = "Control";
        private bool _isEnabled = true;

        // Custom Designer Properties
        private int _watermarkScale = 100;
        private string _titleTemplate = "{name}";
        private string _subtitleTemplate = "虚拟桌面 {n} (共 {total} 个)";
        private bool _showSubtitle = false;
        private int _titleFontSize = 14;
        private bool _showBigNumber = true;
        private bool _showBackgroundCard = true;
        private string _colorTheme = "WhiteHighlight";

        private int _windowWidth = 240;
        private int _windowHeight = 88;
        private bool _isTaskbarDocked = false; // True when positioned as an overlay over the Windows taskbar
        private bool _isTaskbarOverflowFloating = false; // True when taskbar has no horizontal space and watermark floats right above taskbar
        private Rectangle _actualScreenRect = Rectangle.Empty;

        // Dedicated Taskbar Space Provider & Background Monitor
        private ITaskbarSpaceProvider _taskbarSpaceProvider;
        private int _isSpaceUpdatePending = 0;
        private bool _lastSnapshotWasFresh = false;

        // Dedicated High-Priority Background Monitor Thread (Zero UI Pump Dependency)
        private Thread _monitorThread;
        private volatile bool _isMonitoring = false;
        private volatile bool _isInteractive = false;
        private volatile bool _isDragging = false;
        private Point _dragStartMouse;
        private Point _dragStartWin;

        // Snapping state
        private bool _isSnapped = false;
        private const int SNAP_THRESHOLD = 28;

        // Glow animation phase
        private int _animTick = 0;
        private int _topmostTick = 0;

        public event Action<int, int> PositionMoved;
        public event EventHandler RequestContextMenu;

        private long _lastMenuTriggerTick = 0;
        private volatile bool _isContextMenuOpen = false;

        public void SetContextMenuOpen(bool isOpen)
        {
            _isContextMenuOpen = isOpen;
        }

        public bool IsContextMenuOpen
        {
            get { return _isContextMenuOpen; }
        }

        internal byte LastCalculatedAlpha { get; private set; }
        internal Bitmap LastRenderedBitmapForTest { get; private set; }
        internal bool CaptureRenderedBitmapForTesting { get; set; }

        internal bool HasTaskbarIntent
        {
            get { return _isTaskbarDocked; }
            set { _isTaskbarDocked = value; }
        }

        internal void TriggerRequestContextMenu()
        {
            long now = Stopwatch.GetTimestamp();
            if (_isContextMenuOpen || (_lastMenuTriggerTick > 0 && (now - _lastMenuTriggerTick) < Stopwatch.Frequency / 2))
            {
                return;
            }
            _lastMenuTriggerTick = now;

            EventHandler handler = RequestContextMenu;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
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

        public WatermarkWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = _windowWidth;
            Height = _windowHeight;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _taskbarSpaceProvider = new TaskbarSpaceMonitor();
            _taskbarSpaceProvider.SpaceChanged += OnTaskbarSpaceChanged;
            _taskbarSpaceProvider.Start();

            _isMonitoring = true;
            _monitorThread = new Thread(MonitorThreadProc)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "WatermarkHotkeyMonitorThread"
            };
            _monitorThread.Start();
        }

        internal void SetTaskbarSpaceProvider(ITaskbarSpaceProvider provider)
        {
            if (_taskbarSpaceProvider != null)
            {
                _taskbarSpaceProvider.SpaceChanged -= OnTaskbarSpaceChanged;
                _taskbarSpaceProvider.Dispose();
            }
            _taskbarSpaceProvider = provider;
            _lastSnapshotWasFresh = false;
            if (_taskbarSpaceProvider != null)
            {
                _taskbarSpaceProvider.SpaceChanged += OnTaskbarSpaceChanged;
                _taskbarSpaceProvider.Start();
            }
            if (IsHandleCreated && !IsDisposed && _isTaskbarDocked)
            {
                UpdateWatermark();
            }
        }

        private void OnTaskbarSpaceChanged(TaskbarSpaceSnapshot snapshot)
        {
            if (!_isEnabled || !IsHandleCreated || IsDisposed) return;
            if (!_isTaskbarDocked) return;

            if (Interlocked.CompareExchange(ref _isSpaceUpdatePending, 1, 0) == 0)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        Interlocked.Exchange(ref _isSpaceUpdatePending, 0);
                        UpdateWatermark();
                    }));
                }
                catch
                {
                    Interlocked.Exchange(ref _isSpaceUpdatePending, 0);
                }
            }
        }

        internal void CheckTaskbarSpaceFreshnessGuard()
        {
            if (!_isEnabled || !IsHandleCreated || IsDisposed) return;
            if (!_isTaskbarDocked) return;

            ITaskbarSpaceProvider provider = _taskbarSpaceProvider;
            TaskbarSpaceSnapshot snapshot = provider != null ? provider.GetLatestSnapshot() : null;
            bool isFresh = snapshot != null && snapshot.IsValid && snapshot.IsFresh(2000);

            if (_lastSnapshotWasFresh && !isFresh)
            {
                // 仅当有效性从 fresh 转 stale 时合并 UI 刷新，切换为任务栏上方安全浮动模式，不每 tick 重复重绘
                _lastSnapshotWasFresh = false;
                if (Interlocked.CompareExchange(ref _isSpaceUpdatePending, 1, 0) == 0)
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            Interlocked.Exchange(ref _isSpaceUpdatePending, 0);
                            if (!_isTaskbarDocked) return;
                            UpdateWatermark();
                        }));
                    }
                    catch
                    {
                        Interlocked.Exchange(ref _isSpaceUpdatePending, 0);
                    }
                }
            }
            else if (!_lastSnapshotWasFresh && isFresh)
            {
                // 契约：fresh 恢复同几何也要触发恢复
                _lastSnapshotWasFresh = true;
                if (Interlocked.CompareExchange(ref _isSpaceUpdatePending, 1, 0) == 0)
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            Interlocked.Exchange(ref _isSpaceUpdatePending, 0);
                            if (!_isTaskbarDocked) return;
                            UpdateWatermark();
                        }));
                    }
                    catch
                    {
                        Interlocked.Exchange(ref _isSpaceUpdatePending, 0);
                    }
                }
            }
        }

        private void ApplyInteractiveState(bool interactive, bool isKeyDown)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                // A queued hover callback must not change hit testing while a menu owns input.
                if (_isContextMenuOpen) return;

                if (interactive)
                {
                    int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
                    {
                        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle & ~NativeMethods.WS_EX_TRANSPARENT);
                    }

                    Cursor targetCursor = isKeyDown ? Cursors.SizeAll : (_isTaskbarDocked ? Cursors.Hand : Cursors.Default);
                    if (this.Cursor != targetCursor)
                    {
                        this.Cursor = targetCursor;
                    }

                    if (!_isTaskbarDocked)
                    {
                        UpdateWatermark();
                    }
                }
                else
                {
                    _isSnapped = false;
                    int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) == 0)
                    {
                        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT);
                    }

                    if (this.Cursor != Cursors.Default)
                    {
                        this.Cursor = Cursors.Default;
                    }

                    if (!_isTaskbarDocked)
                    {
                        UpdateWatermark();
                    }
                }
            }
            catch { }
        }

        private void MonitorThreadProc()
        {
            bool lastReportedKeyDown = false;

            while (_isMonitoring)
            {
                if (!_isEnabled || !IsHandleCreated || IsDisposed)
                {
                    Thread.Sleep(50);
                    continue;
                }

                // 1. Taskbar Space Freshness Guard:
                // 必须在 menu skip 之前执行，防止菜单打开期间空间数据变 stale 后仍显示过期占位
                _topmostTick++;
                if (_topmostTick % 10 == 0) // every ~150ms
                {
                    CheckTaskbarSpaceFreshnessGuard();
                }

                // 菜单期间禁止轮询 hover 样式变化和 z-order 抢顶，但仍允许空间快照守护
                if (_isContextMenuOpen)
                {
                    Thread.Sleep(50);
                    continue;
                }

                int vKey = NativeMethods.VK_CONTROL;
                if (_modifierKey == "Alt") vKey = NativeMethods.VK_MENU;
                else if (_modifierKey == "Shift") vKey = NativeMethods.VK_SHIFT;

                bool isKeyDown = (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;

                NativeMethods.POINT curPt;
                NativeMethods.GetCursorPos(out curPt);

                // 背景线程不读 Bounds 等 Control 属性，使用 actual rect 或 Win32 GetWindowRect
                Rectangle targetRect = _actualScreenRect;
                if (targetRect.IsEmpty && IsHandleCreated && !IsDisposed)
                {
                    NativeMethods.RECT rc;
                    if (NativeMethods.GetWindowRect(Handle, out rc))
                    {
                        targetRect = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
                    }
                }

                bool isMouseOver = (curPt.x >= targetRect.Left && curPt.x <= targetRect.Right &&
                                    curPt.y >= targetRect.Top && curPt.y <= targetRect.Bottom);

                // 停靠在任务栏（或上方浮动）时支持鼠标划入悬停与点击交互；非任务栏模式下仅在按住修饰键时进入交互
                bool canInteract = isMouseOver && (isKeyDown || _isTaskbarDocked);

                if (canInteract)
                {
                    if (!_isInteractive)
                    {
                        _isInteractive = true;
                        lastReportedKeyDown = isKeyDown;
                        if (IsHandleCreated && !IsDisposed)
                        {
                            try
                            {
                                BeginInvoke(new Action(() => ApplyInteractiveState(true, isKeyDown)));
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // 光标修饰键切换需保留响应且避免每 15ms 入队
                        if (isKeyDown != lastReportedKeyDown)
                        {
                            lastReportedKeyDown = isKeyDown;
                            if (IsHandleCreated && !IsDisposed)
                            {
                                try
                                {
                                    BeginInvoke(new Action(() => ApplyInteractiveState(true, isKeyDown)));
                                }
                                catch { }
                            }
                        }

                        if (isKeyDown)
                        {
                            _animTick++;
                            if (_animTick % 3 == 0)
                            {
                                try
                                {
                                    if (IsHandleCreated && !IsDisposed && !_isDragging)
                                    {
                                        BeginInvoke(new Action(UpdateWatermark));
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                else
                {
                    if (_isInteractive && !_isDragging)
                    {
                        _isInteractive = false;
                        lastReportedKeyDown = false;
                        if (IsHandleCreated && !IsDisposed)
                        {
                            try
                            {
                                BeginInvoke(new Action(() => ApplyInteractiveState(false, false)));
                            }
                            catch { }
                        }
                    }
                }

                Thread.Sleep(15);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int vKey = NativeMethods.VK_CONTROL;
            if (_modifierKey == "Alt") vKey = NativeMethods.VK_MENU;
            else if (_modifierKey == "Shift") vKey = NativeMethods.VK_SHIFT;

            bool isKeyDown = (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;

            if (isKeyDown && e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStartMouse = Cursor.Position;
                _dragStartWin = new Point(Left, Top);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                Point cur = Cursor.Position;
                int rawX = _dragStartWin.X + (cur.X - _dragStartMouse.X);
                int rawY = _dragStartWin.Y + (cur.Y - _dragStartMouse.Y);

                int snappedX = rawX;
                int snappedY = rawY;
                _isSnapped = false;
                bool enterTaskbar = false;

                Screen screen = Screen.FromPoint(cur);
                Rectangle workArea = screen.WorkingArea;
                Rectangle fullBounds = screen.Bounds;

                // 1. Taskbar Overlay Snapping Detection (floating topmost overlay over Shell_TrayWnd; does not inject shell code or reserve shell space)
                IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (hTaskbar != IntPtr.Zero)
                {
                    NativeMethods.RECT rcTaskbar;
                    NativeMethods.GetWindowRect(hTaskbar, out rcTaskbar);

                    int tbHeight = rcTaskbar.Bottom - rcTaskbar.Top;
                    if (tbHeight <= 0) tbHeight = 48;

                    // When mouse cursor or window reaches taskbar threshold
                    if (cur.Y >= rcTaskbar.Top - 15 || rawY + 30 >= rcTaskbar.Top)
                    {
                        enterTaskbar = true;
                        _isSnapped = true;

                        Rectangle tbRect = Rectangle.FromLTRB(rcTaskbar.Left, rcTaskbar.Top, rcTaskbar.Right, rcTaskbar.Bottom);
                        Rectangle trayRect = Rectangle.Empty;

                        IntPtr hTray = NativeMethods.FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                        if (hTray != IntPtr.Zero)
                        {
                            NativeMethods.RECT rcTray;
                            NativeMethods.GetWindowRect(hTray, out rcTray);
                            trayRect = Rectangle.FromLTRB(rcTray.Left, rcTray.Top, rcTray.Right, rcTray.Bottom);
                        }

                        float dpiScale = Math.Max(0.5f, tbHeight / 48.0f);
                        Rectangle dockedBounds = CalculateTaskbarDockedBounds(tbRect, trayRect, _windowWidth, _windowHeight, dpiScale);
                        snappedX = dockedBounds.X;
                        snappedY = dockedBounds.Y;
                    }
                }

                _isTaskbarDocked = enterTaskbar;

                // 2. Standard Screen Boundary Snapping if not positioned as taskbar overlay (桌面拖动不被锁定)
                if (!_isTaskbarDocked)
                {
                    if (Math.Abs(rawX - workArea.Left) <= SNAP_THRESHOLD) { snappedX = workArea.Left; _isSnapped = true; }
                    else if (Math.Abs(rawX - (workArea.Left + 16)) <= SNAP_THRESHOLD) { snappedX = workArea.Left + 16; _isSnapped = true; }
                    else if (Math.Abs(rawX - (workArea.Right - _windowWidth)) <= SNAP_THRESHOLD) { snappedX = workArea.Right - _windowWidth; _isSnapped = true; }
                    else if (Math.Abs(rawX - (workArea.Right - _windowWidth - 20)) <= SNAP_THRESHOLD) { snappedX = workArea.Right - _windowWidth - 20; _isSnapped = true; }
                    else if (Math.Abs((rawX + _windowWidth / 2) - (workArea.Left + workArea.Width / 2)) <= SNAP_THRESHOLD) { snappedX = workArea.Left + (workArea.Width - _windowWidth) / 2; _isSnapped = true; }

                    if (Math.Abs(rawY - workArea.Top) <= SNAP_THRESHOLD) { snappedY = workArea.Top; _isSnapped = true; }
                    else if (Math.Abs(rawY - (workArea.Top + 16)) <= SNAP_THRESHOLD) { snappedY = workArea.Top + 16; _isSnapped = true; }
                    else if (Math.Abs(rawY - (workArea.Bottom - _windowHeight)) <= SNAP_THRESHOLD) { snappedY = workArea.Bottom - _windowHeight; _isSnapped = true; }
                    else if (Math.Abs(rawY - (workArea.Bottom - _windowHeight - 20)) <= SNAP_THRESHOLD) { snappedY = workArea.Bottom - _windowHeight - 20; _isSnapped = true; }
                }

                _customX = snappedX;
                _customY = snappedY;
                _position = "Custom";

                _actualScreenRect = new Rectangle(snappedX, snappedY, _windowWidth, _windowHeight);

                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    snappedX,
                    snappedY,
                    0,
                    0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

                UpdateWatermark();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                _isSnapped = false;
                if (PositionMoved != null)
                {
                    PositionMoved(Left, Top);
                }
                UpdateWatermark();
            }
            else if (_isTaskbarDocked)
            {
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                {
                    TriggerRequestContextMenu();
                }
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke(new Action(UpdateWatermark));
                }
                catch { }
            }
        }

        public void ApplyConfig(AppConfig config)
        {
            _isEnabled = config.WatermarkEnabled;
            _opacityPercent = config.WatermarkOpacity;
            _style = config.WatermarkStyle;
            _position = config.WatermarkPosition;
            _customX = config.CustomX;
            _customY = config.CustomY;
            _modifierKey = config.DragModifierKey;

            _watermarkScale = config.WatermarkScale;
            _titleTemplate = config.TitleTemplate;
            _subtitleTemplate = config.SubtitleTemplate;
            _showSubtitle = config.ShowSubtitle;
            _titleFontSize = config.TitleFontSize > 0 ? Math.Max(10, Math.Min(48, config.TitleFontSize)) : 14;
            _showBigNumber = config.ShowBigNumber;
            _showBackgroundCard = config.ShowBackgroundCard;
            _colorTheme = config.WatermarkColorTheme;

            _desktopName = config.GetDesktopName(_currentDesktop);

            CheckIfPositionIsTaskbarDocked();

            if (_isEnabled)
            {
                if (!Visible) Show();
                UpdateWatermark();
            }
            else
            {
                ClearLayeredWindow();
                Hide();
            }
        }

        private void CheckIfPositionIsTaskbarDocked()
        {
            bool docked = false;
            if (_position == "Custom" && _customY > 0)
            {
                IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (hTaskbar != IntPtr.Zero)
                {
                    NativeMethods.RECT rcTaskbar;
                    NativeMethods.GetWindowRect(hTaskbar, out rcTaskbar);
                    if (_customY >= rcTaskbar.Top - 10 && _customY <= rcTaskbar.Bottom)
                    {
                        docked = true;
                    }
                }
            }
            _isTaskbarDocked = docked;
        }

        public void SetDesktop(int current, int total, string customName)
        {
            _currentDesktop = current;
            _totalDesktops = Math.Max(1, total);
            _desktopName = customName;

            if (_isEnabled)
            {
                if (!Visible) Show();
                UpdateWatermark();
            }
        }

        public void SetDesktopName(string name)
        {
            _desktopName = name;
            if (_isEnabled && Visible)
            {
                UpdateWatermark();
            }
        }

        public void SetOpacity(int percent)
        {
            _opacityPercent = Math.Max(5, Math.Min(100, percent));
            if (_isEnabled && Visible)
            {
                UpdateWatermark();
            }
        }

        public void SetStyle(string style)
        {
            _style = style;
            if (_isEnabled && Visible)
            {
                UpdateWatermark();
            }
        }

        public void SetPosition(string position)
        {
            _position = position;
            if (position != "Custom")
            {
                _customX = -1;
                _customY = -1;
            }
            _isTaskbarDocked = false;
            _isTaskbarOverflowFloating = false;
            if (_isEnabled && Visible)
            {
                UpdateWatermark();
            }
        }

        public void SetModifierKey(string key)
        {
            _modifierKey = key;
        }

        public void SetDesignerProps(int scale, string titleTpl, string subTpl, bool showBigNum, bool showCard, string colorTheme)
        {
            SetDesignerProps(scale, titleTpl, subTpl, showBigNum, showCard, colorTheme, _showSubtitle, _titleFontSize);
        }

        public void SetDesignerProps(int scale, string titleTpl, string subTpl, bool showBigNum, bool showCard, string colorTheme, bool showSubtitle, int titleFontSize = 14)
        {
            _watermarkScale = Math.Max(70, Math.Min(180, scale));
            _titleTemplate = titleTpl;
            _subtitleTemplate = subTpl;
            _showBigNumber = showBigNum;
            _showBackgroundCard = showCard;
            _colorTheme = colorTheme;
            _showSubtitle = showSubtitle;
            _titleFontSize = Math.Max(10, Math.Min(48, titleFontSize));

            if (_isEnabled && Visible)
            {
                UpdateWatermark();
            }
        }

        public void SetWatermarkEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (_isEnabled)
            {
                Show();
                UpdateWatermark();
            }
            else
            {
                ClearLayeredWindow();
                Hide();
            }
        }

        private string ParseTemplate(string tpl)
        {
            if (string.IsNullOrEmpty(tpl)) return "";
            string curName = string.IsNullOrEmpty(_desktopName) ? ("桌面 " + _currentDesktop) : _desktopName;

            string res = tpl;
            res = res.Replace("{n}", _currentDesktop.ToString());
            res = res.Replace("{index}", _currentDesktop.ToString());
            res = res.Replace("{total}", _totalDesktops.ToString());
            res = res.Replace("{name}", curName);
            return res;
        }

        private void ClearLayeredWindow()
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                using (Bitmap bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                {
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
            }
        }

        public void UpdateWatermark()
        {
            if (!_isEnabled || !IsHandleCreated) return;

            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Rectangle screenBounds = Screen.PrimaryScreen.Bounds;

            string titleText = ParseTemplate(_titleTemplate);

            int w, h;
            float scale = _watermarkScale / 100.0f;
            int posX, posY;

            TaskbarAdaptiveResult adaptiveResult = default(TaskbarAdaptiveResult);

            if (_isTaskbarDocked)
            {
                TaskbarSpaceSnapshot snapshot = _taskbarSpaceProvider != null ? _taskbarSpaceProvider.GetLatestSnapshot() : null;

                // 契约：失败/过期保守隐藏而不是盖按钮；无taskbar时安全退化
                if (snapshot == null || !snapshot.IsValid || !snapshot.IsFresh(2000))
                {
                    _lastSnapshotWasFresh = false;
                    ClearLayeredWindow();
                    _actualScreenRect = Rectangle.Empty;
                    return;
                }

                _lastSnapshotWasFresh = true;

                Rectangle tbRect = snapshot.TaskbarRect;
                Rectangle trRect = snapshot.TrayRect;
                int maxAppRight = snapshot.MaxAppRight;

                int tbHeight = tbRect.Height > 0 ? tbRect.Height : 48;
                float dpiScale = Math.Max(0.5f, tbHeight / 48.0f);
                int trayGap = Math.Max(2, (int)Math.Round(4.0f * (dpiScale / 1.5f)));

                int anchorRight;
                if (!trRect.IsEmpty && trRect.Left > tbRect.Left)
                {
                    anchorRight = trRect.Left - trayGap;
                }
                else
                {
                    anchorRight = tbRect.Right - trayGap - 12;
                }

                int safetyGap = Math.Max(4, (int)Math.Round(6.0f * (dpiScale / 1.0f)));
                int availableWidth = maxAppRight > 0
                    ? (anchorRight - (maxAppRight + safetyGap))
                    : (anchorRight - (tbRect.Left + safetyGap));

                if (availableWidth > 0)
                {
                    using (Bitmap measureBmp = new Bitmap(1, 1))
                    using (Graphics gMeasure = Graphics.FromImage(measureBmp))
                    {
                        gMeasure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        adaptiveResult = ResolveAdaptiveTaskbarContent(
                            gMeasure,
                            tbHeight,
                            _watermarkScale,
                            _currentDesktop,
                            titleText,
                            _showBigNumber,
                            availableWidth);
                    }
                }
                else
                {
                    adaptiveResult = new TaskbarAdaptiveResult
                    {
                        IsVisible = false,
                        Mode = TaskbarAdaptiveMode.Hidden
                    };
                }

                bool spaceFitsInTaskbar = (adaptiveResult.IsVisible && adaptiveResult.Mode != TaskbarAdaptiveMode.Hidden);
                _isTaskbarOverflowFloating = !spaceFitsInTaskbar;

                if (!_isTaskbarOverflowFloating)
                {
                    // State A: Taskbar has enough horizontal space -> dock seamlessly inside taskbar
                    w = adaptiveResult.Width;
                    h = adaptiveResult.Height;
                    _windowWidth = w;
                    _windowHeight = h;

                    posX = anchorRight - _windowWidth;
                    posY = tbRect.Top + (tbHeight - _windowHeight) / 2;

                    // 迟滞稳定滤波 (Jitter Elimination Filter):
                    // 若新计算位置与当前屏幕位置差异在 2 像素以内（系统托盘测量或悬停微抖动），严格锁定当前位置，彻底杜绝跳动
                    if (!_actualScreenRect.IsEmpty && Math.Abs(posX - _actualScreenRect.X) <= 2 && Math.Abs(posY - _actualScreenRect.Y) <= 2)
                    {
                        posX = _actualScreenRect.X;
                        posY = _actualScreenRect.Y;
                    }

                    _customX = posX;
                    _customY = posY;
                }
                else
                {
                    // State B: Taskbar horizontal space is completely filled by app buttons.
                    // DO NOT disappear! Float right above the taskbar top edge so the user NEVER loses the watermark,
                    // and taskbar app buttons (ChatGPT, pi, etc.) remain 100% unobstructed.
                    _isTaskbarOverflowFloating = true;

                    using (Bitmap measureBmp = new Bitmap(1, 1))
                    using (Graphics gMeasure = Graphics.FromImage(measureBmp))
                    {
                        gMeasure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        TaskbarWidgetLayout fullL = CalculateTaskbarLayoutCore(
                            gMeasure,
                            tbHeight,
                            titleText,
                            _showBigNumber,
                            _showBigNumber && !string.IsNullOrEmpty(titleText),
                            _watermarkScale,
                            _currentDesktop);

                        adaptiveResult = new TaskbarAdaptiveResult
                        {
                            IsVisible = true,
                            DisplayTitle = titleText,
                            ShowBigNumber = _showBigNumber,
                            ShowSeparator = _showBigNumber && !string.IsNullOrEmpty(titleText),
                            Width = fullL.Width + 14,
                            Height = fullL.Height,
                            Layout = fullL,
                            Mode = TaskbarAdaptiveMode.Full
                        };
                    }

                    w = adaptiveResult.Width;
                    h = adaptiveResult.Height;
                    _windowWidth = w;
                    _windowHeight = h;

                    int targetRight = (!trRect.IsEmpty && trRect.Left > 0) ? trRect.Left : (workArea.Right - 16);
                    posX = targetRight - _windowWidth;
                    if (posX + _windowWidth > workArea.Right - 16) posX = workArea.Right - _windowWidth - 16;
                    if (posX < workArea.Left + 16) posX = workArea.Left + 16;

                    posY = (tbRect.Top > 0 ? tbRect.Top : workArea.Bottom) - _windowHeight - 8;
                }
            }
            else
            {
                int baseW = 240;
                int baseH = 88;
                if (_style == "Minimal") { baseW = 160; baseH = 130; }
                else if (_style == "Badge") { baseW = 190; baseH = 54; }
                else if (_style == "TextZh" || _style == "TextEn") { baseW = 220; baseH = 76; }

                string subText = ParseTemplate(_subtitleTemplate);

                if (_style == "Card")
                {
                    Size cardSz = CalculateCardWindowSize(scale, _currentDesktop, titleText, subText, _showBigNumber, _showSubtitle, _titleFontSize);
                    w = cardSz.Width;
                    h = cardSz.Height;
                }
                else if (_style == "TextZh" || _style == "TextEn")
                {
                    Size textSize = CalculateCardWindowSize(scale, _currentDesktop, titleText, subText, false, _showSubtitle, _titleFontSize);
                    w = textSize.Width;
                    h = textSize.Height;
                }
                else
                {
                    if (!string.IsNullOrEmpty(titleText) && titleText.Length > 4)
                    {
                        baseW += Math.Min(160, (titleText.Length - 4) * 16);
                    }

                    w = Math.Max(100, (int)(baseW * scale));
                    h = Math.Max(40, (int)(baseH * scale));
                }

                _windowWidth = w;
                _windowHeight = h;

                int marginX = 32;
                int marginYBottom = 72;
                int marginYTop = 36;

                if (_position == "Custom" && _customX >= 0 && _customY >= 0)
                {
                    posX = _customX;
                    posY = _customY;
                }
                else if (_position == "TopRight")
                {
                    posX = workArea.Right - _windowWidth - marginX;
                    posY = workArea.Top + marginYTop;
                }
                else if (_position == "BottomLeft")
                {
                    posX = workArea.Left + marginX;
                    posY = workArea.Bottom - _windowHeight - marginYBottom;
                }
                else if (_position == "TopCenter")
                {
                    posX = workArea.Left + (workArea.Width - _windowWidth) / 2;
                    posY = workArea.Top + marginYTop;
                }
                else // "BottomRight" (Default)
                {
                    posX = workArea.Right - _windowWidth - marginX;
                    posY = workArea.Bottom - _windowHeight - marginYBottom;
                }
            }

            // 契约：UpdateLayeredWindow 自身同时原子更新位置、大小与位图。
            // 避免 water 自身每 render 无条件 raise 破坏任务栏层级链，任务栏层级由协调器拥有；desktop watermark 正常独立 topmost 保留
            if (!_isContextMenuOpen && !_isTaskbarDocked)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }

            byte alpha = (byte)(_opacityPercent * 255 / 100);
            if (_isInteractive && !_isTaskbarDocked)
            {
                alpha = (byte)Math.Min(255, alpha + 60);
            }
            LastCalculatedAlpha = alpha;

            using (Bitmap bmp = new Bitmap(_windowWidth, _windowHeight, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = _isTaskbarDocked ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.ClearTypeGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    // 任务栏模式下整个 widget 矩形预填 Alpha=1 黑色底（肉眼不可察无可见边框），阻止 OS 将透明区域视为穿透
                    // 桌面水印模式仍使用完全 Alpha=0 透明背景，保持 100% 桌面背景穿透
                    g.Clear(_isTaskbarDocked ? Color.FromArgb(1, 0, 0, 0) : Color.Transparent);

                    if (_isTaskbarDocked)
                    {
                        if (_isTaskbarOverflowFloating)
                        {
                            Rectangle cardRect = new Rectangle(0, 0, _windowWidth - 1, _windowHeight - 1);
                            using (GraphicsPath path = CreateRoundedRectanglePath(cardRect, 10))
                            {
                                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(170, 18, 20, 28)))
                                {
                                    g.FillPath(bgBrush, path);
                                }
                                using (Pen borderPen = new Pen(Color.FromArgb(150, 139, 92, 246), 1.2f))
                                {
                                    g.DrawPath(borderPen, path);
                                }
                            }
                        }
                        RenderTaskbarDockedWidgetCore(g, _windowWidth, _windowHeight, adaptiveResult.DisplayTitle, adaptiveResult.ShowBigNumber, adaptiveResult.ShowSeparator);
                    }
                    else
                    {
                        if (_style == "Card")
                        {
                            RenderCardStyle(g, _windowWidth, _windowHeight, scale);
                        }
                        else if (_style == "Minimal")
                        {
                            RenderMinimalStyle(g, _windowWidth, _windowHeight, scale);
                        }
                        else if (_style == "Badge")
                        {
                            RenderBadgeStyle(g, _windowWidth, _windowHeight, scale);
                        }
                        else
                        {
                            RenderTextStyle(g, _windowWidth, _windowHeight, scale);
                        }
                    }

                    if (_isInteractive && !_isTaskbarDocked)
                    {
                        RenderBreathingGlow(g, _windowWidth, _windowHeight);
                    }
                }

                IntPtr screenDc = IntPtr.Zero;
                IntPtr memDc = IntPtr.Zero;
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr oldBitmap = IntPtr.Zero;
                bool ulwSuccess = false;
                try
                {
                    if (_isTaskbarDocked) ApplyTaskbarPixelOpacity(bmp, alpha);
                    screenDc = NativeMethods.GetDC(IntPtr.Zero);
                    memDc = NativeMethods.CreateCompatibleDC(screenDc);
                    hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                    oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                    NativeMethods.POINT ptDst = new NativeMethods.POINT(posX, posY);
                    NativeMethods.SIZE size = new NativeMethods.SIZE(_windowWidth, _windowHeight);
                    NativeMethods.POINT ptSrc = new NativeMethods.POINT(0, 0);

                    NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                    blend.BlendOp = NativeMethods.AC_SRC_OVER;
                    blend.BlendFlags = 0;
                    // 任务栏模式下固定 SourceConstantAlpha 为 255，保障 alpha1 底色的有效 alpha 恒保持 nonzero (1*255/255=1)，彻底杜绝 OS 命中截断穿透
                    // 桌面水印模式使用配置透明度 alpha 进行半透明合成
                    blend.SourceConstantAlpha = _isTaskbarDocked ? (byte)255 : alpha;
                    blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                    ulwSuccess = NativeMethods.UpdateLayeredWindow(
                        this.Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);

                    if (ulwSuccess)
                    {
                        _actualScreenRect = new Rectangle(posX, posY, _windowWidth, _windowHeight);
                        if (CaptureRenderedBitmapForTesting) try
                        {
                            if (LastRenderedBitmapForTest != null)
                            {
                                LastRenderedBitmapForTest.Dispose();
                            }
                            LastRenderedBitmapForTest = (Bitmap)bmp.Clone();
                        }
                        catch { }
                    }
                }
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
                }
            }
        }

        // Taskbar docked bounds calculation pure function:
        // Ensures the window's right edge strictly docks to tray.Left minus DPI gap (e.g. 4px at 144 DPI)
        // without keeping stale CustomX gaps, and automatically follows tray width changes.
        public static Rectangle CalculateTaskbarDockedBounds(Rectangle taskbarRect, Rectangle trayRect, int windowWidth, int windowHeight, float dpiScale)
        {
            if (taskbarRect.Height <= 0)
            {
                taskbarRect.Height = 48;
            }

            if (dpiScale <= 0f)
            {
                dpiScale = Math.Max(0.5f, taskbarRect.Height / 48.0f);
            }

            // 停靠时 right 恒等 tray.Left 减小 DPI 间隔 (如 4px at 144DPI)
            // 144 DPI 是 1.5 倍缩放 (dpiScale = 1.5f)。基准公式：Math.Round(4.0f * (dpiScale / 1.5f))
            int trayGap = Math.Max(2, (int)Math.Round(4.0f * (dpiScale / 1.5f)));

            int targetRight;
            if (!trayRect.IsEmpty && trayRect.Left > taskbarRect.Left)
            {
                targetRight = trayRect.Left - trayGap;
            }
            else
            {
                targetRight = taskbarRect.Right - trayGap - 12;
            }

            int posX = targetRight - windowWidth;
            int posY = taskbarRect.Top + (taskbarRect.Height - windowHeight) / 2;

            return new Rectangle(posX, posY, windowWidth, windowHeight);
        }

        public static Rectangle CalculateTaskbarDockedBounds(Rectangle taskbarRect, Rectangle trayRect, int windowWidth, int windowHeight)
        {
            float dpiScale = Math.Max(0.5f, taskbarRect.Height / 48.0f);
            return CalculateTaskbarDockedBounds(taskbarRect, trayRect, windowWidth, windowHeight, dpiScale);
        }

        // Taskbar layout measurement helper struct
        public struct TaskbarWidgetLayout
        {
            public int Width;
            public int Height;
            public float DpiScale;
            public float NumFontSize;
            public float TitleFontSize;
            public float NumX;
            public float NumY;
            public float NumW;
            public float NumH;
            public float SepX;
            public float SepY1;
            public float SepY2;
            public float TitleX;
            public float TitleY;
            public float TitleW;
            public float TitleH;
        }

        public enum TaskbarAdaptiveMode
        {
            Full,
            Ellipsis,
            NumberOnly,
            Hidden
        }

        public struct TaskbarAdaptiveResult
        {
            public bool IsVisible;
            public string DisplayTitle;
            public bool ShowBigNumber;
            public bool ShowSeparator;
            public int Width;
            public int Height;
            public TaskbarWidgetLayout Layout;
            public TaskbarAdaptiveMode Mode;
        }

        public static TaskbarAdaptiveResult ResolveAdaptiveTaskbarContent(
            Graphics g,
            int taskbarHeight,
            int watermarkScale,
            int currentDesktop,
            string fullTitleText,
            bool showBigNumber,
            int availableWidth)
        {
            if (availableWidth <= 0)
            {
                return new TaskbarAdaptiveResult
                {
                    IsVisible = false,
                    Mode = TaskbarAdaptiveMode.Hidden
                };
            }

            // 1. Full Title
            if (!string.IsNullOrEmpty(fullTitleText))
            {
                TaskbarWidgetLayout layoutFull = CalculateTaskbarLayoutCore(
                    g, taskbarHeight, fullTitleText, showBigNumber, showBigNumber, watermarkScale, currentDesktop);

                if (layoutFull.Width <= availableWidth)
                {
                    return new TaskbarAdaptiveResult
                    {
                        IsVisible = true,
                        DisplayTitle = fullTitleText,
                        ShowBigNumber = showBigNumber,
                        ShowSeparator = showBigNumber,
                        Width = layoutFull.Width,
                        Height = layoutFull.Height,
                        Layout = layoutFull,
                        Mode = TaskbarAdaptiveMode.Full
                    };
                }
            }

            // 2. Ellipsis (Truncate via Unicode text elements, preserving surrogate pairs)
            if (!string.IsNullOrEmpty(fullTitleText))
            {
                List<string> elements = new List<string>();
                TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(fullTitleText);
                while (enumerator.MoveNext())
                {
                    elements.Add(enumerator.GetTextElement());
                }

                for (int i = elements.Count - 1; i >= 1; i--)
                {
                    string candidate = string.Concat(elements.Take(i).ToArray()) + "…";
                    TaskbarWidgetLayout candLayout = CalculateTaskbarLayoutCore(
                        g, taskbarHeight, candidate, showBigNumber, showBigNumber, watermarkScale, currentDesktop);

                    if (candLayout.Width <= availableWidth)
                    {
                        return new TaskbarAdaptiveResult
                        {
                            IsVisible = true,
                            DisplayTitle = candidate,
                            ShowBigNumber = showBigNumber,
                            ShowSeparator = showBigNumber,
                            Width = candLayout.Width,
                            Height = candLayout.Height,
                            Layout = candLayout,
                            Mode = TaskbarAdaptiveMode.Ellipsis
                        };
                    }
                }
            }

            // 3. Number-only (No isolated separator)
            if (showBigNumber)
            {
                TaskbarWidgetLayout numLayout = CalculateTaskbarLayoutCore(
                    g, taskbarHeight, "", true, false, watermarkScale, currentDesktop);

                if (numLayout.Width <= availableWidth)
                {
                    return new TaskbarAdaptiveResult
                    {
                        IsVisible = true,
                        DisplayTitle = "",
                        ShowBigNumber = true,
                        ShowSeparator = false, // 严格不显示孤立分隔符
                        Width = numLayout.Width,
                        Height = numLayout.Height,
                        Layout = numLayout,
                        Mode = TaskbarAdaptiveMode.NumberOnly
                    };
                }
            }

            // 4. No space left even for number -> Hidden
            return new TaskbarAdaptiveResult
            {
                IsVisible = false,
                Mode = TaskbarAdaptiveMode.Hidden
            };
        }

        private TaskbarWidgetLayout CalculateTaskbarLayout(Graphics g, int taskbarHeight, string titleText)
        {
            bool showSep = _showBigNumber && !string.IsNullOrEmpty(titleText);
            return CalculateTaskbarLayoutCore(g, taskbarHeight, titleText, _showBigNumber, showSep, _watermarkScale, _currentDesktop);
        }

        internal static TaskbarWidgetLayout CalculateTaskbarLayoutCore(
            Graphics g,
            int taskbarHeight,
            string titleText,
            bool showBigNumber,
            bool showSeparator,
            int watermarkScale,
            int currentDesktop)
        {
            TaskbarWidgetLayout layout = new TaskbarWidgetLayout();
            if (taskbarHeight <= 0) taskbarHeight = 48;
            layout.Height = taskbarHeight;

            // 基准 48px 对应 96 DPI (100% 缩放)
            float dpiScale = Math.Max(0.5f, taskbarHeight / 48.0f);
            layout.DpiScale = dpiScale;
            float userScale = watermarkScale / 100.0f;

            // 契约：默认名称 18 逻辑 px、数字 22 逻辑 px，乘 _watermarkScale / 100
            float baseTitleSize = 18f;
            float baseNumSize = 22f;

            float numSize = baseNumSize * dpiScale * userScale;
            float titleSize = baseTitleSize * dpiScale * userScale;

            // 实际字体高度必须 <= 任务栏高 - 12px 并居中 (48/72/96px 等)
            float maxFontHeight = Math.Max(8f, taskbarHeight - 12f);
            if (numSize > maxFontHeight)
            {
                float ratio = maxFontHeight / numSize;
                numSize = maxFontHeight;
                titleSize = Math.Max(8f, titleSize * ratio);
            }
            if (titleSize > maxFontHeight)
            {
                titleSize = maxFontHeight;
            }

            layout.NumFontSize = numSize;
            layout.TitleFontSize = titleSize;

            string numText = currentDesktop.ToString();

            float padLeft = Math.Max(3f, (float)Math.Round(4f * dpiScale));
            float padRight = Math.Max(3f, (float)Math.Round(3f * dpiScale));
            float sepGap = Math.Max(4f, 6f * dpiScale);

            float currentX = padLeft;

            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                if (showBigNumber)
                {
                    using (Font numFont = new Font("Segoe UI Variable Display", numSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        SizeF numSz = g.MeasureString(numText, numFont, PointF.Empty, sf);
                        layout.NumW = numSz.Width;
                        layout.NumH = numSz.Height;
                        layout.NumX = currentX;
                        layout.NumY = (taskbarHeight - numSz.Height) / 2.0f;

                        currentX += numSz.Width;

                        if (showSeparator)
                        {
                            currentX += sepGap;
                            layout.SepX = currentX;
                            float sepH = Math.Min(maxFontHeight, Math.Max(numSz.Height, titleSize) * 0.8f);
                            layout.SepY1 = (taskbarHeight - sepH) / 2.0f;
                            layout.SepY2 = layout.SepY1 + sepH;

                            currentX += sepGap;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(titleText))
                {
                    using (Font titleFont = new Font("Microsoft YaHei UI", titleSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        SizeF titleSz = g.MeasureString(titleText, titleFont, PointF.Empty, sf);
                        layout.TitleW = titleSz.Width;
                        layout.TitleH = titleSz.Height;
                        layout.TitleX = currentX;
                        layout.TitleY = (taskbarHeight - titleSz.Height) / 2.0f;

                        currentX += titleSz.Width;
                    }
                }
            }

            layout.Width = (int)Math.Ceiling(currentX + padRight);
            return layout;
        }

        // Transparent overlay single-line widget: "数字 | 开发调试" (no background, no border, no subtitle/counter/drag-hint)
        internal static void ApplyTaskbarPixelOpacity(Bitmap bitmap, byte opacity)
        {
            // Bake user opacity into the pixels, retaining alpha=1 in blank hit-area pixels.
            // SourceConstantAlpha must stay 255 so Windows does not round the hit surface to zero.
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[bitmap.Width * 4];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr address = IntPtr.Add(data.Scan0, y * data.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(address, row, 0, row.Length);
                    for (int x = 3; x < row.Length; x += 4)
                        row[x] = (byte)Math.Max(1, (row[x] * opacity + 127) / 255);
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, address, row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
        }

        private void RenderTaskbarDockedWidget(Graphics g, int w, int h, string titleText)
        {
            bool showSep = _showBigNumber && !string.IsNullOrEmpty(titleText);
            RenderTaskbarDockedWidgetCore(g, w, h, titleText, _showBigNumber, showSep);
        }

        internal void RenderTaskbarDockedWidgetCore(Graphics g, int w, int h, string titleText, bool showBigNumber, bool showSeparator)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            TaskbarWidgetLayout layout = CalculateTaskbarLayoutCore(g, h, titleText, showBigNumber, showSeparator, _watermarkScale, _currentDesktop);

            string numText = _currentDesktop.ToString();

            using (Font numFont = new Font("Segoe UI Variable Display", layout.NumFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font titleFont = new Font("Microsoft YaHei UI", layout.TitleFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                if (showBigNumber)
                {
                    g.DrawString(numText, numFont, shadowBrush, new PointF(layout.NumX + 1f, layout.NumY + 1f), sf);
                    g.DrawString(numText, numFont, whiteBrush, new PointF(layout.NumX, layout.NumY), sf);

                    if (showSeparator)
                    {
                        float penWidth = Math.Max(1.0f, layout.DpiScale >= 1.5f ? 1.5f : 1.0f);
                        using (Pen sepPen = new Pen(Color.FromArgb(120, 255, 255, 255), penWidth))
                        {
                            g.DrawLine(sepPen, layout.SepX, layout.SepY1, layout.SepX, layout.SepY2);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(titleText))
                {
                    g.DrawString(titleText, titleFont, shadowBrush, new PointF(layout.TitleX + 1f, layout.TitleY + 1f), sf);
                    g.DrawString(titleText, titleFont, whiteBrush, new PointF(layout.TitleX, layout.TitleY), sf);
                }
            }
        }

        private void RenderBreathingGlow(Graphics g, int w, int h)
        {
            if (_isTaskbarDocked) return;

            double pulse = 0.70 + 0.30 * Math.Sin((_animTick * 6) * Math.PI / 180.0);

            Color outerGlowColor = _isSnapped
                ? Color.FromArgb((int)(150 * pulse), 52, 211, 153)
                : Color.FromArgb((int)(110 * pulse), 167, 139, 250);

            Color innerBorderColor = _isSnapped
                ? Color.FromArgb((int)(245 * pulse), 16, 185, 129)
                : Color.FromArgb((int)(240 * pulse), 192, 132, 252);

            Rectangle outerRect = new Rectangle(1, 1, w - 3, h - 3);
            int radius = _isTaskbarDocked ? 6 : 18;

            using (GraphicsPath outerPath = CreateRoundedRectanglePath(outerRect, radius))
            using (Pen glowPen = new Pen(outerGlowColor, 3.5f))
            {
                g.DrawPath(glowPen, outerPath);
            }

            using (GraphicsPath innerPath = CreateRoundedRectanglePath(new Rectangle(2, 2, w - 5, h - 5), radius))
            using (Pen strokePen = new Pen(innerBorderColor, 1.8f))
            {
                strokePen.DashStyle = DashStyle.Dash;
                strokePen.DashOffset = (_animTick % 20);
                g.DrawPath(strokePen, innerPath);
            }
        }

        private void GetThemeColors(out Color titleColor, out Color subColor, out Color numColor, out Color cardBgColor, out Color borderPenColor)
        {
            if (_colorTheme == "NeonPurple")
            {
                titleColor = Color.FromArgb(255, 255, 255);
                subColor = Color.FromArgb(216, 180, 254);
                numColor = Color.FromArgb(192, 132, 252);
                cardBgColor = Color.FromArgb(160, 26, 20, 38);
                borderPenColor = Color.FromArgb(150, 168, 85, 247);
            }
            else if (_colorTheme == "CyanGlow")
            {
                titleColor = Color.FromArgb(255, 255, 255);
                subColor = Color.FromArgb(186, 230, 253);
                numColor = Color.FromArgb(56, 189, 248);
                cardBgColor = Color.FromArgb(160, 16, 28, 38);
                borderPenColor = Color.FromArgb(150, 14, 165, 233);
            }
            else if (_colorTheme == "DarkSubtle")
            {
                titleColor = Color.FromArgb(235, 240, 248);
                subColor = Color.FromArgb(160, 170, 190);
                numColor = Color.FromArgb(220, 225, 235);
                cardBgColor = Color.FromArgb(130, 16, 20, 26);
                borderPenColor = Color.FromArgb(110, 255, 255, 255);
            }
            else // "WhiteHighlight"
            {
                titleColor = Color.FromArgb(255, 255, 255);
                subColor = Color.FromArgb(245, 248, 255);
                numColor = Color.FromArgb(255, 255, 255);
                cardBgColor = Color.FromArgb(180, 14, 18, 26);
                borderPenColor = Color.FromArgb(220, 255, 255, 255);
            }
        }

        internal struct CardLayoutResult
        {
            public Rectangle CardRect;
            public bool ShowBigNumber;
            public RectangleF NumAreaRect;
            public PointF NumDrawPoint;
            public RectangleF NumGlyphBounds;
            public bool ShowSeparator;
            public float SepX;
            public float SepY1;
            public float SepY2;
            public RectangleF ContentAreaRect;
            public bool HasSubtitle;
            public PointF TitleDrawPoint;
            public RectangleF TitleGlyphBounds;
            public PointF SubDrawPoint;
            public RectangleF SubGlyphBounds;
        }

        public static RectangleF MeasureGlyphBounds(string text, Font font, StringFormat sf = null)
        {
            if (string.IsNullOrEmpty(text)) return RectangleF.Empty;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, Point.Empty, sf ?? StringFormat.GenericTypographic);
                return path.GetBounds();
            }
        }

        internal static CardLayoutResult CalculateCardLayoutCore(
            int w,
            int h,
            float scale,
            int currentDesktop,
            string titleText,
            string subText,
            bool showBigNumber,
            bool showSubtitle,
            int titleFontSizeLogic)
        {
            CardLayoutResult res = new CardLayoutResult();
            res.CardRect = new Rectangle(4, 4, w - 8, h - 8);
            res.ShowBigNumber = showBigNumber;

            string numText = currentDesktop.ToString();
            int numFontSize = Math.Max(12, (int)(48 * scale));
            int actualTitleFontSize = Math.Max(10, Math.Min(48, titleFontSizeLogic > 0 ? titleFontSizeLogic : 14));
            int titleFontSize = Math.Max(8, (int)(actualTitleFontSize * scale));
            int subFontSize = Math.Max(8, (int)(11 * scale));

            using (StringFormat sf = StringFormat.GenericTypographic)
            using (Font numFont = new Font("Segoe UI Variable Display", numFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font titleFont = new Font("Microsoft YaHei UI", titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font subFont = new Font("Microsoft YaHei UI", subFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float sepX = 0;
                if (showBigNumber)
                {
                    RectangleF rawNumBounds = MeasureGlyphBounds(numText, numFont, sf);
                    sepX = (int)((numText.Length > 1 ? 98 : 78) * scale);
                    if (sepX < res.CardRect.Left + rawNumBounds.Width + 20 * scale)
                    {
                        sepX = (float)Math.Ceiling(res.CardRect.Left + rawNumBounds.Width + 20 * scale);
                    }

                    res.SepX = sepX;
                    res.SepY1 = (int)(16 * scale);
                    res.SepY2 = h - (int)(16 * scale);
                    res.ShowSeparator = true;

                    res.NumAreaRect = new RectangleF(res.CardRect.Left, res.CardRect.Top, sepX - res.CardRect.Left, res.CardRect.Height);
                    float numCenterX = res.NumAreaRect.X + res.NumAreaRect.Width / 2.0f;
                    float numCenterY = res.NumAreaRect.Y + res.NumAreaRect.Height / 2.0f;

                    res.NumDrawPoint = new PointF(
                        numCenterX - (rawNumBounds.X + rawNumBounds.Width / 2.0f),
                        numCenterY - (rawNumBounds.Y + rawNumBounds.Height / 2.0f));

                    res.NumGlyphBounds = new RectangleF(
                        res.NumDrawPoint.X + rawNumBounds.X,
                        res.NumDrawPoint.Y + rawNumBounds.Y,
                        rawNumBounds.Width,
                        rawNumBounds.Height);
                }
                else
                {
                    res.ShowSeparator = false;
                    res.NumAreaRect = RectangleF.Empty;
                    res.NumDrawPoint = PointF.Empty;
                    res.NumGlyphBounds = RectangleF.Empty;
                }

                // Content Area
                if (showBigNumber)
                {
                    float contentLeft = sepX + 1;
                    res.ContentAreaRect = new RectangleF(
                        contentLeft,
                        res.CardRect.Top,
                        Math.Max(10f, res.CardRect.Right - contentLeft),
                        res.CardRect.Height);
                }
                else
                {
                    res.ContentAreaRect = new RectangleF(
                        res.CardRect.Left,
                        res.CardRect.Top,
                        res.CardRect.Width,
                        res.CardRect.Height);
                }

                float contentCenterX = res.ContentAreaRect.X + res.ContentAreaRect.Width / 2.0f;
                float contentCenterY = res.ContentAreaRect.Y + res.ContentAreaRect.Height / 2.0f;

                bool hasSub = showSubtitle && !string.IsNullOrWhiteSpace(subText);
                res.HasSubtitle = hasSub;

                RectangleF rawTitleBounds = MeasureGlyphBounds(titleText, titleFont, sf);
                RectangleF rawSubBounds = hasSub ? MeasureGlyphBounds(subText, subFont, sf) : RectangleF.Empty;

                if (!hasSub)
                {
                    if (!string.IsNullOrEmpty(titleText))
                    {
                        res.TitleDrawPoint = new PointF(
                            contentCenterX - (rawTitleBounds.X + rawTitleBounds.Width / 2.0f),
                            contentCenterY - (rawTitleBounds.Y + rawTitleBounds.Height / 2.0f));

                        res.TitleGlyphBounds = new RectangleF(
                            res.TitleDrawPoint.X + rawTitleBounds.X,
                            res.TitleDrawPoint.Y + rawTitleBounds.Y,
                            rawTitleBounds.Width,
                            rawTitleBounds.Height);
                    }
                    else
                    {
                        res.TitleDrawPoint = new PointF(contentCenterX, contentCenterY);
                        res.TitleGlyphBounds = RectangleF.Empty;
                    }

                    res.SubDrawPoint = PointF.Empty;
                    res.SubGlyphBounds = RectangleF.Empty;
                }
                else
                {
                    float lineGap = Math.Max(3f, (float)Math.Round(5f * scale));
                    float totalBlockH = rawTitleBounds.Height + lineGap + rawSubBounds.Height;
                    float blockTop = contentCenterY - totalBlockH / 2.0f;

                    float titleTargetCenterY = blockTop + rawTitleBounds.Height / 2.0f;
                    float subTargetCenterY = blockTop + rawTitleBounds.Height + lineGap + rawSubBounds.Height / 2.0f;

                    res.TitleDrawPoint = new PointF(
                        contentCenterX - (rawTitleBounds.X + rawTitleBounds.Width / 2.0f),
                        titleTargetCenterY - (rawTitleBounds.Y + rawTitleBounds.Height / 2.0f));

                    res.TitleGlyphBounds = new RectangleF(
                        res.TitleDrawPoint.X + rawTitleBounds.X,
                        res.TitleDrawPoint.Y + rawTitleBounds.Y,
                        rawTitleBounds.Width,
                        rawTitleBounds.Height);

                    res.SubDrawPoint = new PointF(
                        contentCenterX - (rawSubBounds.X + rawSubBounds.Width / 2.0f),
                        subTargetCenterY - (rawSubBounds.Y + rawSubBounds.Height / 2.0f));

                    res.SubGlyphBounds = new RectangleF(
                        res.SubDrawPoint.X + rawSubBounds.X,
                        res.SubDrawPoint.Y + rawSubBounds.Y,
                        rawSubBounds.Width,
                        rawSubBounds.Height);
                }
            }

            return res;
        }

        internal static Size CalculateCardWindowSize(
            float scale,
            int currentDesktop,
            string titleText,
            string subText,
            bool showBigNumber,
            bool showSubtitle,
            int titleFontSizeLogic)
        {
            int baseW = 240;
            int baseH = 88;

            string numText = currentDesktop.ToString();
            int numFontSize = Math.Max(12, (int)(48 * scale));
            int actualTitleFontSize = Math.Max(10, Math.Min(48, titleFontSizeLogic > 0 ? titleFontSizeLogic : 14));
            int titleFontSize = Math.Max(8, (int)(actualTitleFontSize * scale));
            int subFontSize = Math.Max(8, (int)(11 * scale));

            using (StringFormat sf = StringFormat.GenericTypographic)
            using (Font numFont = new Font("Segoe UI Variable Display", numFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font titleFont = new Font("Microsoft YaHei UI", titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font subFont = new Font("Microsoft YaHei UI", subFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                RectangleF rawTitleBounds = MeasureGlyphBounds(titleText, titleFont, sf);
                bool hasSub = showSubtitle && !string.IsNullOrWhiteSpace(subText);
                RectangleF rawSubBounds = hasSub ? MeasureGlyphBounds(subText, subFont, sf) : RectangleF.Empty;

                float maxTextW = Math.Max(rawTitleBounds.Width, hasSub ? rawSubBounds.Width : 0);
                float minW, minH;

                if (showBigNumber)
                {
                    RectangleF rawNumBounds = MeasureGlyphBounds(numText, numFont, sf);
                    float sepX = (int)((numText.Length > 1 ? 98 : 78) * scale);
                    if (sepX < 4 + rawNumBounds.Width + 20 * scale)
                    {
                        sepX = (float)Math.Ceiling(4 + rawNumBounds.Width + 20 * scale);
                    }
                    minW = sepX + maxTextW + 36 * scale;
                    float textH = hasSub ? (rawTitleBounds.Height + Math.Max(4f, 6f * scale) + rawSubBounds.Height) : rawTitleBounds.Height;
                    minH = Math.Max(rawNumBounds.Height + 28 * scale, textH + 28 * scale);
                }
                else
                {
                    minW = maxTextW + 48 * scale;
                    float textH = hasSub ? (rawTitleBounds.Height + Math.Max(4f, 6f * scale) + rawSubBounds.Height) : rawTitleBounds.Height;
                    minH = textH + 28 * scale;
                }

                int w = (int)Math.Max(baseW * scale, Math.Ceiling(minW));
                int h = (int)Math.Max(baseH * scale, Math.Ceiling(minH));
                return new Size(w, h);
            }
        }

        private void RenderCardStyle(Graphics g, int w, int h, float scale)
        {
            Color titleColor, subColor, numColor, cardBgColor, borderPenColor;
            GetThemeColors(out titleColor, out subColor, out numColor, out cardBgColor, out borderPenColor);

            if (_showBackgroundCard)
            {
                CardBorderGeometry borderGeo = CalculateCardBorderGeometry(w, h, scale);
                PixelOffsetMode oldPom = g.PixelOffsetMode;
                try
                {
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (GraphicsPath outerPath = CreateRoundedRectanglePath(borderGeo.OuterRect, borderGeo.OuterRadius))
                    using (SolidBrush bgBrush = new SolidBrush(cardBgColor))
                    {
                        g.FillPath(bgBrush, outerPath);
                    }
                }
                finally
                {
                    g.PixelOffsetMode = oldPom;
                }

                RenderCardBorderRing(g, w, h, borderGeo, borderPenColor);
            }

            string numText = _currentDesktop.ToString();
            string titleText = ParseTemplate(_titleTemplate);
            string subText = ParseTemplate(_subtitleTemplate);

            if (_isInteractive)
            {
                subText = _isSnapped ? "🧲 已吸附" : "✋ 按住左键自由拖动";
            }

            CardLayoutResult layout = CalculateCardLayoutCore(
                w,
                h,
                scale,
                _currentDesktop,
                titleText,
                subText,
                _showBigNumber,
                _showSubtitle,
                _titleFontSize);

            int numFontSize = Math.Max(12, (int)(48 * scale));
            int actualTitleFontSize = Math.Max(10, Math.Min(48, _titleFontSize > 0 ? _titleFontSize : 14));
            int titleFontSize = Math.Max(8, (int)(actualTitleFontSize * scale));
            int subFontSize = Math.Max(8, (int)(11 * scale));

            using (StringFormat sf = StringFormat.GenericTypographic)
            using (Font numFont = new Font("Segoe UI Variable Display", numFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font titleFont = new Font("Microsoft YaHei UI", titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font subFont = new Font("Microsoft YaHei UI", subFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                if (layout.ShowBigNumber)
                {
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                    {
                        g.DrawString(numText, numFont, shadow, new PointF(layout.NumDrawPoint.X + 2, layout.NumDrawPoint.Y + 1), sf);
                    }
                    using (SolidBrush white = new SolidBrush(numColor))
                    {
                        g.DrawString(numText, numFont, white, layout.NumDrawPoint, sf);
                    }

                    if (layout.ShowSeparator)
                    {
                        using (Pen sepPen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.2f))
                        {
                            g.DrawLine(sepPen, layout.SepX, layout.SepY1, layout.SepX, layout.SepY2);
                        }
                    }
                }

                // High Contrast Title
                if (!string.IsNullOrEmpty(titleText))
                {
                    using (SolidBrush titleShadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                    using (SolidBrush titleBrush = new SolidBrush(titleColor))
                    {
                        g.DrawString(titleText, titleFont, titleShadow, new PointF(layout.TitleDrawPoint.X + 1, layout.TitleDrawPoint.Y + 1), sf);
                        g.DrawString(titleText, titleFont, titleBrush, layout.TitleDrawPoint, sf);
                    }
                }

                // High Contrast Subtitle
                if (layout.HasSubtitle && !string.IsNullOrEmpty(subText))
                {
                    Color displaySubColor = _isInteractive
                        ? (_isSnapped ? Color.FromArgb(52, 211, 153) : Color.FromArgb(192, 132, 252))
                        : subColor;

                    using (SolidBrush subShadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    using (SolidBrush subBrush = new SolidBrush(displaySubColor))
                    {
                        g.DrawString(subText, subFont, subShadow, new PointF(layout.SubDrawPoint.X + 1, layout.SubDrawPoint.Y + 1), sf);
                        g.DrawString(subText, subFont, subBrush, layout.SubDrawPoint, sf);
                    }
                }
            }
        }

        private void RenderMinimalStyle(Graphics g, int w, int h, float scale)
        {
            string numText = _currentDesktop.ToString();
            string titleText = ParseTemplate(_titleTemplate);

            Color titleColor, subColor, numColor, cardBgColor, borderPenColor;
            GetThemeColors(out titleColor, out subColor, out numColor, out cardBgColor, out borderPenColor);

            using (Font numFont = new Font("Segoe UI Variable Display", (int)(74 * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font nameFont = new Font("Microsoft YaHei UI", (int)(12 * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                {
                    g.DrawString(numText, numFont, shadow, new PointF((int)(26 * scale), (int)(8 * scale)));
                    g.DrawString(numText, numFont, shadow, new PointF((int)(22 * scale), (int)(12 * scale)));
                }
                using (SolidBrush textBrush = new SolidBrush(numColor))
                {
                    g.DrawString(numText, numFont, textBrush, new PointF((int)(24 * scale), (int)(10 * scale)));
                }

                if (!string.IsNullOrEmpty(titleText))
                {
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                    using (SolidBrush white = new SolidBrush(titleColor))
                    {
                        g.DrawString(titleText, nameFont, shadow, new PointF((int)(25 * scale), (int)(94 * scale)));
                        g.DrawString(titleText, nameFont, white, new PointF((int)(24 * scale), (int)(93 * scale)));
                    }
                }
            }
        }

        private void RenderBadgeStyle(Graphics g, int w, int h, float scale)
        {
            Rectangle pillRect = new Rectangle(4, 4, w - 8, h - 8);
            int radius = (h - 8) / 2;

            Color titleColor, subColor, numColor, cardBgColor, borderPenColor;
            GetThemeColors(out titleColor, out subColor, out numColor, out cardBgColor, out borderPenColor);

            if (_showBackgroundCard)
            {
                using (GraphicsPath path = CreateRoundedRectanglePath(pillRect, radius))
                {
                    using (SolidBrush bgBrush = new SolidBrush(cardBgColor))
                    {
                        g.FillPath(bgBrush, path);
                    }
                    using (Pen borderPen = new Pen(borderPenColor, 1.2f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }
            }

            string text = ParseTemplate(_titleTemplate);
            if (string.IsNullOrEmpty(text)) text = string.Format("🖥️ 桌面 {0} / {1}", _currentDesktop, _totalDesktops);

            if (_isInteractive) text = _isSnapped ? "🧲 已吸附" : "✋ 按住左键自由拖动";

            using (Font font = new Font("Microsoft YaHei UI", (int)(12.5F * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF sz = g.MeasureString(text, font);
                float x = (w - sz.Width) / 2.0f;
                float y = (h - sz.Height) / 2.0f;

                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                {
                    g.DrawString(text, font, shadow, new PointF(x + 1, y + 1));
                }
                using (SolidBrush white = new SolidBrush(titleColor))
                {
                    g.DrawString(text, font, white, new PointF(x, y));
                }
            }
        }

        private void RenderTextStyle(Graphics g, int w, int h, float scale)
        {
            Rectangle cardRect = new Rectangle(4, 4, w - 8, h - 8);
            int radius = (int)(14 * scale);

            Color titleColor, subColor, numColor, cardBgColor, borderPenColor;
            GetThemeColors(out titleColor, out subColor, out numColor, out cardBgColor, out borderPenColor);

            if (_showBackgroundCard)
            {
                using (GraphicsPath path = CreateRoundedRectanglePath(cardRect, radius))
                {
                    using (SolidBrush bgBrush = new SolidBrush(cardBgColor))
                    {
                        g.FillPath(bgBrush, path);
                    }
                    using (Pen borderPen = new Pen(borderPenColor, 1.0f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }
            }

            string title = ParseTemplate(_titleTemplate);
            string subtitle = ParseTemplate(_subtitleTemplate);

            if (_isInteractive)
            {
                subtitle = _isSnapped ? "🧲 已吸附" : "✋ 按住左键自由拖动";
            }

            int actualTitleFontSize = Math.Max(10, Math.Min(48, _titleFontSize > 0 ? _titleFontSize : 14));
            int titleFontSize = Math.Max(8, (int)(actualTitleFontSize * scale));
            int subFontSize = Math.Max(8, (int)(11 * scale));
            bool hasSub = _showSubtitle && !string.IsNullOrWhiteSpace(subtitle);

            using (StringFormat sf = StringFormat.GenericTypographic)
            using (Font titleFont = new Font("Microsoft YaHei UI", titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font subFont = new Font("Microsoft YaHei UI", subFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float contentCenterX = cardRect.X + cardRect.Width / 2.0f;
                float contentCenterY = cardRect.Y + cardRect.Height / 2.0f;

                RectangleF titleBounds = MeasureGlyphBounds(title, titleFont, sf);
                RectangleF subBounds = hasSub ? MeasureGlyphBounds(subtitle, subFont, sf) : RectangleF.Empty;

                PointF titleDrawPoint;
                PointF subDrawPoint = PointF.Empty;

                if (!hasSub)
                {
                    titleDrawPoint = new PointF(
                        contentCenterX - (titleBounds.X + titleBounds.Width / 2.0f),
                        contentCenterY - (titleBounds.Y + titleBounds.Height / 2.0f));
                }
                else
                {
                    float gap = Math.Max(3f, (float)Math.Round(5f * scale));
                    float totalH = titleBounds.Height + gap + subBounds.Height;
                    float blockTop = contentCenterY - totalH / 2.0f;

                    float titleTargetY = blockTop + titleBounds.Height / 2.0f;
                    float subTargetY = blockTop + titleBounds.Height + gap + subBounds.Height / 2.0f;

                    titleDrawPoint = new PointF(
                        contentCenterX - (titleBounds.X + titleBounds.Width / 2.0f),
                        titleTargetY - (titleBounds.Y + titleBounds.Height / 2.0f));

                    subDrawPoint = new PointF(
                        contentCenterX - (subBounds.X + subBounds.Width / 2.0f),
                        subTargetY - (subBounds.Y + subBounds.Height / 2.0f));
                }

                if (!string.IsNullOrEmpty(title))
                {
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                    using (SolidBrush textBrush = new SolidBrush(titleColor))
                    {
                        g.DrawString(title, titleFont, shadow, new PointF(titleDrawPoint.X + 1, titleDrawPoint.Y + 1), sf);
                        g.DrawString(title, titleFont, textBrush, titleDrawPoint, sf);
                    }
                }

                if (hasSub && !string.IsNullOrEmpty(subtitle))
                {
                    Color displaySubColor = _isInteractive
                        ? (_isSnapped ? Color.FromArgb(52, 211, 153) : Color.FromArgb(192, 132, 252))
                        : subColor;

                    using (SolidBrush subBrush = new SolidBrush(displaySubColor))
                    {
                        g.DrawString(subtitle, subFont, subBrush, subDrawPoint, sf);
                    }
                }
            }
        }

        internal struct CardBorderGeometry
        {
            public RectangleF OuterRect;
            public RectangleF InnerRect;
            public float OuterRadius;
            public float InnerRadius;
            public float Thickness;
            public PointF TopLeftOuterCenter;
            public PointF TopLeftInnerCenter;
            public PointF TopRightOuterCenter;
            public PointF TopRightInnerCenter;
            public PointF BottomRightOuterCenter;
            public PointF BottomRightInnerCenter;
            public PointF BottomLeftOuterCenter;
            public PointF BottomLeftInnerCenter;
        }

        internal static CardBorderGeometry CalculateCardBorderGeometry(int w, int h, float scale)
        {
            float thickness = 2.4f * scale;
            RectangleF outerRect = new RectangleF(4f, 4f, (float)(w - 8), (float)(h - 8));
            float outerRadius = 18f * scale;

            float innerX = outerRect.X + thickness;
            float innerY = outerRect.Y + thickness;
            float innerW = Math.Max(0f, outerRect.Width - 2f * thickness);
            float innerH = Math.Max(0f, outerRect.Height - 2f * thickness);
            float innerRadius = Math.Max(0f, outerRadius - thickness);
            RectangleF innerRect = new RectangleF(innerX, innerY, innerW, innerH);

            CardBorderGeometry geo = new CardBorderGeometry();
            geo.OuterRect = outerRect;
            geo.InnerRect = innerRect;
            geo.OuterRadius = outerRadius;
            geo.InnerRadius = innerRadius;
            geo.Thickness = thickness;

            geo.TopLeftOuterCenter = new PointF(outerRect.X + outerRadius, outerRect.Y + outerRadius);
            geo.TopLeftInnerCenter = new PointF(innerRect.X + innerRadius, innerRect.Y + innerRadius);

            geo.TopRightOuterCenter = new PointF(outerRect.Right - outerRadius, outerRect.Y + outerRadius);
            geo.TopRightInnerCenter = new PointF(innerRect.Right - innerRadius, innerRect.Y + innerRadius);

            geo.BottomRightOuterCenter = new PointF(outerRect.Right - outerRadius, outerRect.Bottom - outerRadius);
            geo.BottomRightInnerCenter = new PointF(innerRect.Right - innerRadius, innerRect.Bottom - innerRadius);

            geo.BottomLeftOuterCenter = new PointF(outerRect.X + outerRadius, outerRect.Bottom - outerRadius);
            geo.BottomLeftInnerCenter = new PointF(innerRect.X + innerRadius, innerRect.Bottom - innerRadius);

            return geo;
        }

        internal static void AddRoundedRectangleToPath(GraphicsPath path, RectangleF rect, float radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;

            float maxRadius = Math.Min(rect.Width / 2f, rect.Height / 2f);
            float r = Math.Min(radius, maxRadius);

            if (r <= 0.05f)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return;
            }

            float d = r * 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
        }

        internal static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            AddRoundedRectangleToPath(path, rect, radius);
            return path;
        }

        internal static GraphicsPath CreateRoundedRectangleRingPath(RectangleF outerRect, float outerRadius, float thickness)
        {
            GraphicsPath path = new GraphicsPath();
            path.FillMode = FillMode.Alternate;
            AddRoundedRectangleToPath(path, outerRect, outerRadius);

            float innerX = outerRect.X + thickness;
            float innerY = outerRect.Y + thickness;
            float innerW = Math.Max(0f, outerRect.Width - 2f * thickness);
            float innerH = Math.Max(0f, outerRect.Height - 2f * thickness);
            float innerRadius = Math.Max(0f, outerRadius - thickness);

            RectangleF innerRect = new RectangleF(innerX, innerY, innerW, innerH);
            AddRoundedRectangleToPath(path, innerRect, innerRadius);

            return path;
        }

        internal static void RenderCardBorderRing(Graphics g, int w, int h, CardBorderGeometry borderGeo, Color borderPenColor)
        {
            using (Bitmap borderBmp = CreateSymmetricBorderBitmap(w, h, borderGeo.OuterRect, borderGeo.OuterRadius, borderGeo.Thickness, borderPenColor))
            {
                InterpolationMode oldIm = g.InterpolationMode;
                PixelOffsetMode oldPom = g.PixelOffsetMode;
                CompositingMode oldCm = g.CompositingMode;
                CompositingQuality oldCq = g.CompositingQuality;
                try
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    borderBmp.SetResolution(g.DpiX, g.DpiY);
                    g.DrawImage(borderBmp, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel);
                }
                finally
                {
                    g.InterpolationMode = oldIm;
                    g.PixelOffsetMode = oldPom;
                    g.CompositingMode = oldCm;
                    g.CompositingQuality = oldCq;
                }
            }
        }

        internal static Bitmap CreateSymmetricBorderBitmap(int w, int h, RectangleF outerRect, float outerRadius, float thickness, Color borderPenColor)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] buffer = new byte[stride * h];

            float cx = outerRect.X + outerRect.Width / 2f;
            float cy = outerRect.Y + outerRect.Height / 2f;
            float hw = outerRect.Width / 2f;
            float hh = outerRect.Height / 2f;
            float hw_in = hw - thickness;
            float hh_in = hh - thickness;
            float innerRadius = Math.Max(0f, outerRadius - thickness);
            float qx = hw - outerRadius;
            float qy = hh - outerRadius;
            float r_out_sq = outerRadius * outerRadius;
            float r_in_sq = innerRadius * innerRadius;

            int N = 8;
            float subStep = 1.0f / N;
            float halfSub = 0.5f / N;
            float invTotal = 1.0f / (N * N);

            byte baseA = borderPenColor.A;
            byte baseR = borderPenColor.R;
            byte baseG = borderPenColor.G;
            byte baseB = borderPenColor.B;

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < w; x++)
                {
                    float minDx = (x <= cx && cx <= x + 1f) ? 0f : Math.Min(Math.Abs(x - cx), Math.Abs(x + 1f - cx));
                    float maxDx = Math.Max(Math.Abs(x - cx), Math.Abs(x + 1f - cx));
                    float minDy = (y <= cy && cy <= y + 1f) ? 0f : Math.Min(Math.Abs(y - cy), Math.Abs(y + 1f - cy));
                    float maxDy = Math.Max(Math.Abs(y - cy), Math.Abs(y + 1f - cy));

                    if (minDx > hw || minDy > hh) continue;
                    if (minDx > qx && minDy > qy)
                    {
                        float ex = minDx - qx;
                        float ey = minDy - qy;
                        if (ex * ex + ey * ey > r_out_sq) continue;
                    }

                    if (maxDx <= hw_in && maxDy <= hh_in)
                    {
                        if (maxDx <= qx || maxDy <= qy) continue;
                        float ex = maxDx - qx;
                        float ey = maxDy - qy;
                        if (ex * ex + ey * ey <= r_in_sq) continue;
                    }

                    bool fullyInOuter = (maxDx <= hw && maxDy <= hh);
                    if (fullyInOuter && maxDx > qx && maxDy > qy)
                    {
                        float ex = maxDx - qx;
                        float ey = maxDy - qy;
                        fullyInOuter = (ex * ex + ey * ey <= r_out_sq);
                    }

                    bool fullyOutInner = (minDx > hw_in || minDy > hh_in);
                    if (!fullyOutInner && minDx > qx && minDy > qy)
                    {
                        float ex = minDx - qx;
                        float ey = minDy - qy;
                        fullyOutInner = (ex * ex + ey * ey > r_in_sq);
                    }

                    int pxOffset = rowOffset + x * 4;
                    if (fullyInOuter && fullyOutInner)
                    {
                        buffer[pxOffset] = baseB;
                        buffer[pxOffset + 1] = baseG;
                        buffer[pxOffset + 2] = baseR;
                        buffer[pxOffset + 3] = baseA;
                        continue;
                    }

                    int hits = 0;
                    for (int sy = 0; sy < N; sy++)
                    {
                        float py = y + sy * subStep + halfSub;
                        float dy = Math.Abs(py - cy);
                        if (dy > hh) continue;

                        for (int sx = 0; sx < N; sx++)
                        {
                            float px = x + sx * subStep + halfSub;
                            float dx = Math.Abs(px - cx);
                            if (dx > hw) continue;

                            bool inOut;
                            if (dx <= qx || dy <= qy) inOut = true;
                            else
                            {
                                float ex = dx - qx;
                                float ey = dy - qy;
                                inOut = (ex * ex + ey * ey <= r_out_sq);
                            }
                            if (!inOut) continue;

                            bool inIn;
                            if (dx > hw_in || dy > hh_in) inIn = false;
                            else if (dx <= qx || dy <= qy) inIn = true;
                            else
                            {
                                float ex = dx - qx;
                                float ey = dy - qy;
                                inIn = (ex * ex + ey * ey <= r_in_sq);
                            }
                            if (!inIn) hits++;
                        }
                    }

                    if (hits > 0)
                    {
                        float coverage = hits * invTotal;
                        byte alpha = (byte)Math.Round(baseA * coverage);
                        if (alpha > 0)
                        {
                            buffer[pxOffset] = baseB;
                            buffer[pxOffset + 1] = baseG;
                            buffer[pxOffset + 2] = baseR;
                            buffer[pxOffset + 3] = alpha;
                        }
                    }
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_taskbarSpaceProvider != null)
                {
                    _taskbarSpaceProvider.SpaceChanged -= OnTaskbarSpaceChanged;
                    _taskbarSpaceProvider.Dispose();
                    _taskbarSpaceProvider = null;
                }
                _isMonitoring = false;
                if (_monitorThread != null && _monitorThread.IsAlive)
                {
                    _monitorThread.Join(200);
                    _monitorThread = null;
                }
                if (LastRenderedBitmapForTest != null)
                {
                    try { LastRenderedBitmapForTest.Dispose(); } catch { }
                    LastRenderedBitmapForTest = null;
                }
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            base.Dispose(disposing);
        }
    }
}
