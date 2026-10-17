using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class TaskbarOverlayOrderTests
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

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion Failed: " + message);
            }
        }

        private sealed class SyntheticShellForm : Form
        {
            public SyntheticShellForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(0, 1000, 1920, 48);
                TopMost = true;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }
        }

        private sealed class TopmostSentinelForm : Form
        {
            public TopmostSentinelForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(500, 500, 60, 60);
                TopMost = true;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }
        }

        private sealed class WindowPosMsgTracker : NativeWindow
        {
            public int WindowPosChangingCount = 0;

            protected override void WndProc(ref Message m)
            {
                const int WM_WINDOWPOSCHANGING = 0x0046;
                if (m.Msg == WM_WINDOWPOSCHANGING)
                {
                    WindowPosChangingCount++;
                }
                base.WndProc(ref m);
            }
        }

        private sealed class FakeContentionSpaceProvider : ITaskbarSpaceProvider
        {
            private readonly Rectangle _tbRect;
            private readonly Rectangle _trRect;
            private readonly int _maxAppRight;

#pragma warning disable 0067
            public event Action<TaskbarSpaceSnapshot> SpaceChanged;
#pragma warning restore 0067

            public FakeContentionSpaceProvider(Rectangle taskbarRect, Rectangle trayRect, int maxAppRight)
            {
                _tbRect = taskbarRect;
                _trRect = trayRect;
                _maxAppRight = maxAppRight;
            }

            public TaskbarSpaceSnapshot GetLatestSnapshot()
            {
                return new TaskbarSpaceSnapshot(_maxAppRight, _trRect.Left, _tbRect, _trRect, true, Stopwatch.GetTimestamp());
            }

            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
        }

        [STAThread]
        public static int Main()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Overlay Order Tests");
            Console.WriteLine("==================================================");

            // 契约安全守卫：必须检查当前 thread desktop name 是否前缀 WindicatorTest-，不是则明确拒绝执行（防默认桌面）
            string desktopName = GetCurrentDesktopName();
            if (string.IsNullOrEmpty(desktopName) || !desktopName.StartsWith("WindicatorTest-", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SAFETY GUARD] TaskbarOverlayOrderTests requires an isolated desktop (prefix 'WindicatorTest-') to avoid disturbing user foreground window. Current desktop: " + desktopName);
                Console.WriteLine("[SAFETY GUARD] Execution rejected on default/non-test desktop. Worker compiled tests successfully.");
                return 2;
            }

            try
            {
                TestNoShellTintNeverFullScreen();
                Console.WriteLine("PASS: TestNoShellTintNeverFullScreen");

                TestDisabledOverlayNeverReshown();
                Console.WriteLine("PASS: TestDisabledOverlayNeverReshown");

                TestTimerDisposalNoResidual();
                Console.WriteLine("PASS: TestTimerDisposalNoResidual");

                TestSyntheticShellContention30Cycles();
                Console.WriteLine("PASS: TestSyntheticShellContention30Cycles");

                TestAlreadyCorrectNoChange();
                Console.WriteLine("PASS: TestAlreadyCorrectNoChange");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL TASKBAR OVERLAY ORDER TESTS PASSED SUCCESSFULLY (5/5)");
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

        private static void TestNoShellTintNeverFullScreen()
        {
            AppConfig config = new AppConfig();
            config.TaskbarTintEnabled = false; // 初始 disabled

            // Geometry provider returns Empty, simulating no shell present
            using (TaskbarTintOverlay tint = new TaskbarTintOverlay(config, 1, () => Rectangle.Empty))
            {
                // 显式创建句柄，避免仅调用 CreateControl 未真正创建 Win32 句柄导致空跑
                IntPtr hTint = tint.Handle;
                Assert(hTint != IntPtr.Zero && NativeMethods.IsWindow(hTint), "Tint overlay Win32 handle must be explicitly created");

                // 1. disabled 状态验证
                Assert(!tint.Visible, "Tint must remain not visible when initially disabled");

                // 2. 切换为 enabled 验证
                tint.SetOverlayEnabled(true);
                tint.RenderTintOverlay();
                Application.DoEvents();

                // 断言 native rect 必定为 1x1，绝不覆盖全屏
                NativeMethods.RECT rc;
                Assert(NativeMethods.GetWindowRect(hTint, out rc), "GetWindowRect on tint must succeed");
                int width = rc.Right - rc.Left;
                int height = rc.Bottom - rc.Top;
                Assert(width == 1 && height == 1,
                    string.Format("In no-shell mode when enabled, layered window must be native 1x1, got {0}x{1}", width, height));

                Rectangle primary = Screen.PrimaryScreen.Bounds;
                Assert(width != primary.Width || height != primary.Height,
                    "Tint overlay must NOT cover full screen when no shell is detected");

                // 3. 再次切换回 disabled 验证
                tint.SetOverlayEnabled(false);
                Application.DoEvents();

                Assert(!tint.Visible, "Tint must become hidden after switching back to disabled");
                Assert(NativeMethods.GetWindowRect(hTint, out rc), "GetWindowRect on disabled tint must succeed");
                int disabledW = rc.Right - rc.Left;
                int disabledH = rc.Bottom - rc.Top;
                Assert(disabledW == 1 && disabledH == 1,
                    string.Format("Tint native dimensions must remain 1x1 after returning to disabled, got {0}x{1}", disabledW, disabledH));
            }
        }

        private static void TestDisabledOverlayNeverReshown()
        {
            AppConfig config = new AppConfig();
            config.TaskbarTintEnabled = false; // Initially disabled
            config.WatermarkEnabled = true;
            config.WatermarkPosition = "Custom";

            Rectangle fakeShellRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle fakeTrayRect = new Rectangle(1500, 1000, 420, 48);

            using (FakeContentionSpaceProvider spaceProvider = new FakeContentionSpaceProvider(fakeShellRect, fakeTrayRect, 500))
            using (SyntheticShellForm shell = new SyntheticShellForm())
            using (WatermarkWindow watermark = new WatermarkWindow())
            using (TaskbarTintOverlay tint = new TaskbarTintOverlay(config, 1, () => shell.Bounds))
            {
                watermark.SetTaskbarSpaceProvider(spaceProvider);
                shell.Show();
                watermark.HasTaskbarIntent = true;
                watermark.Show();
                watermark.UpdateWatermark();

                using (TrayIconManager tray = new TrayIconManager(config, watermark, null, tint))
                {
                    // Tint should NOT be visible when disabled
                    Assert(!tint.Visible, "Tint must remain not visible when disabled");

                    // Reconcile must not re-show disabled tint
                    tray.ReconcileTaskbarOverlayOrder(shell.Handle);
                    Assert(!tint.Visible, "ReconcileTaskbarOverlayOrder must never re-show disabled tint");

                    // Now disable watermark as well
                    watermark.SetWatermarkEnabled(false);
                    watermark.Hide();
                    Assert(!watermark.Visible, "Watermark must be hidden after SetWatermarkEnabled(false)");

                    tray.ReconcileTaskbarOverlayOrder(shell.Handle);
                    Assert(!watermark.Visible, "ReconcileTaskbarOverlayOrder must never re-show disabled watermark");
                }
            }
        }

        private static void TestTimerDisposalNoResidual()
        {
            AppConfig config = new AppConfig();
            using (WatermarkWindow watermark = new WatermarkWindow())
            using (TaskbarTintOverlay tint = new TaskbarTintOverlay(config, 1))
            {
                TrayIconManager tray = new TrayIconManager(config, watermark, null, tint);

                FieldInfo timerField = typeof(TrayIconManager).GetField("_zOrderTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(timerField != null, "_zOrderTimer field must exist in TrayIconManager");

                Timer timer = timerField.GetValue(tray) as Timer;
                Assert(timer != null && timer.Enabled, "TrayIconManager Z-order timer must be running");

                tray.Dispose();

                Timer disposedTimer = timerField.GetValue(tray) as Timer;
                Assert(disposedTimer == null || !disposedTimer.Enabled,
                    "TrayIconManager timer must be disposed and stopped after Dispose()");
            }
        }

        private static void TestSyntheticShellContention30Cycles()
        {
            AppConfig config = new AppConfig();
            config.TaskbarTintEnabled = true;
            config.WatermarkEnabled = true;
            config.WatermarkPosition = "Custom";
            config.CustomX = 100;
            config.CustomY = 1008;

            Rectangle fakeShellRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle fakeTrayRect = new Rectangle(1500, 1000, 420, 48);
            int fakeMaxAppRight = 500;

            using (FakeContentionSpaceProvider spaceProvider = new FakeContentionSpaceProvider(fakeShellRect, fakeTrayRect, fakeMaxAppRight))
            using (SyntheticShellForm shell = new SyntheticShellForm())
            using (WatermarkWindow watermark = new WatermarkWindow())
            using (TaskbarTintOverlay tint = new TaskbarTintOverlay(config, 1, () => shell.Bounds))
            {
                // 1. 注入专用 ITaskbarSpaceProvider，返回最新 fake shell/tray snapshot，防止在无 Explorer 测试桌面走 invalid -> 1x1
                watermark.SetTaskbarSpaceProvider(spaceProvider);

                // 显式创建句柄
                IntPtr hShell = shell.Handle;
                IntPtr hTint = tint.Handle;
                IntPtr hWater = watermark.Handle;

                shell.Show();

                tint.Show();
                tint.RenderTintOverlay();

                watermark.HasTaskbarIntent = true;
                watermark.Show();
                watermark.SetDesktop(1, 1, "Desk1");
                watermark.UpdateWatermark();
                Application.DoEvents();

                // 使用 native GetWindowRect 获取真实物理窗口尺寸，确认 actual > 1 尺寸，证明文字/色层确实有图
                NativeMethods.RECT rcWaterInitial;
                Assert(NativeMethods.GetWindowRect(hWater, out rcWaterInitial), "Watermark GetWindowRect must succeed");
                int initWaterW = rcWaterInitial.Right - rcWaterInitial.Left;
                int initWaterH = rcWaterInitial.Bottom - rcWaterInitial.Top;
                Assert(initWaterW > 1 && initWaterH > 1,
                    string.Format("Watermark actual native dimensions must be > 1, got {0}x{1}", initWaterW, initWaterH));

                NativeMethods.RECT rcTintInitial;
                Assert(NativeMethods.GetWindowRect(hTint, out rcTintInitial), "Tint GetWindowRect must succeed");
                int initTintW = rcTintInitial.Right - rcTintInitial.Left;
                int initTintH = rcTintInitial.Bottom - rcTintInitial.Top;
                Assert(initTintW > 1 && initTintH > 1,
                    string.Format("Tint actual native dimensions must be > 1, got {0}x{1}", initTintW, initTintH));

                byte initialWaterAlpha = watermark.LastCalculatedAlpha;
                Assert(initialWaterAlpha > 0, "Watermark initial alpha must be > 0");

                using (TrayIconManager tray = new TrayIconManager(config, watermark, null, tint))
                {
                    ContextMenuStrip menu = typeof(TrayIconManager)
                        .GetField("_contextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(tray) as ContextMenuStrip;
                    Assert(menu != null, "ContextMenuStrip must exist");

                    // 30 轮抢顶测试：前 15 轮测试菜单打开（Menu > Water > Tint > Shell），后 15 轮测试菜单关闭（Water > Tint > Shell）
                    // 覆盖开闭状态 + alpha 稳定性 + 文字/色层真实有图
                    for (int round = 1; round <= 30; round++)
                    {
                        bool menuShouldBeOpen = (round <= 15);

                        if (menuShouldBeOpen)
                        {
                            if (!menu.Visible)
                            {
                                menu.Show(shell, new Point(200, -100));
                                Application.DoEvents();
                            }

                            // 确认 menu 真实 Visible 且 native 尺寸 > 1
                            Assert(menu.Visible, string.Format("Round {0}: ContextMenu must be reported Visible", round));
                            IntPtr hMenu = menu.Handle;
                            Assert(NativeMethods.IsWindowVisible(hMenu), string.Format("Round {0}: ContextMenu native window must be visible", round));
                            NativeMethods.RECT rcMenu;
                            Assert(NativeMethods.GetWindowRect(hMenu, out rcMenu), "Menu GetWindowRect must succeed");
                            Assert((rcMenu.Right - rcMenu.Left) > 1 && (rcMenu.Bottom - rcMenu.Top) > 1,
                                string.Format("Round {0}: Menu native dimensions must be > 1", round));

                            // 模拟 Windows Shell 抢顶
                            NativeMethods.SetWindowPos(
                                hShell,
                                NativeMethods.HWND_TOPMOST,
                                0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

                            // 触发协调
                            tray.ReconcileTaskbarOverlayOrder(hShell);

                            // 断言层级关系：Menu > Watermark > Tint > Shell
                            TrayIconManager.ZOrderRelation relMenuWater = TrayIconManager.GetZOrderRelation(hMenu, hWater);
                            Assert(relMenuWater == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Round {0}: Menu must be strictly above Watermark in Z-order", round));

                            TrayIconManager.ZOrderRelation relWaterTint = TrayIconManager.GetZOrderRelation(hWater, hTint);
                            Assert(relWaterTint == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Round {0}: Watermark must be strictly above Tint in Z-order", round));

                            TrayIconManager.ZOrderRelation relTintShell = TrayIconManager.GetZOrderRelation(hTint, hShell);
                            Assert(relTintShell == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Round {0}: Tint must be strictly above Shell in Z-order", round));
                        }
                        else
                        {
                            if (menu.Visible)
                            {
                                menu.Close();
                                Application.DoEvents();
                            }

                            Assert(!menu.Visible, string.Format("Round {0}: ContextMenu must be closed", round));

                            // 模拟 Windows Shell 抢顶
                            NativeMethods.SetWindowPos(
                                hShell,
                                NativeMethods.HWND_TOPMOST,
                                0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

                            // 触发协调
                            tray.ReconcileTaskbarOverlayOrder(hShell);

                            // 断言层级关系：Watermark > Tint > Shell
                            TrayIconManager.ZOrderRelation relWaterTint = TrayIconManager.GetZOrderRelation(hWater, hTint);
                            Assert(relWaterTint == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Round {0}: Watermark must be strictly above Tint in Z-order (menu closed)", round));

                            TrayIconManager.ZOrderRelation relTintShell = TrayIconManager.GetZOrderRelation(hTint, hShell);
                            Assert(relTintShell == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Round {0}: Tint must be strictly above Shell in Z-order (menu closed)", round));
                        }

                        // Native GetWindowRect 尺寸与位置稳定性验证（不用 WinForms Bounds 缓存掩盖 ULW）
                        NativeMethods.RECT rcWaterCurrent;
                        Assert(NativeMethods.GetWindowRect(hWater, out rcWaterCurrent), "GetWindowRect on watermark must succeed");
                        Assert(rcWaterCurrent.Left == rcWaterInitial.Left &&
                               rcWaterCurrent.Top == rcWaterInitial.Top &&
                               rcWaterCurrent.Right == rcWaterInitial.Right &&
                               rcWaterCurrent.Bottom == rcWaterInitial.Bottom,
                            string.Format("Round {0}: Watermark native GetWindowRect bounds must remain stable ({1},{2},{3},{4}) vs ({5},{6},{7},{8})",
                                round, rcWaterCurrent.Left, rcWaterCurrent.Top, rcWaterCurrent.Right, rcWaterCurrent.Bottom,
                                rcWaterInitial.Left, rcWaterInitial.Top, rcWaterInitial.Right, rcWaterInitial.Bottom));

                        NativeMethods.RECT rcTintCurrent;
                        Assert(NativeMethods.GetWindowRect(hTint, out rcTintCurrent), "GetWindowRect on tint must succeed");
                        Assert(rcTintCurrent.Left == rcTintInitial.Left &&
                               rcTintCurrent.Top == rcTintInitial.Top &&
                               rcTintCurrent.Right == rcTintInitial.Right &&
                               rcTintCurrent.Bottom == rcTintInitial.Bottom,
                            string.Format("Round {0}: Tint native GetWindowRect bounds must remain stable", round));

                        // 断言 actual > 1 尺寸保持有效（文字/色层确实有图）
                        Assert((rcWaterCurrent.Right - rcWaterCurrent.Left) > 1 && (rcWaterCurrent.Bottom - rcWaterCurrent.Top) > 1,
                            string.Format("Round {0}: Watermark native dimensions must remain > 1", round));
                        Assert((rcTintCurrent.Right - rcTintCurrent.Left) > 1 && (rcTintCurrent.Bottom - rcTintCurrent.Top) > 1,
                            string.Format("Round {0}: Tint native dimensions must remain > 1", round));

                        // 断言 alpha 稳定
                        Assert(watermark.LastCalculatedAlpha == initialWaterAlpha,
                            string.Format("Round {0}: Watermark alpha must remain stable", round));
                    }
                }
            }
        }

        private static void TestAlreadyCorrectNoChange()
        {
            AppConfig config = new AppConfig();
            config.TaskbarTintEnabled = true;
            config.WatermarkEnabled = true;
            config.WatermarkPosition = "Custom";

            Rectangle fakeShellRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle fakeTrayRect = new Rectangle(1500, 1000, 420, 48);
            int fakeMaxAppRight = 500;

            using (FakeContentionSpaceProvider spaceProvider = new FakeContentionSpaceProvider(fakeShellRect, fakeTrayRect, fakeMaxAppRight))
            using (TopmostSentinelForm sentinel = new TopmostSentinelForm())
            using (SyntheticShellForm shell = new SyntheticShellForm())
            using (WatermarkWindow watermark = new WatermarkWindow())
            using (TaskbarTintOverlay tint = new TaskbarTintOverlay(config, 1, () => shell.Bounds))
            {
                watermark.SetTaskbarSpaceProvider(spaceProvider);

                sentinel.Show();
                shell.Show();
                tint.Show();
                tint.RenderTintOverlay();

                watermark.HasTaskbarIntent = true;
                watermark.Show();
                watermark.SetDesktop(1, 1, "Desk1");
                watermark.UpdateWatermark();
                Application.DoEvents();

                using (TrayIconManager tray = new TrayIconManager(config, watermark, null, tint))
                {
                    ContextMenuStrip menu = typeof(TrayIconManager)
                        .GetField("_contextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(tray) as ContextMenuStrip;
                    Assert(menu != null, "ContextMenuStrip must exist");

                    menu.Show(shell, new Point(200, -100));
                    Application.DoEvents();

                    IntPtr hSentinel = sentinel.Handle;
                    IntPtr hMenu = menu.Handle;
                    IntPtr hWater = watermark.Handle;
                    IntPtr hTint = tint.Handle;
                    IntPtr hShell = shell.Handle;

                    // 1. 先进行一次协调，建立标准层级：Menu > Watermark > Tint > Shell
                    tray.ReconcileTaskbarOverlayOrder(hShell);

                    // 2. 将 sentinel 显式推到最顶端，使其保持在 Menu 之上（Sentinel > Menu > Watermark > Tint > Shell）
                    NativeMethods.SetWindowPos(
                        hSentinel,
                        NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

                    // 确认基线关系：Sentinel 在 Menu 之上，且 Menu > Watermark > Tint > Shell
                    Assert(TrayIconManager.GetZOrderRelation(hSentinel, hMenu) == TrayIconManager.ZOrderRelation.Above,
                        "Sentinel must be above Menu initially");
                    Assert(TrayIconManager.GetZOrderRelation(hMenu, hWater) == TrayIconManager.ZOrderRelation.Above,
                        "Menu must be above Watermark initially");
                    Assert(TrayIconManager.GetZOrderRelation(hWater, hTint) == TrayIconManager.ZOrderRelation.Above,
                        "Watermark must be above Tint initially");
                    Assert(TrayIconManager.GetZOrderRelation(hTint, hShell) == TrayIconManager.ZOrderRelation.Above,
                        "Tint must be above Shell initially");

                    // 3. 用窗口消息 tracker 钩住各图层，用窗口消息计数检验不重复 position
                    WindowPosMsgTracker trackerWater = new WindowPosMsgTracker();
                    WindowPosMsgTracker trackerTint = new WindowPosMsgTracker();
                    WindowPosMsgTracker trackerMenu = new WindowPosMsgTracker();

                    try
                    {
                        trackerWater.AssignHandle(hWater);
                        trackerTint.AssignHandle(hTint);
                        trackerMenu.AssignHandle(hMenu);

                        trackerWater.WindowPosChangingCount = 0;
                        trackerTint.WindowPosChangingCount = 0;
                        trackerMenu.WindowPosChangingCount = 0;

                        // 连续调用 5 轮协调：层级已正确，协调器绝不重复 SetWindowPos，绝不抢超哨兵
                        for (int i = 0; i < 5; i++)
                        {
                            tray.ReconcileTaskbarOverlayOrder(hShell);

                            // 验证协调器绝不抢超哨兵
                            Assert(TrayIconManager.GetZOrderRelation(hSentinel, hMenu) == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Iteration {0}: Reconcile must NEVER steal topmost above Sentinel (Sentinel > Menu)", i));
                            Assert(TrayIconManager.GetZOrderRelation(hSentinel, hWater) == TrayIconManager.ZOrderRelation.Above,
                                string.Format("Iteration {0}: Reconcile must NEVER steal topmost above Sentinel (Sentinel > Watermark)", i));

                            // 验证相对序依然正确
                            Assert(TrayIconManager.GetZOrderRelation(hMenu, hWater) == TrayIconManager.ZOrderRelation.Above,
                                "Menu must remain above Watermark");
                            Assert(TrayIconManager.GetZOrderRelation(hWater, hTint) == TrayIconManager.ZOrderRelation.Above,
                                "Watermark must remain above Tint");
                            Assert(TrayIconManager.GetZOrderRelation(hTint, hShell) == TrayIconManager.ZOrderRelation.Above,
                                "Tint must remain above Shell");
                        }

                        Application.DoEvents();

                        // 检验窗口消息计数：因为层级完全正确，不得调用 SetWindowPos 产生任何 WM_WINDOWPOSCHANGING
                        Assert(trackerWater.WindowPosChangingCount == 0,
                            string.Format("Watermark must not receive WM_WINDOWPOSCHANGING when already correct, got {0}", trackerWater.WindowPosChangingCount));
                        Assert(trackerTint.WindowPosChangingCount == 0,
                            string.Format("Tint must not receive WM_WINDOWPOSCHANGING when already correct, got {0}", trackerTint.WindowPosChangingCount));
                        Assert(trackerMenu.WindowPosChangingCount == 0,
                            string.Format("Menu must not receive WM_WINDOWPOSCHANGING when already correct, got {0}", trackerMenu.WindowPosChangingCount));
                    }
                    finally
                    {
                        trackerWater.ReleaseHandle();
                        trackerTint.ReleaseHandle();
                        trackerMenu.ReleaseHandle();
                        menu.Close();
                    }
                }
            }
        }
    }
}
