using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class MockRecoverySpaceProvider : ITaskbarSpaceProvider
    {
        private TaskbarSpaceSnapshot _snapshot;
        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

        public MockRecoverySpaceProvider(TaskbarSpaceSnapshot initialSnapshot = null)
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

    public class TaskbarRecoveryAnchorTests
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

        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Recovery Anchor Stability Tests");
            Console.WriteLine("==================================================");

            string desktopName = GetCurrentDesktopName();
            bool isIsolated = (!string.IsNullOrEmpty(desktopName) && desktopName.StartsWith("WindicatorTest-", StringComparison.OrdinalIgnoreCase));

            if (!isIsolated)
            {
                Console.WriteLine("[SAFETY GUARD] TaskbarRecoveryAnchorTests requires an isolated desktop (prefix 'WindicatorTest-') to avoid disturbing user foreground window. Current desktop: " + desktopName);
                Console.WriteLine("[SAFETY GUARD] Execution rejected on default/non-test desktop. Worker compiled tests successfully.");
                return 2;
            }

            try
            {
                RunAllTests();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("TEST FAILURE: " + (ex != null ? ex.Message : "Unknown error"));
                Console.WriteLine(ex != null ? ex.StackTrace : "");
                Console.ResetColor();
                return 1;
            }
        }

        public static void RunAllTests()
        {
            TestValidInvalidValidTrayExpand40pxPreservesAnchorZeroJump();
            Console.WriteLine("PASS: TestValidInvalidValidTrayExpand40pxPreservesAnchorZeroJump (0px drift on recovery with 40px expanded tray)");

            TestStaleSnapshotClearsAndRecoversAnchor();
            Console.WriteLine("PASS: TestStaleSnapshotClearsAndRecoversAnchor (Conservative clear on stale, safe restore on fresh)");

            TestUnsafeAppCollisionRejectsAnchorAndDegradesSafely();
            Console.WriteLine("PASS: TestUnsafeAppCollisionRejectsAnchorAndDegradesSafely (Safety gap invasion rejects stale anchor and degrades gracefully)");

            TestNativeTaskbarChangeRejectsAnchorAndInvalidates();
            Console.WriteLine("PASS: TestNativeTaskbarChangeRejectsAnchorAndInvalidates (Native taskbar geometry change immediately invalidates anchor)");

            TestDisabledAndDesktopModeRoundtripDoesNotLeakAnchor();
            Console.WriteLine("PASS: TestDisabledAndDesktopModeRoundtripDoesNotLeakAnchor (Disabled toggle and desktop mode roundtrip do not leak anchor)");

            TestRecoveredRenderProducesValidBitmapAndMatchingGeometry();
            Console.WriteLine("PASS: TestRecoveredRenderProducesValidBitmapAndMatchingGeometry (ULW rendered bitmap and physical window geometry strictly consistent)");

            TestValidDisabledNativeTrayExpandReenabledRecalculatesZeroLeak();
            Console.WriteLine("PASS: TestValidDisabledNativeTrayExpandReenabledRecalculatesZeroLeak (Disabled toggle with tray expansion recalculates without stale anchor reuse)");

            TestDragAndHandleDestroyedInvalidatesAnchorAndDoesNotReuse();
            Console.WriteLine("PASS: TestDragAndHandleDestroyedInvalidatesAnchorAndDoesNotReuse (Drag and handle destruction strictly invalidate anchor and reject stale reuse)");

            Console.WriteLine("==================================================");
            Console.WriteLine("ALL TASKBAR RECOVERY ANCHOR TESTS PASSED (8/8)");
            Console.WriteLine("==================================================");
        }

        /// <summary>
        /// 核心测试 1:
        /// 初始 valid snapshot (tray.Left=1680) -> Invalid snapshot 清空隐藏 -> 恢复 valid snapshot (tray.Left=1720，即右扩 40px)。
        /// 旧版代码中由于 Invalid 清空了 _actualScreenRect 导致丢失锚点，恢复时横移 40px (pos.X 增加 40)；
        /// 新版代码中利用 ULW 成功记录的独立最近成功停靠锚点，经 fresh snapshot 重新证明后完全复用原锚点，实现 0px 横移！
        /// </summary>
        public static void TestValidInvalidValidTrayExpand40pxPreservesAnchorZeroJump()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRectInitial = new Rectangle(1680, 1000, 240, 48);
            int maxAppRight = 500;

            TaskbarSpaceSnapshot validInitialSnap = new TaskbarSpaceSnapshot(maxAppRight, trRectInitial.Left, tbRect, trRectInitial, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(validInitialSnap);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "Desktop1");
                watermark.CreateControl();
                watermark.Show();

                // 阶段 1: 初始有效停靠
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle posInitial = watermark.ActualScreenRect;
                Bitmap bmpInitial = watermark.LastRenderedBitmapForTest;
                Assert(!posInitial.IsEmpty, "Initial render must produce valid docked screen rect");
                Assert(bmpInitial != null, "Initial render must produce rendered bitmap");

                // 阶段 2: 变为 Invalid snapshot (例如 UIA 探测瞬断/不可信)
                TaskbarSpaceSnapshot invalidSnap = new TaskbarSpaceSnapshot(maxAppRight, 1680, Rectangle.Empty, Rectangle.Empty, false, Stopwatch.GetTimestamp());
                provider.SetSnapshot(invalidSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                // 契约不变量断言: Invalid 必须保守隐藏，实际可见几何必须为 Empty，绝不能拿历史假冒 actual！
                Assert(watermark.ActualScreenRect.IsEmpty, "ActualScreenRect must strictly be Empty during invalid snapshot");
                NativeMethods.RECT rcInvalid;
                Assert(NativeMethods.GetWindowRect(watermark.Handle, out rcInvalid), "GetWindowRect must succeed during invalid state");
                int invW = rcInvalid.Right - rcInvalid.Left;
                int invH = rcInvalid.Bottom - rcInvalid.Top;
                Assert(invW == 1 && invH == 1, string.Format("Invalid snapshot must shrink layered window to 1x1, got {0}x{1}", invW, invH));

                // 阶段 3: 恢复为 Valid snapshot，但托盘向右扩大 40px (例如 chevron 折叠或隐藏图标菜单关闭，trRect.Left 从 1680 变为 1720)
                Rectangle trRectExpanded = new Rectangle(1720, 1000, 200, 48);
                TaskbarSpaceSnapshot recoveredSnap = new TaskbarSpaceSnapshot(maxAppRight, trRectExpanded.Left, tbRect, trRectExpanded, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(recoveredSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle posRecovered = watermark.ActualScreenRect;
                Bitmap bmpRecovered = watermark.LastRenderedBitmapForTest;

                Assert(!posRecovered.IsEmpty, "Recovered watermark must restore non-empty actual screen rect");
                Assert(bmpRecovered != null, "Recovered watermark must produce non-null rendered bitmap");

                // 核心断言: 水印 X 坐标绝不能横移！必须精确等于 posInitial.X
                // 旧版代码未保留独立锚点，此时会横移 40px (posRecovered.X == posInitial.X + 40)
                int deltaX = posRecovered.X - posInitial.X;
                Assert(deltaX == 0, string.Format("Watermark X position must NOT drift after Valid->Invalid->Valid recovery! Expected: {0}, Actual: {1} (Drift: {2}px)",
                    posInitial.X, posRecovered.X, deltaX));
                Assert(posRecovered.Y == posInitial.Y, string.Format("Watermark Y position must match. Expected: {0}, Actual: {1}", posInitial.Y, posRecovered.Y));
                Assert(posRecovered.Width == posInitial.Width, "Watermark Width must match initial width");
                Assert(posRecovered.Height == posInitial.Height, "Watermark Height must match initial height");
            }
        }

        /// <summary>
        /// 测试 2: 过期 snapshot (stale, 超过 2000ms) 保守清空，恢复 fresh snapshot 后重新证明并恢复锚点
        /// </summary>
        public static void TestStaleSnapshotClearsAndRecoversAnchor()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRect = new Rectangle(1680, 1000, 240, 48);
            int maxAppRight = 500;

            TaskbarSpaceSnapshot freshSnap1 = new TaskbarSpaceSnapshot(maxAppRight, trRect.Left, tbRect, trRect, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(freshSnap1);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "Desktop1");
                watermark.CreateControl();
                watermark.Show();

                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle posInitial = watermark.ActualScreenRect;
                Assert(!posInitial.IsEmpty, "Initial snapshot must dock successfully");

                // 模拟过期快照: 时间戳早于当前 5 秒 (已过期)
                long staleTimestamp = Stopwatch.GetTimestamp() - (Stopwatch.Frequency * 5);
                TaskbarSpaceSnapshot staleSnap = new TaskbarSpaceSnapshot(maxAppRight, trRect.Left, tbRect, trRect, true, staleTimestamp);
                provider.SetSnapshot(staleSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                // 契约断言: 过期快照必须被保守清空隐藏，绝不能无条件给旧快照续命
                Assert(watermark.ActualScreenRect.IsEmpty, "ActualScreenRect must be empty when snapshot is stale");

                // 恢复为有效 fresh 快照 (trRect 向右扩展 20px)
                Rectangle trRect2 = new Rectangle(1700, 1000, 220, 48);
                TaskbarSpaceSnapshot freshSnap2 = new TaskbarSpaceSnapshot(maxAppRight, trRect2.Left, tbRect, trRect2, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(freshSnap2);
                watermark.UpdateWatermark();
                Application.DoEvents();

                // 契约断言: 恢复 fresh 快照后重新证明安全容纳并恢复锚点
                Assert(watermark.ActualScreenRect == posInitial,
                    string.Format("Fresh snapshot after stale must safely restore anchor. Expected: {0}, Got: {1}", posInitial, watermark.ActualScreenRect));
                Assert(watermark.LastRenderedBitmapForTest != null, "Recovered render must yield non-null bitmap");
            }
        }

        /// <summary>
        /// 测试 3: 应用按钮侵占 (Unsafe App Space Collision) 必须拒绝复用旧锚点，安全退化
        /// </summary>
        public static void TestUnsafeAppCollisionRejectsAnchorAndDegradesSafely()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRect = new Rectangle(1680, 1000, 240, 48);

            // 初始: maxAppRight = 500，停靠在 1476 左右
            TaskbarSpaceSnapshot initialSnap = new TaskbarSpaceSnapshot(500, trRect.Left, tbRect, trRect, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(initialSnap);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "DeskSafe");
                watermark.CreateControl();
                watermark.Show();

                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle posInitial = watermark.ActualScreenRect;
                Assert(!posInitial.IsEmpty, "Initial dock must succeed");

                // 经历 Invalid
                TaskbarSpaceSnapshot invalidSnap = new TaskbarSpaceSnapshot(500, trRect.Left, Rectangle.Empty, Rectangle.Empty, false, Stopwatch.GetTimestamp());
                provider.SetSnapshot(invalidSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();
                Assert(watermark.ActualScreenRect.IsEmpty, "Must be empty when invalid");

                // 恢复时，应用按钮大幅向右侵占至 1670 (紧贴托盘，任务栏内部已无足够空间容纳水印)
                TaskbarSpaceSnapshot crowdedSnap = new TaskbarSpaceSnapshot(1670, trRect.Left, tbRect, trRect, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(crowdedSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                // 契约断言: 安全守卫必须拒绝盲目复用旧锚点 (旧锚点 Left 约 1476，已被 1670 应用按钮严重遮挡)
                Assert(watermark.ActualScreenRect != posInitial, "Safety guard must reject stale anchor when app buttons invade safety gap");
                // 此时水平空间不足，必须安全退化为 floating 悬浮模式，不可覆盖应用按钮或消失
                Assert(watermark.IsTaskbarOverflowFloating, "Must safely degrade to overflow floating when taskbar space is crowded");
                Assert(!watermark.ActualScreenRect.IsEmpty, "Floating fallback must maintain active visible rect");
            }
        }

        /// <summary>
        /// 测试 4: Native taskbar 变更 (屏幕/任务栏高度或坐标改变) 必须立即拒绝旧锚点并 Invalidate
        /// </summary>
        public static void TestNativeTaskbarChangeRejectsAnchorAndInvalidates()
        {
            Rectangle tbRectBottom = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRectBottom = new Rectangle(1680, 1000, 240, 48);

            TaskbarSpaceSnapshot snapBottom = new TaskbarSpaceSnapshot(500, trRectBottom.Left, tbRectBottom, trRectBottom, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(snapBottom);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "DeskBottom");
                watermark.CreateControl();
                watermark.Show();

                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle posBottom = watermark.ActualScreenRect;
                Assert(posBottom.Top >= 1000, "Initial position must be on bottom taskbar");

                // 变为 Invalid
                provider.SetSnapshot(TaskbarSpaceSnapshot.Invalid());
                watermark.UpdateWatermark();
                Application.DoEvents();
                Assert(watermark.ActualScreenRect.IsEmpty, "ActualScreenRect must be empty during invalid");

                // 恢复时，任务栏变成了屏幕顶部任务栏 (Top=0, Bottom=48)
                Rectangle tbRectTop = new Rectangle(0, 0, 1920, 48);
                Rectangle trRectTop = new Rectangle(1680, 0, 240, 48);
                TaskbarSpaceSnapshot snapTop = new TaskbarSpaceSnapshot(500, trRectTop.Left, tbRectTop, trRectTop, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(snapTop);
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle posTop = watermark.ActualScreenRect;
                Assert(!posTop.IsEmpty, "Watermark must be docked on new top taskbar");
                // 契约断言: Native taskbar 改变必须拒绝旧锚点，新位置必须在顶部任务栏内，绝对不能残留在 1000+ 的旧位置
                Assert(posTop.Top >= 0 && posTop.Bottom <= 48,
                    string.Format("Watermark must adapt to new taskbar rect. Expected Top between 0 and 48, got Top: {0}, Bottom: {1}", posTop.Top, posTop.Bottom));
                Assert(posTop.Y != posBottom.Y, "Watermark must NOT retain stale Y coordinate from previous taskbar");
            }
        }

        /// <summary>
        /// 测试 5: 禁用 (WatermarkEnabled=false) 与桌面模式 Roundtrip 必须彻底清理锚点，不跨模式泄漏
        /// </summary>
        public static void TestDisabledAndDesktopModeRoundtripDoesNotLeakAnchor()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRect = new Rectangle(1680, 1000, 240, 48);

            TaskbarSpaceSnapshot snap = new TaskbarSpaceSnapshot(500, trRect.Left, tbRect, trRect, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(snap);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "DeskClean");
                watermark.CreateControl();
                watermark.Show();

                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle initialPos = watermark.ActualScreenRect;
                Assert(!initialPos.IsEmpty, "Initial dock must succeed");

                // 1. 禁用水印
                watermark.SetWatermarkEnabled(false);
                Application.DoEvents();
                Assert(!watermark.Visible, "Watermark must be hidden when disabled");

                // 2. 重新启用
                watermark.SetWatermarkEnabled(true);
                Application.DoEvents();
                Assert(watermark.Visible, "Watermark must be visible when re-enabled");

                // 3. 切换为桌面模式 (HasTaskbarIntent = false)
                watermark.HasTaskbarIntent = false;
                watermark.UpdateWatermark();
                Application.DoEvents();

                // 4. 切回任务栏模式，且此时任务栏托盘位置改变
                Rectangle trRectNew = new Rectangle(1600, 1000, 320, 48);
                TaskbarSpaceSnapshot snapNew = new TaskbarSpaceSnapshot(500, trRectNew.Left, tbRect, trRectNew, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(snapNew);

                watermark.HasTaskbarIntent = true;
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle finalPos = watermark.ActualScreenRect;
                Assert(!finalPos.IsEmpty, "Final dock must succeed");
                // 契约断言: 桌面模式离开时已清理 anchor，重新进入任务栏模式后基于新托盘 (1600) 定位，不得泄漏旧锚点 (1680)
                Assert(finalPos.Right <= 1600,
                    string.Format("Watermark right edge must dock to new tray left 1600 without leaking previous anchor. Got Right: {0}", finalPos.Right));
            }
        }

        /// <summary>
        /// 测试 6: 恢复渲染生成有效位图，且实际 ActualScreenRect 与物理底层窗口矩形严格一致
        /// </summary>
        public static void TestRecoveredRenderProducesValidBitmapAndMatchingGeometry()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRect = new Rectangle(1680, 1000, 240, 48);

            TaskbarSpaceSnapshot validSnap = new TaskbarSpaceSnapshot(500, trRect.Left, tbRect, trRect, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(validSnap);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "DeskGeometry");
                watermark.CreateControl();
                watermark.Show();

                // Valid -> Invalid -> Valid 恢复
                watermark.UpdateWatermark();
                Application.DoEvents();

                provider.SetSnapshot(TaskbarSpaceSnapshot.Invalid());
                watermark.UpdateWatermark();
                Application.DoEvents();

                provider.SetSnapshot(validSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle actual = watermark.ActualScreenRect;
                Bitmap bmp = watermark.LastRenderedBitmapForTest;

                Assert(!actual.IsEmpty && actual.Width > 1 && actual.Height > 1,
                    string.Format("ActualScreenRect must be non-empty and greater than 1x1, got: {0}", actual));
                Assert(bmp != null, "LastRenderedBitmapForTest must be non-null after recovery");
                Assert(bmp.Width == actual.Width && bmp.Height == actual.Height,
                    string.Format("Bitmap dimensions ({0}x{1}) must match ActualScreenRect dimensions ({2}x{3})",
                        bmp.Width, bmp.Height, actual.Width, actual.Height));

                NativeMethods.RECT winRect;
                Assert(NativeMethods.GetWindowRect(watermark.Handle, out winRect), "GetWindowRect must succeed");
                int winW = winRect.Right - winRect.Left;
                int winH = winRect.Bottom - winRect.Top;

                Assert(winW == actual.Width && winH == actual.Height,
                    string.Format("Physical window size ({0}x{1}) must strictly match ActualScreenRect ({2}x{3})",
                        winW, winH, actual.Width, actual.Height));
                Assert(winRect.Left == actual.Left && winRect.Top == actual.Top,
                    string.Format("Physical window position ({0},{1}) must match ActualScreenRect position ({2},{3})",
                        winRect.Left, winRect.Top, actual.Left, actual.Top));
            }
        }

        /// <summary>
        /// 测试 7 (返修增强 1):
        /// valid -> 禁用 -> nativeTray右扩 -> 启用（中间不经 invalid snapshot）
        /// 验证禁用状态下调用 InvalidateDockedAnchor，无论是否经历 invalid，重新启用时绝不能把旧锚点复用回来，
        /// 必须根据新托盘右扩后的几何重新计算 X 坐标并正确定位。
        /// </summary>
        public static void TestValidDisabledNativeTrayExpandReenabledRecalculatesZeroLeak()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRectInitial = new Rectangle(1680, 1000, 240, 48);
            int maxAppRight = 500;

            TaskbarSpaceSnapshot initialSnap = new TaskbarSpaceSnapshot(maxAppRight, trRectInitial.Left, tbRect, trRectInitial, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(initialSnap);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "DeskDisableExpand");
                watermark.CreateControl();
                watermark.Show();

                // 初始有效停靠
                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle posInitial = watermark.ActualScreenRect;
                Assert(!posInitial.IsEmpty, "Initial dock must succeed");

                // 禁用（不经过 invalid snapshot，直接禁用）
                watermark.SetWatermarkEnabled(false);
                Application.DoEvents();
                Assert(!watermark.Visible, "Watermark must be hidden when disabled");

                // 在禁用期间，nativeTray 发生右扩 40px（trRect.Left 从 1680 变为 1720），快照仍保持 valid
                Rectangle trRectExpanded = new Rectangle(1720, 1000, 200, 48);
                TaskbarSpaceSnapshot expandedSnap = new TaskbarSpaceSnapshot(maxAppRight, trRectExpanded.Left, tbRect, trRectExpanded, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(expandedSnap);

                // 重新启用
                watermark.SetWatermarkEnabled(true);
                Application.DoEvents();
                Rectangle posReenabled = watermark.ActualScreenRect;

                Assert(watermark.Visible, "Watermark must be visible after re-enabling");
                Assert(!posReenabled.IsEmpty, "Watermark actual screen rect must be non-empty after re-enabling");

                // 契约断言：
                // 禁用操作已经显式 InvalidateDockedAnchor；启用时绝不能复用禁午前旧锚点！
                // 必须基于新托盘 (Left=1720) 重新计算，即 X 坐标必须向右平移 40px！
                Assert(posReenabled.X != posInitial.X,
                    string.Format("Re-enabled watermark must NOT reuse stale anchor from before disabled! posInitial.X={0}, posReenabled.X={1}", posInitial.X, posReenabled.X));
                Assert(posReenabled.X == posInitial.X + 40,
                    string.Format("Re-enabled watermark must adapt to 40px expanded tray! Expected X={0}, Actual X={1}", posInitial.X + 40, posReenabled.X));
            }
        }

        /// <summary>
        /// 测试 8 (返修增强 2):
        /// drag/handle 失效不复用；用 reflection 触发受保护可控回调，绝不能触发真实键盘鼠标物理输入
        /// 验证手动拖拽与 handle 销毁后停靠锚点被严格清空，后续托盘变化必须重新计算定位，绝不泄漏旧锚点。
        /// </summary>
        public static void TestDragAndHandleDestroyedInvalidatesAnchorAndDoesNotReuse()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRectInitial = new Rectangle(1680, 1000, 240, 48);
            int maxAppRight = 500;

            TaskbarSpaceSnapshot initialSnap = new TaskbarSpaceSnapshot(maxAppRight, trRectInitial.Left, tbRect, trRectInitial, true, Stopwatch.GetTimestamp());
            MockRecoverySpaceProvider provider = new MockRecoverySpaceProvider(initialSnap);

            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(provider);
                watermark.SetDesktop(1, 1, "DeskDragHandle");
                watermark.CreateControl();
                watermark.Show();

                // 初始有效停靠
                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle posInitial = watermark.ActualScreenRect;
                Assert(!posInitial.IsEmpty, "Initial dock must succeed");

                // --- 阶段 A: 模拟手动拖拽失效 ---
                // 利用 reflection 触发可控回调与内部拖拽状态，不产生任何真实操作系统输入
                FieldInfo isDraggingField = typeof(WatermarkWindow).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(isDraggingField != null, "_isDragging field must exist");
                isDraggingField.SetValue(watermark, true);

                MethodInfo onMouseUpMethod = typeof(WatermarkWindow).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(onMouseUpMethod != null, "OnMouseUp method must exist");
                // 触发拖拽释放回调，这将清除 _isDragging 并执行 InvalidateDockedAnchor
                onMouseUpMethod.Invoke(watermark, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
                Application.DoEvents();

                // 此时 nativeTray 发生右扩 40px
                Rectangle trRectExpanded = new Rectangle(1720, 1000, 200, 48);
                TaskbarSpaceSnapshot expandedSnap = new TaskbarSpaceSnapshot(maxAppRight, trRectExpanded.Left, tbRect, trRectExpanded, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(expandedSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle posAfterDrag = watermark.ActualScreenRect;
                Assert(!posAfterDrag.IsEmpty, "Actual rect must be valid after drag release and update");
                Assert(posAfterDrag.X != posInitial.X, "Watermark must NOT reuse stale anchor after drag release!");
                Assert(posAfterDrag.X == posInitial.X + 40,
                    string.Format("Watermark after drag must adapt to expanded tray! Expected X={0}, Actual X={1}", posInitial.X + 40, posAfterDrag.X));

                // --- 阶段 B: 模拟 Handle 销毁失效 ---
                // 恢复到初始托盘位置重新成功停靠并生成锚点
                provider.SetSnapshot(initialSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();
                Rectangle posReset = watermark.ActualScreenRect;
                Assert(posReset.X == posInitial.X, "Reset to initial snapshot must dock to initial X");

                // 利用 reflection 调用受保护的 OnHandleDestroyed 生命周期方法
                MethodInfo onHandleDestroyedMethod = typeof(WatermarkWindow).GetMethod("OnHandleDestroyed", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(onHandleDestroyedMethod != null, "OnHandleDestroyed method must exist");
                onHandleDestroyedMethod.Invoke(watermark, new object[] { EventArgs.Empty });

                // 托盘再次右扩 40px
                provider.SetSnapshot(expandedSnap);
                watermark.UpdateWatermark();
                Application.DoEvents();

                Rectangle posAfterHandleDestroyed = watermark.ActualScreenRect;
                Assert(!posAfterHandleDestroyed.IsEmpty, "Actual rect must be valid after handle destroyed update");
                Assert(posAfterHandleDestroyed.X != posReset.X, "Watermark must NOT reuse stale anchor after handle destruction!");
                Assert(posAfterHandleDestroyed.X == posInitial.X + 40,
                    string.Format("Watermark after handle destruction must adapt to expanded tray! Expected X={0}, Actual X={1}", posInitial.X + 40, posAfterHandleDestroyed.X));
            }
        }
    }
}
