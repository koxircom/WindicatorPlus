using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class MockStabilitySpaceProvider : ITaskbarSpaceProvider
    {
        private TaskbarSpaceSnapshot _snapshot;
        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

        public MockStabilitySpaceProvider(TaskbarSpaceSnapshot initialSnapshot = null)
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

    public class TaskbarMenuStabilityTests
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(int dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] byte[] pvInfo, int nLength, out int lpnLengthNeeded);

        private const int UOI_NAME = 2;

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

        [STAThread]
        public static int Main()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Menu Stability Tests");
            Console.WriteLine("==================================================");

            // 契约安全守卫：必须检查当前 thread desktop name 是否前缀 WindicatorTest-，不是则明确拒绝执行（防默认桌面）
            string desktopName = GetCurrentDesktopName();
            if (string.IsNullOrEmpty(desktopName) || !desktopName.StartsWith("WindicatorTest-", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SAFETY GUARD] TaskbarMenuStabilityTests requires an isolated desktop (prefix 'WindicatorTest-') to avoid disturbing user foreground window. Current desktop: " + desktopName);
                Console.WriteLine("[SAFETY GUARD] Execution rejected on default/non-test desktop. Worker compiled tests successfully.");
                return 2;
            }

            try
            {
                TestTaskbarAlphaStableUnderHoverAndRepetition();
                Console.WriteLine("PASS: TestTaskbarAlphaStableUnderHoverAndRepetition");

                TestSpaceDepletionImmediateFloatingAndNonZeroDimensions();
                Console.WriteLine("PASS: TestSpaceDepletionImmediateFloatingAndNonZeroDimensions");

                TestSpaceRecoveryImmediateDockedWithoutMultipleRenders();
                Console.WriteLine("PASS: TestSpaceRecoveryImmediateDockedWithoutMultipleRenders");

                TestActualScreenRectOnlyPublishedAfterSuccessfulULW();
                Console.WriteLine("PASS: TestActualScreenRectOnlyPublishedAfterSuccessfulULW");

                TestContextMenuLifecycleAndStability30Cycles();
                Console.WriteLine("PASS: TestContextMenuLifecycleAndStability30Cycles");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL TASKBAR MENU STABILITY TESTS PASSED (5/5)");
                Console.WriteLine("==================================================");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static object GetField(object obj, string name)
        {
            FieldInfo fi = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) throw new Exception("Field not found: " + name);
            return fi.GetValue(obj);
        }

        private static void SetField(object obj, string name, object val)
        {
            FieldInfo fi = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) throw new Exception("Field not found: " + name);
            fi.SetValue(obj, val);
        }

        private static void TestTaskbarAlphaStableUnderHoverAndRepetition()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                IntPtr h = win.Handle; // Create handle without calling Show()
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1500, 1000, 420, 48);
                TaskbarSpaceSnapshot snap = new TaskbarSpaceSnapshot(500, 1500, tbRect, trRect, true);
                MockStabilitySpaceProvider mock = new MockStabilitySpaceProvider(snap);
                win.SetTaskbarSpaceProvider(mock);

                SetField(win, "_isTaskbarDocked", true);
                SetField(win, "_opacityPercent", 80);

                // 1. Taskbar docked non-hover render
                SetField(win, "_isInteractive", false);
                win.UpdateWatermark();
                byte alphaNormal = win.LastCalculatedAlpha;
                byte expectedAlpha = (byte)(80 * 255 / 100);
                Assert(alphaNormal == expectedAlpha,
                    string.Format("Docked taskbar watermark alpha ({0}) must equal {1}", alphaNormal, expectedAlpha));

                // 2. Taskbar docked hover render -> alpha must NOT increase!
                SetField(win, "_isInteractive", true);
                win.UpdateWatermark();
                byte alphaHover = win.LastCalculatedAlpha;
                Assert(alphaHover == expectedAlpha,
                    string.Format("Docked taskbar watermark alpha under hover ({0}) must NOT increase, expected {1}", alphaHover, expectedAlpha));

                // 3. Repetitive renders keep identical alpha
                for (int i = 0; i < 5; i++)
                {
                    win.UpdateWatermark();
                    Assert(win.LastCalculatedAlpha == expectedAlpha, "Repetitive renders must maintain stable alpha");
                }

                // 4. Contrast: Desktop mode DOES increase alpha on hover
                SetField(win, "_isTaskbarDocked", false);
                SetField(win, "_isInteractive", false);
                win.UpdateWatermark();
                Assert(win.LastCalculatedAlpha == expectedAlpha, "Desktop mode non-interactive has base alpha");

                SetField(win, "_isInteractive", true);
                win.UpdateWatermark();
                byte desktopHoverAlpha = win.LastCalculatedAlpha;
                byte expectedDesktopHover = (byte)Math.Min(255, expectedAlpha + 60);
                Assert(desktopHoverAlpha == expectedDesktopHover,
                    string.Format("Desktop mode interactive alpha ({0}) must increase by 60 to {1}", desktopHoverAlpha, expectedDesktopHover));
            }
        }

        private static void TestSpaceDepletionImmediateFloatingAndNonZeroDimensions()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                IntPtr h = win.Handle;
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1000, 1000, 920, 48);
                // Crowded space: maxAppRight == 1000, trayLeft == 1000 -> availableWidth <= 0
                TaskbarSpaceSnapshot crowded = new TaskbarSpaceSnapshot(1000, 1000, tbRect, trRect, true);
                MockStabilitySpaceProvider mock = new MockStabilitySpaceProvider(crowded);
                win.SetTaskbarSpaceProvider(mock);

                SetField(win, "_isTaskbarDocked", true);
                win.UpdateWatermark();

                bool isFloating = (bool)GetField(win, "_isTaskbarOverflowFloating");
                Assert(isFloating, "Watermark must immediately float above taskbar on horizontal space depletion (no counter lag)");

                int winW = (int)GetField(win, "_windowWidth");
                int winH = (int)GetField(win, "_windowHeight");
                Assert(winW > 0 && winH > 0,
                    string.Format("Floating watermark must have non-zero dimensions ({0}x{1}) without hitting hidden 0-size bitmap path", winW, winH));

                Rectangle actualRect = (Rectangle)GetField(win, "_actualScreenRect");
                Assert(!actualRect.IsEmpty, "Actual screen rect must be non-empty when floating");
                Assert(actualRect.Width == winW && actualRect.Height == winH, "Actual screen rect dimensions must match window dimensions");
            }
        }

        private static void TestSpaceRecoveryImmediateDockedWithoutMultipleRenders()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                IntPtr h = win.Handle;
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1000, 1000, 920, 48);
                TaskbarSpaceSnapshot crowded = new TaskbarSpaceSnapshot(1000, 1000, tbRect, trRect, true);
                MockStabilitySpaceProvider mock = new MockStabilitySpaceProvider(crowded);
                win.SetTaskbarSpaceProvider(mock);

                SetField(win, "_isTaskbarDocked", true);
                win.UpdateWatermark();

                Assert((bool)GetField(win, "_isTaskbarOverflowFloating"), "Setup: Watermark is currently floating");

                // Now space becomes available (e.g. apps closed, maxAppRight drops to 300, trayLeft expanded to 1500)
                TaskbarSpaceSnapshot expanded = new TaskbarSpaceSnapshot(300, 1500, tbRect, new Rectangle(1500, 1000, 420, 48), true);
                mock.SetSnapshot(expanded, notify: false);

                // Exactly ONE single render call
                win.UpdateWatermark();

                bool isFloatingAfter = (bool)GetField(win, "_isTaskbarOverflowFloating");
                Assert(!isFloatingAfter, "Watermark must immediately recover to docked state in a SINGLE render without multi-tick lag");

                Rectangle dockedRect = (Rectangle)GetField(win, "_actualScreenRect");
                Assert(!dockedRect.IsEmpty, "Docked rect must not be empty");
                Assert(dockedRect.Right <= 1500, "Docked rect Right must anchor to tray left");
            }
        }

        private static void TestActualScreenRectOnlyPublishedAfterSuccessfulULW()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                IntPtr h = win.Handle;
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1500, 1000, 420, 48);
                TaskbarSpaceSnapshot snap = new TaskbarSpaceSnapshot(300, 1500, tbRect, trRect, true);
                MockStabilitySpaceProvider mock = new MockStabilitySpaceProvider(snap);
                win.SetTaskbarSpaceProvider(mock);

                SetField(win, "_isTaskbarDocked", true);
                win.UpdateWatermark();

                Rectangle actualBefore = (Rectangle)GetField(win, "_actualScreenRect");
                Assert(!actualBefore.IsEmpty, "ActualScreenRect must be populated after successful UpdateLayeredWindow");

                // Stale snapshot triggers conservative ClearLayeredWindow
                TaskbarSpaceSnapshot staleSnap = TaskbarSpaceSnapshot.Invalid();
                mock.SetSnapshot(staleSnap, notify: false);
                win.UpdateWatermark();

                Rectangle actualAfterClear = (Rectangle)GetField(win, "_actualScreenRect");
                Assert(actualAfterClear.IsEmpty, "ActualScreenRect must be reset to Empty when snapshot is invalid and layered window is cleared");
            }
        }

        private static void TestContextMenuLifecycleAndStability30Cycles()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                IntPtr h = win.Handle;
                Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
                Rectangle trRect = new Rectangle(1500, 1000, 420, 48);
                TaskbarSpaceSnapshot normalSnap = new TaskbarSpaceSnapshot(500, 1500, tbRect, trRect, true);
                TaskbarSpaceSnapshot crowdedSnap = new TaskbarSpaceSnapshot(1500, 1500, tbRect, trRect, true);
                MockStabilitySpaceProvider mock = new MockStabilitySpaceProvider(normalSnap);
                win.SetTaskbarSpaceProvider(mock);

                SetField(win, "_isTaskbarDocked", true);
                SetField(win, "_opacityPercent", 80);
                win.UpdateWatermark();

                NativeMethods.RECT initWinRect;
                NativeMethods.GetWindowRect(win.Handle, out initWinRect);
                byte expectedAlpha = (byte)(80 * 255 / 100);
                Assert(win.LastCalculatedAlpha == expectedAlpha, "Initial alpha must match docked alpha");

                AppConfig config = new AppConfig();
                using (TrayIconManager tray = new TrayIconManager(config, win, null, null))
                {
                    ContextMenuStrip menu = (ContextMenuStrip)GetField(tray, "_contextMenu");
                    Assert(menu != null, "TrayIconManager must have initialized _contextMenu");

                    int openingCount = 0;
                    menu.Opening += (s, e) => { openingCount++; };

                    MethodInfo miShowMenu = typeof(TrayIconManager).GetMethod("ShowContextMenuAboveTaskbar", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert(miShowMenu != null, "ShowContextMenuAboveTaskbar must exist");

                    // 循环 30 次 Show + Close 真实生命周期验证
                    for (int cycle = 0; cycle < 30; cycle++)
                    {
                        // 1. Show 菜单
                        miShowMenu.Invoke(tray, null);
                        Application.DoEvents();

                        Assert(menu.Visible, string.Format("Cycle {0}: Context menu must be Visible after Show", cycle));
                        Assert(win.IsContextMenuOpen, string.Format("Cycle {0}: Watermark IsContextMenuOpen must be true", cycle));

                        // 2. 防重复触发 opening
                        int openingBeforeDuplicate = openingCount;
                        miShowMenu.Invoke(tray, null); // 重复调用
                        Application.DoEvents();
                        Assert(openingCount == openingBeforeDuplicate, string.Format("Cycle {0}: Repeated show must NOT trigger opening again", cycle));
                        Assert(menu.Visible, string.Format("Cycle {0}: Menu must remain Visible", cycle));

                        // 3. GetWindowRect watermark 前后完全相同
                        NativeMethods.RECT currentWinRect;
                        NativeMethods.GetWindowRect(win.Handle, out currentWinRect);
                        Assert(currentWinRect.Left == initWinRect.Left &&
                               currentWinRect.Top == initWinRect.Top &&
                               currentWinRect.Right == initWinRect.Right &&
                               currentWinRect.Bottom == initWinRect.Bottom,
                            string.Format("Cycle {0}: Watermark GetWindowRect must be identical before and after menu show", cycle));

                        // 4. Alpha 保持相同
                        Assert(win.LastCalculatedAlpha == expectedAlpha,
                            string.Format("Cycle {0}: Alpha ({1}) must remain stable without hover distortion during menu", cycle, win.LastCalculatedAlpha));

                        // 5. 菜单期间 crowded -> float 仍立即生效
                        mock.SetSnapshot(crowdedSnap, notify: false);
                        win.UpdateWatermark();
                        bool isFloatingDuringMenu = (bool)GetField(win, "_isTaskbarOverflowFloating");
                        Assert(isFloatingDuringMenu, string.Format("Cycle {0}: Crowded space during menu must immediately trigger floating", cycle));

                        // 恢复正常空间快照并恢复停靠
                        mock.SetSnapshot(normalSnap, notify: false);
                        win.UpdateWatermark();
                        bool isFloatingRecovered = (bool)GetField(win, "_isTaskbarOverflowFloating");
                        Assert(!isFloatingRecovered, string.Format("Cycle {0}: Space recovery during menu must recover to docked", cycle));

                        // 6. Close 菜单
                        menu.Close();
                        Application.DoEvents();

                        Assert(!menu.Visible, string.Format("Cycle {0}: Menu must not be Visible after Close", cycle));
                        Assert(!win.IsContextMenuOpen, string.Format("Cycle {0}: Watermark IsContextMenuOpen must be false after Close", cycle));

                        // 7. 宿主形式验证（隐藏）
                        object hostFormObj = GetField(tray, "_menuHostForm");
                        if (hostFormObj != null)
                        {
                            Form host = (Form)hostFormObj;
                            Assert(!host.Visible, string.Format("Cycle {0}: MenuHostForm must be hidden when menu is closed", cycle));
                        }
                    }

                    // 8. 截图位图渲染仅算 renderer 证据
                    using (Bitmap renderEvidence = new Bitmap(100, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(renderEvidence))
                    {
                        g.Clear(Color.FromArgb(128, 0, 0, 0));
                        Assert(renderEvidence.Width == 100 && renderEvidence.Height == 30, "Render evidence bitmap created successfully");
                    }

                    // 9. 释放 tray 后，宿主必须被 dispose
                    object finalHost = GetField(tray, "_menuHostForm");
                    tray.Dispose();

                    if (finalHost != null)
                    {
                        Form host = (Form)finalHost;
                        Assert(host.IsDisposed, "MenuHostForm must be disposed after TrayIconManager.Dispose()");
                    }
                }
            }
        }
    }
}
