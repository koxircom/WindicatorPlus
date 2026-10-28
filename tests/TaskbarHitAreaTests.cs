using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class MockHitTestSpaceProvider : ITaskbarSpaceProvider
    {
        private TaskbarSpaceSnapshot _snapshot;
        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

        public MockHitTestSpaceProvider(TaskbarSpaceSnapshot initialSnapshot = null)
        {
            _snapshot = initialSnapshot ?? TaskbarSpaceSnapshot.Invalid();
        }

        public TaskbarSpaceSnapshot GetLatestSnapshot()
        {
            return _snapshot;
        }

        public void SetSnapshot(TaskbarSpaceSnapshot snapshot, bool notify = true)
        {
            _snapshot = snapshot;
            if (notify && SpaceChanged != null)
            {
                SpaceChanged(snapshot);
            }
        }

        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    public class TaskbarHitAreaTests
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, int dwDesiredAccess, IntPtr lpsa);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(int dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] byte[] pvInfo, int nLength, out int lpnLengthNeeded);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point pt);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hwndParent, Point pt, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private const int UOI_NAME = 2;
        private const uint WM_NCHITTEST = 0x0084;
        private const uint CWP_ALL = 0x0000;

        private static string GetCurrentDesktopName()
        {
            try
            {
                IntPtr hDesktop = GetThreadDesktop(GetCurrentThreadId());
                if (hDesktop == IntPtr.Zero) return string.Empty;

                int needed = 0;
                GetUserObjectInformation(hDesktop, UOI_NAME, null, 0, out needed);
                if (needed <= 0) return string.Empty;

                byte[] buffer = new byte[needed];
                if (GetUserObjectInformation(hDesktop, UOI_NAME, buffer, needed, out needed))
                {
                    string name = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                    return name;
                }
            }
            catch { }
            return string.Empty;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }

        private static void SetField(object obj, string name, object val)
        {
            FieldInfo fi = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) throw new Exception("Field not found: " + name);
            fi.SetValue(obj, val);
        }

        private static object GetField(object obj, string name)
        {
            FieldInfo fi = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) throw new Exception("Field not found: " + name);
            return fi.GetValue(obj);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Hit Area & Click Tests");
            Console.WriteLine("==================================================");

            string desktopName = GetCurrentDesktopName();
            bool isIsolatedDesktop = (!string.IsNullOrEmpty(desktopName) && desktopName.StartsWith("WindicatorTest-", StringComparison.OrdinalIgnoreCase));

            // 安全守卫：如果在非测试桌面直接启动，自动通过 CreateDesktopW 创建隔离桌面并在隔离环境中运行，绝对不打扰用户当前桌面
            if (!isIsolatedDesktop)
            {
                if (args != null && args.Length > 0 && args[0] == "--isolated-child")
                {
                    Console.WriteLine("[SAFETY GUARD] TaskbarHitAreaTests execution rejected on default/non-test desktop. Current desktop: " + desktopName);
                    return 2;
                }

                Console.WriteLine("[SAFETY GUARD] Current desktop is '" + desktopName + "'. Auto-launching runner inside isolated desktop prefix 'WindicatorTest-'...");
                string testDeskName = "WindicatorTest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                IntPtr hNewDesk = CreateDesktop(testDeskName, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
                if (hNewDesk == IntPtr.Zero)
                {
                    Console.WriteLine("[SAFETY GUARD] CreateDesktop failed: " + Marshal.GetLastWin32Error());
                    return 2;
                }

                try
                {
                    STARTUPINFO si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf(si);
                    si.lpDesktop = @"winsta0\" + testDeskName;

                    PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    string cmdLine = "\"" + exePath + "\" --isolated-child";

                    bool spawned = CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi);
                    if (!spawned)
                    {
                        Console.WriteLine("[SAFETY GUARD] CreateProcess in isolated desktop failed: " + Marshal.GetLastWin32Error());
                        return 2;
                    }

                    WaitForSingleObject(pi.hProcess, 30000);
                    uint childExitCode = 0;
                    GetExitCodeProcess(pi.hProcess, out childExitCode);
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    Console.WriteLine("[SAFETY GUARD] Isolated desktop child exited with code: " + childExitCode);
                    return (int)childExitCode;
                }
                finally
                {
                    CloseDesktop(hNewDesk);
                }
            }

            Console.WriteLine("[SAFETY GUARD] Verified: Running inside isolated desktop: " + desktopName);

            try
            {
                TestOffscreenBitmapTaskbarDockedAlphaNonZeroAndDesktopZero();
                Console.WriteLine("PASS: TestOffscreenBitmapTaskbarDockedAlphaNonZeroAndDesktopZero");

                TestWin32HitTestingTaskbarDockedAndDesktopPenetration();
                Console.WriteLine("PASS: TestWin32HitTestingTaskbarDockedAndDesktopPenetration");

                TestTaskbarOverflowFloatingWholeRectHittable();
                Console.WriteLine("PASS: TestTaskbarOverflowFloatingWholeRectHittable");

                TestInvalidSnapshotConservativeHideAndNonHittable();
                Console.WriteLine("PASS: TestInvalidSnapshotConservativeHideAndNonHittable");

                TestNativeWmNcHitTestAndExistingEventRegression();
                Console.WriteLine("PASS: TestNativeWmNcHitTestAndExistingEventRegression");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL TASKBAR HIT AREA TESTS PASSED SUCCESSFULLY (5/5)");
                Console.WriteLine("==================================================");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }

        // Test 1: 离屏位图边角与空白像素 Alpha 验证（任务栏模式有效 Alpha 恒非 0，桌面模式未填背景保持 Alpha=0 穿透）
        private static void TestOffscreenBitmapTaskbarDockedAlphaNonZeroAndDesktopZero()
        {
            using (WatermarkWindow win = new WatermarkWindow { CaptureRenderedBitmapForTesting = true })
            {
                IntPtr h = win.Handle;
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1700, 1000, 220, 48);
                TaskbarSpaceSnapshot normalSnapshot = new TaskbarSpaceSnapshot(1200, 1700, tbRect, trRect, true);
                MockHitTestSpaceProvider provider = new MockHitTestSpaceProvider(normalSnapshot);

                win.SetTaskbarSpaceProvider(provider);
                win.HasTaskbarIntent = true;
                SetField(win, "_opacityPercent", 40); // 40% 全局低透明度

                win.UpdateWatermark();

                Bitmap dockedBmp = win.LastRenderedBitmapForTest;
                Assert(dockedBmp != null, "LastRenderedBitmapForTest must be non-null after UpdateWatermark");

                int maxAlpha = 0;
                for (int y = 0; y < dockedBmp.Height; y++)
                    for (int x = 0; x < dockedBmp.Width; x++) maxAlpha = Math.Max(maxAlpha, dockedBmp.GetPixel(x, y).A);
                Assert(maxAlpha > 1 && maxAlpha <= 102, "40% opacity must remain effective for taskbar text, not become opaque");
                int w = dockedBmp.Width;
                int hBmp = dockedBmp.Height;
                Assert(w > 20 && hBmp > 10, "Rendered widget dimensions must be positive");

                // 验证任务栏模式下四个角落（原本透明的空白区域）：Alpha 必须非 0（精确为 1），RGB 为 0（无肉眼可见边框）
                Color cTopLeft = dockedBmp.GetPixel(0, 0);
                Color cTopRight = dockedBmp.GetPixel(w - 1, 0);
                Color cBottomLeft = dockedBmp.GetPixel(0, hBmp - 1);
                Color cBottomRight = dockedBmp.GetPixel(w - 1, hBmp - 1);

                Assert(cTopLeft.A == 1 && cTopLeft.R == 0 && cTopLeft.G == 0 && cTopLeft.B == 0,
                    string.Format("Top-left pixel must be Alpha=1 Black, got A={0} R={1} G={2} B={3}", cTopLeft.A, cTopLeft.R, cTopLeft.G, cTopLeft.B));
                Assert(cTopRight.A == 1 && cTopRight.R == 0 && cTopRight.G == 0 && cTopRight.B == 0,
                    string.Format("Top-right pixel must be Alpha=1 Black, got A={0} R={1} G={2} B={3}", cTopRight.A, cTopRight.R, cTopRight.G, cTopRight.B));
                Assert(cBottomLeft.A == 1 && cBottomLeft.R == 0 && cBottomLeft.G == 0 && cBottomLeft.B == 0,
                    string.Format("Bottom-left pixel must be Alpha=1 Black, got A={0} R={1} G={2} B={3}", cBottomLeft.A, cBottomLeft.R, cBottomLeft.G, cBottomLeft.B));
                Assert(cBottomRight.A == 1 && cBottomRight.R == 0 && cBottomRight.G == 0 && cBottomRight.B == 0,
                    string.Format("Bottom-right pixel must be Alpha=1 Black, got A={0} R={1} G={2} B={3}", cBottomRight.A, cBottomRight.R, cBottomRight.G, cBottomRight.B));

                // 验证整张位图内部没有任何 Alpha == 0 的穿透洞
                for (int y = 0; y < hBmp; y += 4)
                {
                    for (int x = 0; x < w; x += 4)
                    {
                        Color p = dockedBmp.GetPixel(x, y);
                        Assert(p.A >= 1, string.Format("Taskbar widget pixel ({0},{1}) must have Alpha >= 1, got {2}", x, y, p.A));
                    }
                }

                // 对比验证：切换到桌面模式（HasTaskbarIntent = false），背景像素必须保持 Alpha == 0 完全未填充（桌面穿透）
                win.HasTaskbarIntent = false;
                SetField(win, "_style", "Minimal"); // Minimal 风格周围留有大面积透明空白
                win.UpdateWatermark();

                Bitmap deskBmp = win.LastRenderedBitmapForTest;
                Assert(deskBmp != null, "Desktop watermark bitmap must be non-null");
                Color deskCorner = deskBmp.GetPixel(0, 0);
                Assert(deskCorner.A == 0, string.Format("Desktop watermark corner pixel must be Alpha=0 transparent, got {0}", deskCorner.A));
            }
        }

        // Test 2: 真实 Win32 命中测试（WindowFromPoint & ChildWindowFromPointEx 验证整段 widget 矩形可点，且范围限制在 widget 内）
        private static void TestWin32HitTestingTaskbarDockedAndDesktopPenetration()
        {
            using (WatermarkWindow win = new WatermarkWindow { CaptureRenderedBitmapForTesting = true })
            {
                IntPtr h = win.Handle;
                win.StartPosition = FormStartPosition.Manual;
                win.Show();

                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1700, 1000, 220, 48);
                TaskbarSpaceSnapshot normalSnapshot = new TaskbarSpaceSnapshot(1200, 1700, tbRect, trRect, true);
                MockHitTestSpaceProvider provider = new MockHitTestSpaceProvider(normalSnapshot);

                win.SetTaskbarSpaceProvider(provider);
                win.HasTaskbarIntent = true;
                SetField(win, "_opacityPercent", 40);

                win.UpdateWatermark();

                Rectangle actualRect = (Rectangle)GetField(win, "_actualScreenRect");
                Assert(!actualRect.IsEmpty, "ActualScreenRect must not be empty after docked render");

                // 移除 WS_EX_TRANSPARENT 模拟鼠标进入 widget 区域交互状态
                MethodInfo miInteractive = typeof(WatermarkWindow).GetMethod("ApplyInteractiveState", BindingFlags.Instance | BindingFlags.NonPublic);
                miInteractive.Invoke(win, new object[] { true, false });

                IntPtr deskHwnd = GetDesktopWindow();

                // 验证 1：在 widget 矩形的四个角落空白区域（非文字笔画处），Windows 操作系统底层命中该窗口
                Point ptTopLeft = new Point(actualRect.Left + 1, actualRect.Top + 1);
                Point ptTopRight = new Point(actualRect.Right - 2, actualRect.Top + 1);
                Point ptBottomLeft = new Point(actualRect.Left + 1, actualRect.Bottom - 2);
                Point ptBottomRight = new Point(actualRect.Right - 2, actualRect.Bottom - 2);
                Point ptCenter = new Point(actualRect.Left + actualRect.Width / 2, actualRect.Top + actualRect.Height / 2);

                IntPtr hitTL = ChildWindowFromPointEx(deskHwnd, ptTopLeft, CWP_ALL);
                IntPtr hitTR = ChildWindowFromPointEx(deskHwnd, ptTopRight, CWP_ALL);
                IntPtr hitBL = ChildWindowFromPointEx(deskHwnd, ptBottomLeft, CWP_ALL);
                IntPtr hitBR = ChildWindowFromPointEx(deskHwnd, ptBottomRight, CWP_ALL);
                IntPtr hitCenter = ChildWindowFromPointEx(deskHwnd, ptCenter, CWP_ALL);

                Assert(hitTL == win.Handle, "Top-left blank corner must hit WatermarkWindow HWND, not penetrate");
                Assert(hitTR == win.Handle, "Top-right blank corner must hit WatermarkWindow HWND, not penetrate");
                Assert(hitBL == win.Handle, "Bottom-left blank corner must hit WatermarkWindow HWND, not penetrate");
                Assert(hitBR == win.Handle, "Bottom-right blank corner must hit WatermarkWindow HWND, not penetrate");
                Assert(hitCenter == win.Handle, "Center point must hit WatermarkWindow HWND");

                // 验证 2：范围限制在 widget 矩形内部，矩形外部的点严格不得命中该窗口
                Point ptOutsideLeft = new Point(actualRect.Left - 10, actualRect.Top + 10);
                Point ptOutsideAbove = new Point(actualRect.Left + 10, actualRect.Top - 10);
                IntPtr hitOutL = ChildWindowFromPointEx(deskHwnd, ptOutsideLeft, CWP_ALL);
                IntPtr hitOutA = ChildWindowFromPointEx(deskHwnd, ptOutsideAbove, CWP_ALL);
                Assert(hitOutL != win.Handle, "Outside left must not hit WatermarkWindow");
                Assert(hitOutA != win.Handle, "Outside above must not hit WatermarkWindow");

                // 验证 3：桌面水印模式下，空白区域由 OS 穿透回退到 Desktop Window
                win.HasTaskbarIntent = false;
                SetField(win, "_style", "Minimal");
                win.UpdateWatermark();
                // 桌面模式非交互状态具有 WS_EX_TRANSPARENT 且背景 A=0
                Rectangle deskRect = (Rectangle)GetField(win, "_actualScreenRect");
                Point ptDeskCorner = new Point(deskRect.Left + 1, deskRect.Top + 1);
                IntPtr deskHit = ChildWindowFromPointEx(deskHwnd, ptDeskCorner, CWP_ALL);
                Assert(deskHit == deskHwnd || deskHit != win.Handle, "Desktop mode corner must penetrate to desktop window");
            }
        }

        // Test 3: 任务栏溢出浮动状态（_isTaskbarOverflowFloating）整段 widget 矩形同样有效可点
        private static void TestTaskbarOverflowFloatingWholeRectHittable()
        {
            using (WatermarkWindow win = new WatermarkWindow { CaptureRenderedBitmapForTesting = true })
            {
                IntPtr h = win.Handle;
                win.StartPosition = FormStartPosition.Manual;
                win.Show();

                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1000, 1000, 920, 48);
                // 空间被挤占满：availableWidth <= 0 -> 触发 overflow floating
                TaskbarSpaceSnapshot crowdedSnapshot = new TaskbarSpaceSnapshot(1000, 1000, tbRect, trRect, true);
                MockHitTestSpaceProvider provider = new MockHitTestSpaceProvider(crowdedSnapshot);

                win.SetTaskbarSpaceProvider(provider);
                win.HasTaskbarIntent = true;
                win.UpdateWatermark();

                bool isFloating = (bool)GetField(win, "_isTaskbarOverflowFloating");
                Assert(isFloating, "Watermark must enter _isTaskbarOverflowFloating mode under crowded space");

                Bitmap floatBmp = win.LastRenderedBitmapForTest;
                Assert(floatBmp != null, "Floating bitmap must exist");

                // 浮动模式下外圈四个角落像素：底色填充 Alpha=1 黑色，必须 Alpha >= 1
                Color c0 = floatBmp.GetPixel(0, 0);
                Color cMax = floatBmp.GetPixel(floatBmp.Width - 1, floatBmp.Height - 1);
                Assert(c0.A >= 1, "Floating widget top-left corner must have Alpha >= 1");
                Assert(cMax.A >= 1, "Floating widget bottom-right corner must have Alpha >= 1");

                // Win32 命中测试
                MethodInfo miInteractive = typeof(WatermarkWindow).GetMethod("ApplyInteractiveState", BindingFlags.Instance | BindingFlags.NonPublic);
                miInteractive.Invoke(win, new object[] { true, false });

                Rectangle floatRect = (Rectangle)GetField(win, "_actualScreenRect");
                IntPtr deskHwnd = GetDesktopWindow();
                IntPtr hitFloatCorner = ChildWindowFromPointEx(deskHwnd, new Point(floatRect.Left + 1, floatRect.Top + 1), CWP_ALL);
                Assert(hitFloatCorner == win.Handle, "Floating watermark corner must hit WatermarkWindow HWND");
            }
        }

        // Test 4: 空间快照失效时保守隐藏，不可点击
        private static void TestInvalidSnapshotConservativeHideAndNonHittable()
        {
            using (WatermarkWindow win = new WatermarkWindow { CaptureRenderedBitmapForTesting = true })
            {
                IntPtr h = win.Handle;
                win.StartPosition = FormStartPosition.Manual;
                win.Show();

                // 提供一个失效快照
                MockHitTestSpaceProvider provider = new MockHitTestSpaceProvider(TaskbarSpaceSnapshot.Invalid());
                win.SetTaskbarSpaceProvider(provider);
                win.HasTaskbarIntent = true;

                win.UpdateWatermark();

                Rectangle actualRect = (Rectangle)GetField(win, "_actualScreenRect");
                Assert(actualRect.IsEmpty, "ActualScreenRect must be empty when snapshot is invalid");

                // 窗口隐藏后，原区域不得命中
                IntPtr deskHwnd = GetDesktopWindow();
                IntPtr hit = ChildWindowFromPointEx(deskHwnd, new Point(100, 1000), CWP_ALL);
                Assert(hit != win.Handle, "Invalid snapshot hidden watermark must not be hittable");
            }
        }

        // Test 5: Native WM_NCHITTEST 与现有鼠标事件回归（左/右键触发菜单、拖动不触发菜单、防抖节流）
        private static void TestNativeWmNcHitTestAndExistingEventRegression()
        {
            using (WatermarkWindow win = new WatermarkWindow { CaptureRenderedBitmapForTesting = true })
            {
                IntPtr h = win.Handle;
                win.StartPosition = FormStartPosition.Manual;
                win.Show();

                bool menuTriggered = false;
                win.RequestContextMenu += (s, e) => { menuTriggered = true; };

                win.HasTaskbarIntent = true;
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1700, 1000, 220, 48);
                TaskbarSpaceSnapshot normalSnapshot = new TaskbarSpaceSnapshot(1200, 1700, tbRect, trRect, true);
                MockHitTestSpaceProvider provider = new MockHitTestSpaceProvider(normalSnapshot);
                win.SetTaskbarSpaceProvider(provider);
                win.UpdateWatermark();

                Rectangle actualRect = (Rectangle)GetField(win, "_actualScreenRect");

                // 1. Native WM_NCHITTEST 发送给窗口测试
                IntPtr lParam = (IntPtr)((actualRect.Top << 16) | (actualRect.Left & 0xFFFF));
                IntPtr hitRes = SendMessage(win.Handle, WM_NCHITTEST, IntPtr.Zero, lParam);
                Assert((int)hitRes == 1, "WM_NCHITTEST must return 1 (HTCLIENT)");

                MethodInfo miOnMouseUp = typeof(WatermarkWindow).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);

                // 2. 左键点击任意空白位置（例如边角）触发菜单
                menuTriggered = false;
                SetField(win, "_lastMenuTriggerTick", 0L);
                MouseEventArgs eLeft = new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0);
                miOnMouseUp.Invoke(win, new object[] { eLeft });
                Assert(menuTriggered, "Left click anywhere on widget must trigger RequestContextMenu");

                // 3. 右键点击任意空白位置触发菜单
                menuTriggered = false;
                SetField(win, "_lastMenuTriggerTick", 0L);
                MouseEventArgs eRight = new MouseEventArgs(MouseButtons.Right, 1, actualRect.Width - 2, 1, 0);
                miOnMouseUp.Invoke(win, new object[] { eRight });
                Assert(menuTriggered, "Right click anywhere on widget must trigger RequestContextMenu");

                // 4. 拖动状态（_isDragging = true）下 OnMouseUp 绝不触发菜单
                menuTriggered = false;
                SetField(win, "_lastMenuTriggerTick", 0L);
                SetField(win, "_isDragging", true);
                miOnMouseUp.Invoke(win, new object[] { eLeft });
                Assert(!menuTriggered, "Dragging mouse up must NOT trigger RequestContextMenu");
                Assert(!(bool)GetField(win, "_isDragging"), "Dragging flag must be reset to false after mouse up");

                // 5. 防抖/节流（Debounce）：连续点击在 500ms 内不重复触发
                SetField(win, "_lastMenuTriggerTick", Stopwatch.GetTimestamp());
                menuTriggered = false;
                miOnMouseUp.Invoke(win, new object[] { eLeft });
                Assert(!menuTriggered, "Immediate consecutive click within debounce window must be suppressed");
            }
        }
    }
}
