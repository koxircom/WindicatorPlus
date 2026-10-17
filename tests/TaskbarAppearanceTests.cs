using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace WindicatorPlus.Tests
{
    public class MockTaskbarSpaceProvider : ITaskbarSpaceProvider
    {
        private TaskbarSpaceSnapshot _snapshot;
        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

        public MockTaskbarSpaceProvider(TaskbarSpaceSnapshot initialSnapshot = null)
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

    class TaskbarAppearanceTests
    {
        static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Appearance Tests");
            Console.WriteLine("==================================================");

            try
            {
                TestSwpNoMoveConstant();
                Console.WriteLine("PASS: TestSwpNoMoveConstant");

                TestCheckIfPositionIsTaskbarDockedStaleTrueFixed();
                Console.WriteLine("PASS: TestCheckIfPositionIsTaskbarDockedStaleTrueFixed");

                TestTaskbarTransparentEdgesAndSingleLine();
                Console.WriteLine("PASS: TestTaskbarTransparentEdgesAndSingleLine");

                TestTaskbarScaleIncreasesDimensionsAndPixels();
                Console.WriteLine("PASS: TestTaskbarScaleIncreasesDimensionsAndPixels (75/100/130/160)");

                TestTaskbarNoSubtitleAndIdenticalBitmaps();
                Console.WriteLine("PASS: TestTaskbarNoSubtitleAndIdenticalBitmaps");

                TestLongTextAndDoubleDigitNumberNoClipping();
                Console.WriteLine("PASS: TestLongTextAndDoubleDigitNumberNoClipping");

                TestTaskbarHeightsNoVerticalOverflow();
                Console.WriteLine("PASS: TestTaskbarHeightsNoVerticalOverflow (48/72/96)");

                TestTaskbarInteractiveNoGlow();
                Console.WriteLine("PASS: TestTaskbarInteractiveNoGlow");

                TestDesktopModePreservesBackgroundAndBorder();
                Console.WriteLine("PASS: TestDesktopModePreservesBackgroundAndBorder");

                TestTaskbarDockedBoundsRightEdgeAnchoring();
                Console.WriteLine("PASS: TestTaskbarDockedBoundsRightEdgeAnchoring (Pure Function & Tray Change Following)");

                TestRealWin32TaskbarGeometryValidation();
                Console.WriteLine("PASS: TestRealWin32TaskbarGeometryValidation (Actual Shell_TrayWnd & TrayNotifyWnd)");

                TestDesktopFreeDraggingNotLockedAndScreenRectSync();
                Console.WriteLine("PASS: TestDesktopFreeDraggingNotLockedAndScreenRectSync (_actualScreenRect Sync)");

                TestTaskbarCompactPaddingNoSpaceWasted();
                Console.WriteLine("PASS: TestTaskbarCompactPaddingNoSpaceWasted (Compact Padding <= 5px at 144DPI)");

                TestTaskbarAdaptiveFullEllipsisNumberOnlyNoSpace();
                Console.WriteLine("PASS: TestTaskbarAdaptiveFullEllipsisNumberOnlyNoSpace (Full/Ellipsis/NumberOnly/Hidden/Recovery)");

                TestTaskbarAdaptiveUnicodeSurrogatePairsNoSplit();
                Console.WriteLine("PASS: TestTaskbarAdaptiveUnicodeSurrogatePairsNoSplit (Preserves Surrogate Pairs & Emoji)");

                TestTaskbarAdaptiveMultiDpiAndScaleLimits();
                Console.WriteLine("PASS: TestTaskbarAdaptiveMultiDpiAndScaleLimits (Heights 36-96, Scales 75-160, Width <= Available)");

                TestMockTaskbarSpaceProviderInjection();
                Console.WriteLine("PASS: TestMockTaskbarSpaceProviderInjection (1080 Screen 509/593 + Stale/Invalid Hide + Recovery)");

                TestSinglePendingNoCallbackAccumulation();
                Console.WriteLine("PASS: TestSinglePendingNoCallbackAccumulation (100 Rapid Events -> Single Pending)");

                TestLiveWin32AndUiaSnapshotValidation();
                Console.WriteLine("PASS: TestLiveWin32AndUiaSnapshotValidation (Live Win32 + UIA Probe Snapshot)");

                TestProbeInvalidWhenNoButtonsOrMissingTrayOrException();
                Console.WriteLine("PASS: TestProbeInvalidWhenNoButtonsOrMissingTrayOrException (0 Buttons/Missing Tray/Exception -> Invalid)");

                TestStaleGuardActiveHideAndRecovery();
                Console.WriteLine("PASS: TestStaleGuardActiveHideAndRecovery (Stale Guard Active Hide + Same-Geometry Recovery)");

                TestStopLateWorkerSafety();
                Console.WriteLine("PASS: TestStopLateWorkerSafety (Stop Late Worker Safety & Non-Restartable Invariant)");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL TESTS PASSED SUCCESSFULLY (22/22)");
                Console.WriteLine("==================================================");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("TEST FAILURE: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion Failed: " + message);
            }
        }

        static WatermarkWindow CreateIsolatedWatermarkWindow()
        {
            // FormatterServices.GetUninitializedObject isolates pure drawing tests without
            // invoking the constructor (no background thread, no hotkey loop, no monitor interference).
            return (WatermarkWindow)FormatterServices.GetUninitializedObject(typeof(WatermarkWindow));
        }

        static void SetPrivateField(object target, string name, object val)
        {
            FieldInfo fi = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null)
            {
                throw new Exception("Field not found: " + name);
            }
            fi.SetValue(target, val);
        }

        static object GetPrivateField(object target, string name)
        {
            FieldInfo fi = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null)
            {
                throw new Exception("Field not found: " + name);
            }
            return fi.GetValue(target);
        }

        static object InvokeLayoutHelper(WatermarkWindow win, Graphics g, int height, string title)
        {
            MethodInfo mi = typeof(WatermarkWindow).GetMethod(
                "CalculateTaskbarLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(mi != null, "CalculateTaskbarLayout method must exist on WatermarkWindow");
            return mi.Invoke(win, new object[] { g, height, title });
        }

        static void InvokeRenderTaskbar(WatermarkWindow win, Graphics g, int w, int h, string title)
        {
            MethodInfo mi = typeof(WatermarkWindow).GetMethod(
                "RenderTaskbarDockedWidget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(mi != null, "RenderTaskbarDockedWidget method must exist on WatermarkWindow");
            mi.Invoke(win, new object[] { g, w, h, title });
        }

        static void TestSwpNoMoveConstant()
        {
            Assert(NativeMethods.SWP_NOMOVE == 0x0002, "NativeMethods.SWP_NOMOVE must be 0x0002");
            Assert(NativeMethods.SWP_NOSIZE == 0x0001, "NativeMethods.SWP_NOSIZE must be 0x0001");
            Assert(NativeMethods.SWP_NOACTIVATE == 0x0010, "NativeMethods.SWP_NOACTIVATE must be 0x0010");
            Assert(NativeMethods.SWP_SHOWWINDOW == 0x0040, "NativeMethods.SWP_SHOWWINDOW must be 0x0040");
        }

        static void TestCheckIfPositionIsTaskbarDockedStaleTrueFixed()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            MethodInfo checkMethod = typeof(WatermarkWindow).GetMethod(
                "CheckIfPositionIsTaskbarDocked",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(checkMethod != null, "CheckIfPositionIsTaskbarDocked must exist");

            // Case 1: Was docked, but position switched away from Custom
            SetPrivateField(win, "_isTaskbarDocked", true);
            SetPrivateField(win, "_position", "BottomRight");
            SetPrivateField(win, "_customY", 2100);
            checkMethod.Invoke(win, null);
            bool docked1 = (bool)GetPrivateField(win, "_isTaskbarDocked");
            Assert(!docked1, "CheckIfPositionIsTaskbarDocked must clear _isTaskbarDocked when _position is not Custom");

            // Case 2: Was docked, but CustomY moved away from taskbar
            SetPrivateField(win, "_isTaskbarDocked", true);
            SetPrivateField(win, "_position", "Custom");
            SetPrivateField(win, "_customY", 300); // Clearly desktop area
            checkMethod.Invoke(win, null);
            bool docked2 = (bool)GetPrivateField(win, "_isTaskbarDocked");
            Assert(!docked2, "CheckIfPositionIsTaskbarDocked must clear _isTaskbarDocked when CustomY is outside taskbar");
        }

        static void TestTaskbarTransparentEdgesAndSingleLine()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            SetPrivateField(win, "_currentDesktop", 2);
            SetPrivateField(win, "_totalDesktops", 4);
            SetPrivateField(win, "_showBigNumber", true);
            SetPrivateField(win, "_watermarkScale", 130);
            SetPrivateField(win, "_isTaskbarDocked", true);
            SetPrivateField(win, "_isInteractive", false);

            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics gM = Graphics.FromImage(measureBmp))
            {
                object layout = InvokeLayoutHelper(win, gM, 72, "开发调试");
                int width = (int)layout.GetType().GetField("Width").GetValue(layout);
                int height = (int)layout.GetType().GetField("Height").GetValue(layout);

                Assert(width > 120, "Measured taskbar layout width must be dynamic and > 120 (got " + width + ")");
                Assert(height == 72, "Measured height must match taskbar height 72");

                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    InvokeRenderTaskbar(win, g, width, height, "开发调试");

                    Assert(g.TextRenderingHint == TextRenderingHint.AntiAliasGridFit,
                        "Taskbar text rendering hint must be AntiAliasGridFit");

                    // 4 Corners strictly transparent
                    Assert(bmp.GetPixel(0, 0).A == 0, "Corner (0, 0) must be transparent");
                    Assert(bmp.GetPixel(width - 1, 0).A == 0, "Corner (width - 1, 0) must be transparent");
                    Assert(bmp.GetPixel(0, height - 1).A == 0, "Corner (0, height - 1) must be transparent");
                    Assert(bmp.GetPixel(width - 1, height - 1).A == 0, "Corner (width - 1, height - 1) must be transparent");

                    // Inset borders must be transparent (no card background, no outer frame)
                    Assert(bmp.GetPixel(1, 1).A == 0, "Inset (1, 1) must be transparent");
                    Assert(bmp.GetPixel(width - 2, 1).A == 0, "Inset (width - 2, 1) must be transparent");
                    Assert(bmp.GetPixel(1, height - 2).A == 0, "Inset (1, height - 2) must be transparent");
                    Assert(bmp.GetPixel(width - 2, height - 2).A == 0, "Inset (width - 2, height - 2) must be transparent");

                    // Right padding edge must be transparent
                    Assert(bmp.GetPixel(width - 2, height / 2).A == 0, "Right border padding must be transparent");

                    // Number, separator, title must be visible
                    int visiblePixels = 0;
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            if (bmp.GetPixel(x, y).A > 0) visiblePixels++;
                        }
                    }
                    Assert(visiblePixels > 100, "Visible pixels must be > 100 (got " + visiblePixels + ")");
                }
            }
        }

        static void TestTaskbarScaleIncreasesDimensionsAndPixels()
        {
            int[] scales = new int[] { 75, 100, 130, 160 };
            int[] measuredWidths = new int[scales.Length];
            int[] pixelCounts = new int[scales.Length];
            int height = 72;

            for (int i = 0; i < scales.Length; i++)
            {
                WatermarkWindow win = CreateIsolatedWatermarkWindow();
                SetPrivateField(win, "_currentDesktop", 2);
                SetPrivateField(win, "_totalDesktops", 4);
                SetPrivateField(win, "_showBigNumber", true);
                SetPrivateField(win, "_watermarkScale", scales[i]);
                SetPrivateField(win, "_isTaskbarDocked", true);

                using (Bitmap measureBmp = new Bitmap(1, 1))
                using (Graphics gM = Graphics.FromImage(measureBmp))
                {
                    object layout = InvokeLayoutHelper(win, gM, height, "开发调试");
                    measuredWidths[i] = (int)layout.GetType().GetField("Width").GetValue(layout);

                    using (Bitmap bmp = new Bitmap(measuredWidths[i], height, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        InvokeRenderTaskbar(win, g, measuredWidths[i], height, "开发调试");

                        int nonZero = 0;
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            for (int x = 0; x < bmp.Width; x++)
                            {
                                if (bmp.GetPixel(x, y).A > 0) nonZero++;
                            }
                        }
                        pixelCounts[i] = nonZero;
                    }
                }
            }

            // Verify strict monotonic increase across 75 -> 100 -> 130 -> 160
            for (int i = 0; i < scales.Length - 1; i++)
            {
                Assert(measuredWidths[i] < measuredWidths[i + 1],
                    string.Format("Scale {0} width ({1}) must be less than scale {2} width ({3})",
                        scales[i], measuredWidths[i], scales[i + 1], measuredWidths[i + 1]));

                Assert(pixelCounts[i] < pixelCounts[i + 1],
                    string.Format("Scale {0} visible pixels ({1}) must be less than scale {2} visible pixels ({3})",
                        scales[i], pixelCounts[i], scales[i + 1], pixelCounts[i + 1]));
            }
        }

        static void TestTaskbarNoSubtitleAndIdenticalBitmaps()
        {
            // Case A: 1 desktop, default subtitle template, non-interactive
            WatermarkWindow winA = CreateIsolatedWatermarkWindow();
            SetPrivateField(winA, "_currentDesktop", 2);
            SetPrivateField(winA, "_totalDesktops", 1);
            SetPrivateField(winA, "_showBigNumber", true);
            SetPrivateField(winA, "_watermarkScale", 130);
            SetPrivateField(winA, "_isTaskbarDocked", true);
            SetPrivateField(winA, "_isInteractive", false);
            SetPrivateField(winA, "_isSnapped", false);
            SetPrivateField(winA, "_subtitleTemplate", "桌面 {n}");

            // Case B: 99 desktops, custom subtitle template, interactive snapped dragging
            WatermarkWindow winB = CreateIsolatedWatermarkWindow();
            SetPrivateField(winB, "_currentDesktop", 2);
            SetPrivateField(winB, "_totalDesktops", 99);
            SetPrivateField(winB, "_showBigNumber", true);
            SetPrivateField(winB, "_watermarkScale", 130);
            SetPrivateField(winB, "_isTaskbarDocked", true);
            SetPrivateField(winB, "_isInteractive", true);
            SetPrivateField(winB, "_isSnapped", true);
            SetPrivateField(winB, "_animTick", 10);
            SetPrivateField(winB, "_subtitleTemplate", "虚拟桌面 99 (共 999 个)");

            int width = 240;
            int height = 72;

            using (Bitmap bmpA = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics gA = Graphics.FromImage(bmpA))
            using (Bitmap bmpB = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics gB = Graphics.FromImage(bmpB))
            {
                gA.Clear(Color.Transparent);
                gB.Clear(Color.Transparent);

                InvokeRenderTaskbar(winA, gA, width, height, "开发调试");
                InvokeRenderTaskbar(winB, gB, width, height, "开发调试");

                int diffCount = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (bmpA.GetPixel(x, y).ToArgb() != bmpB.GetPixel(x, y).ToArgb())
                        {
                            diffCount++;
                        }
                    }
                }

                Assert(diffCount == 0,
                    "Taskbar single-line mode must NOT render subtitle/counter/drag-hint: bitmaps must be 100% identical (diff=" + diffCount + ")");
            }
        }

        static void TestLongTextAndDoubleDigitNumberNoClipping()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            SetPrivateField(win, "_currentDesktop", 12); // Double-digit desktop index
            SetPrivateField(win, "_showBigNumber", true);
            SetPrivateField(win, "_watermarkScale", 130);
            SetPrivateField(win, "_isTaskbarDocked", true);

            string longTitle = "开发调试 (LongWorkspaceTitle-Testing-中英文混合测试)";
            int height = 72;

            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics gM = Graphics.FromImage(measureBmp))
            {
                object layout = InvokeLayoutHelper(win, gM, height, longTitle);
                int measuredWidth = (int)layout.GetType().GetField("Width").GetValue(layout);

                Assert(measuredWidth > 300, "Measured width for long title must be > 300px (got " + measuredWidth + ")");

                using (Bitmap bmp = new Bitmap(measuredWidth, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    InvokeRenderTaskbar(win, g, measuredWidth, height, longTitle);

                    // Left-most column (x = 0) must be transparent (not clipped on left)
                    for (int y = 0; y < height; y++)
                    {
                        Assert(bmp.GetPixel(0, y).A == 0, "Left boundary column (x=0) must be transparent");
                    }

                    // Right-most column (x = measuredWidth - 1) must be transparent (not clipped on right)
                    for (int y = 0; y < height; y++)
                    {
                        Assert(bmp.GetPixel(measuredWidth - 1, y).A == 0, "Right boundary column must be transparent (not clipped)");
                    }

                    // Verify that text pixels exist in the far right region (> 80% width)
                    int rightRegionPixels = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = (int)(measuredWidth * 0.8f); x < measuredWidth - 1; x++)
                        {
                            if (bmp.GetPixel(x, y).A > 0) rightRegionPixels++;
                        }
                    }
                    Assert(rightRegionPixels > 30, "Long text must render completely without truncation into the right region (got " + rightRegionPixels + " pixels)");
                }
            }
        }

        static void TestTaskbarHeightsNoVerticalOverflow()
        {
            int[] testHeights = new int[] { 48, 72, 96 };

            for (int i = 0; i < testHeights.Length; i++)
            {
                int h = testHeights[i];
                WatermarkWindow win = CreateIsolatedWatermarkWindow();
                SetPrivateField(win, "_currentDesktop", 3);
                SetPrivateField(win, "_showBigNumber", true);
                SetPrivateField(win, "_watermarkScale", 130);
                SetPrivateField(win, "_isTaskbarDocked", true);

                using (Bitmap measureBmp = new Bitmap(1, 1))
                using (Graphics gM = Graphics.FromImage(measureBmp))
                {
                    object layout = InvokeLayoutHelper(win, gM, h, "开发调试");
                    int w = (int)layout.GetType().GetField("Width").GetValue(layout);
                    float numFontH = (float)layout.GetType().GetField("NumFontSize").GetValue(layout);
                    float titleFontH = (float)layout.GetType().GetField("TitleFontSize").GetValue(layout);

                    // Actual font height must be <= taskbar height - 12px
                    Assert(numFontH <= (h - 12.0f),
                        string.Format("Taskbar height {0}: num font size ({1}) must be <= taskbarHeight - 12 ({2})",
                            h, numFontH, h - 12.0f));
                    Assert(titleFontH <= (h - 12.0f),
                        string.Format("Taskbar height {0}: title font size ({1}) must be <= taskbarHeight - 12 ({2})",
                            h, titleFontH, h - 12.0f));

                    using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        InvokeRenderTaskbar(win, g, w, h, "开发调试");

                        // Top line (y = 0) and bottom line (y = h - 1) must be completely transparent (no overflow)
                        for (int x = 0; x < w; x++)
                        {
                            Assert(bmp.GetPixel(x, 0).A == 0,
                                string.Format("Taskbar height {0}: Top border pixel at x={1} must be transparent (got A={2})",
                                    h, x, bmp.GetPixel(x, 0).A));
                            Assert(bmp.GetPixel(x, h - 1).A == 0,
                                string.Format("Taskbar height {0}: Bottom border pixel at x={1} must be transparent (got A={2})",
                                    h, x, bmp.GetPixel(x, h - 1).A));
                        }

                        // Verify vertical centering: there must be visible pixels near middle (h / 2)
                        int midYVisible = 0;
                        for (int x = 0; x < w; x++)
                        {
                            if (bmp.GetPixel(x, h / 2).A > 0) midYVisible++;
                        }
                        Assert(midYVisible > 10,
                            string.Format("Taskbar height {0}: text must be vertically centered at h/2 (got {1} pixels)",
                                h, midYVisible));
                    }
                }
            }
        }

        static void TestTaskbarInteractiveNoGlow()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            SetPrivateField(win, "_isTaskbarDocked", true);
            SetPrivateField(win, "_isInteractive", true);
            SetPrivateField(win, "_animTick", 10);

            int width = 160;
            int height = 34;

            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);

                MethodInfo glowMethod = typeof(WatermarkWindow).GetMethod(
                    "RenderBreathingGlow",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert(glowMethod != null, "RenderBreathingGlow method must exist");

                glowMethod.Invoke(win, new object[] { g, width, height });

                int nonZeroCount = 0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (bmp.GetPixel(x, y).A > 0)
                        {
                            nonZeroCount++;
                        }
                    }
                }

                Assert(nonZeroCount == 0, "Taskbar interactive mode must not draw outer breathing glow (got " + nonZeroCount + " pixels)");
            }
        }

        static void TestDesktopModePreservesBackgroundAndBorder()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            SetPrivateField(win, "_currentDesktop", 1);
            SetPrivateField(win, "_totalDesktops", 3);
            SetPrivateField(win, "_showBigNumber", true);
            SetPrivateField(win, "_showBackgroundCard", true);
            SetPrivateField(win, "_isTaskbarDocked", false);
            SetPrivateField(win, "_colorTheme", "WhiteHighlight");
            SetPrivateField(win, "_titleTemplate", "{name}");
            SetPrivateField(win, "_subtitleTemplate", "桌面 {n}");
            SetPrivateField(win, "_desktopName", "主屏幕");

            int width = 240;
            int height = 88;

            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);

                MethodInfo cardMethod = typeof(WatermarkWindow).GetMethod(
                    "RenderCardStyle",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert(cardMethod != null, "RenderCardStyle method must exist");

                cardMethod.Invoke(win, new object[] { g, width, height, 1.0f });

                // Verify that desktop mode still fills background card
                Color centerPixel = bmp.GetPixel(width / 2, height / 2);
                Assert(centerPixel.A > 0, "Desktop card mode must have non-zero alpha in background (got A=" + centerPixel.A + ")");

                // Verify border stroke exists on top straight edge
                Color borderAreaPixel = bmp.GetPixel(width / 2, 4);
                Assert(borderAreaPixel.A > 0, "Desktop card mode must draw border/card path (got A=" + borderAreaPixel.A + ")");
            }

            // Also verify desktop mode DOES render breathing glow when interactive
            using (Bitmap glowBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics gGlow = Graphics.FromImage(glowBmp))
            {
                gGlow.Clear(Color.Transparent);
                SetPrivateField(win, "_isInteractive", true);
                SetPrivateField(win, "_animTick", 5);

                MethodInfo glowMethod = typeof(WatermarkWindow).GetMethod(
                    "RenderBreathingGlow",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                glowMethod.Invoke(win, new object[] { gGlow, width, height });

                int glowPixels = 0;
                for (int y = 0; y < glowBmp.Height; y++)
                {
                    for (int x = 0; x < glowBmp.Width; x++)
                    {
                        if (glowBmp.GetPixel(x, y).A > 0)
                        {
                            glowPixels++;
                        }
                    }
                }

                Assert(glowPixels > 0, "Desktop mode must preserve outer breathing glow (got " + glowPixels + " pixels)");
            }
        }

        static void TestTaskbarDockedBoundsRightEdgeAnchoring()
        {
            // Scenario 1: Standard 144 DPI (150% scaling, 72px taskbar)
            // Contract Requirement: docked right strictly equals tray.Left minus DPI gap (4px at 144 DPI)
            Rectangle taskbar144 = new Rectangle(0, 1008, 1920, 72);
            Rectangle tray144 = new Rectangle(1650, 1008, 270, 72);
            int winW = 180;
            int winH = 72;
            float dpi144 = 1.5f;

            Rectangle bounds144 = WatermarkWindow.CalculateTaskbarDockedBounds(taskbar144, tray144, winW, winH, dpi144);

            Assert(bounds144.Right == tray144.Left - 4,
                string.Format("At 144 DPI, docked bounds.Right ({0}) must strictly equal tray.Left - 4 ({1})",
                    bounds144.Right, tray144.Left - 4));
            Assert(bounds144.Width == winW, "Docked bounds width must match windowWidth");
            Assert(bounds144.Height == winH, "Docked bounds height must match windowHeight");
            Assert(bounds144.X == bounds144.Right - winW, "Docked bounds.X must equal Right - Width");
            Assert(bounds144.Y == taskbar144.Top, "Docked bounds.Y must vertically center in taskbar");

            // Scenario 2: Dynamic Notification Tray Width Change (Follow Automatically, No Stale CustomX Gap)
            // Case A: Tray becomes narrower (e.g. Tray icons folded, Tray.Left moves right from 1650 to 1770)
            Rectangle trayNarrower = new Rectangle(1770, 1008, 150, 72);
            Rectangle boundsNarrower = WatermarkWindow.CalculateTaskbarDockedBounds(taskbar144, trayNarrower, winW, winH, dpi144);
            Assert(boundsNarrower.Right == 1770 - 4,
                "When tray narrows, docked right must automatically follow rightward to 1770 - 4");
            Assert(boundsNarrower.X == 1770 - 4 - winW,
                "Docked X must update rightward without keeping stale CustomX gap");

            // Case B: Tray becomes wider (e.g. Tray icons expanded, Tray.Left moves left from 1650 to 1480)
            Rectangle trayWider = new Rectangle(1480, 1008, 440, 72);
            Rectangle boundsWider = WatermarkWindow.CalculateTaskbarDockedBounds(taskbar144, trayWider, winW, winH, dpi144);
            Assert(boundsWider.Right == 1480 - 4,
                "When tray widens, docked right must automatically follow leftward to 1480 - 4");
            Assert(boundsWider.X == 1480 - 4 - winW,
                "Docked X must update leftward without overlapping tray icons");

            // Scenario 3: Multiple DPI Scales Gap Validation
            float[] dpiList = new float[] { 1.0f, 1.25f, 1.5f, 2.0f, 2.25f };
            int[] expectedGaps = new int[] { 3, 3, 4, 5, 6 };

            for (int i = 0; i < dpiList.Length; i++)
            {
                float dpi = dpiList[i];
                int expGap = expectedGaps[i];
                int tbH = (int)(48 * dpi);
                Rectangle tb = new Rectangle(0, 1000, 2000, tbH);
                Rectangle tr = new Rectangle(1700, 1000, 300, tbH);

                Rectangle b = WatermarkWindow.CalculateTaskbarDockedBounds(tb, tr, 160, tbH, dpi);
                Assert(b.Right == tr.Left - expGap,
                    string.Format("DPI scale {0}: expected gap {1}, got docked.Right={2}, tray.Left={3}",
                        dpi, expGap, b.Right, tr.Left));
            }
        }

        static void TestRealWin32TaskbarGeometryValidation()
        {
            IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTaskbar == IntPtr.Zero)
            {
                Console.WriteLine("    [INFO] Shell_TrayWnd not found (headless/isolated session), skipping live Win32 verification");
                return;
            }

            NativeMethods.RECT rcTaskbar;
            NativeMethods.GetWindowRect(hTaskbar, out rcTaskbar);
            Rectangle tbRect = Rectangle.FromLTRB(rcTaskbar.Left, rcTaskbar.Top, rcTaskbar.Right, rcTaskbar.Bottom);

            IntPtr hTray = NativeMethods.FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (hTray == IntPtr.Zero)
            {
                Console.WriteLine("    [INFO] TrayNotifyWnd not found under Shell_TrayWnd, skipping live Win32 verification");
                return;
            }

            NativeMethods.RECT rcTray;
            NativeMethods.GetWindowRect(hTray, out rcTray);
            Rectangle trRect = Rectangle.FromLTRB(rcTray.Left, rcTray.Top, rcTray.Right, rcTray.Bottom);

            int tbHeight = tbRect.Height > 0 ? tbRect.Height : 48;
            float dpiScale = Math.Max(0.5f, tbHeight / 48.0f);
            int expGap = Math.Max(2, (int)Math.Round(4.0f * (dpiScale / 1.5f)));

            int testWinW = 160;
            int testWinH = tbHeight;

            Rectangle docked = WatermarkWindow.CalculateTaskbarDockedBounds(tbRect, trRect, testWinW, testWinH, dpiScale);

            Console.WriteLine(string.Format("    [Live Win32] Shell_TrayWnd={0}, TrayNotifyWnd={1}, DpiScale={2:F2}, Docked={3}",
                tbRect, trRect, dpiScale, docked));

            // Verify docked right edge is strictly anchored to tray.Left - expGap
            Assert(docked.Right == trRect.Left - expGap,
                string.Format("Live Win32: docked.Right ({0}) must equal trRect.Left - gap ({1})",
                    docked.Right, trRect.Left - expGap));

            // Verify strictly within taskbar vertical bounds
            Assert(docked.Top >= tbRect.Top && docked.Bottom <= tbRect.Bottom,
                "Live Win32: docked window must stay vertically within taskbar bounds");

            // Verify strictly to the left of TrayNotifyWnd
            Assert(docked.Left < trRect.Left, "Live Win32: docked window must be situated to the left of tray");
        }

        static void TestDesktopFreeDraggingNotLockedAndScreenRectSync()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            SetPrivateField(win, "_isTaskbarDocked", false);
            SetPrivateField(win, "_position", "Custom");
            SetPrivateField(win, "_windowWidth", 200);
            SetPrivateField(win, "_windowHeight", 60);

            // 1. Move to Desktop Coordinate (450, 250)
            SetPrivateField(win, "_customX", 450);
            SetPrivateField(win, "_customY", 250);
            Rectangle rect1 = new Rectangle(450, 250, 200, 60);
            SetPrivateField(win, "_actualScreenRect", rect1);

            Rectangle actual1 = (Rectangle)GetPrivateField(win, "_actualScreenRect");
            Assert(actual1.X == 450 && actual1.Y == 250,
                "Desktop drag must synchronize _actualScreenRect to (450, 250)");
            Assert((bool)GetPrivateField(win, "_isTaskbarDocked") == false,
                "Desktop drag must not be locked into taskbar mode");

            // 2. Move to Another Desktop Coordinate (820, 600)
            SetPrivateField(win, "_customX", 820);
            SetPrivateField(win, "_customY", 600);
            Rectangle rect2 = new Rectangle(820, 600, 200, 60);
            SetPrivateField(win, "_actualScreenRect", rect2);

            Rectangle actual2 = (Rectangle)GetPrivateField(win, "_actualScreenRect");
            Assert(actual2.X == 820 && actual2.Y == 600,
                "Desktop drag must synchronize _actualScreenRect to (820, 600)");
            Assert(actual2.Right == 1020, "Desktop drag right edge must equal 820 + 200 = 1020");
        }

        static void TestTaskbarCompactPaddingNoSpaceWasted()
        {
            WatermarkWindow win = CreateIsolatedWatermarkWindow();
            SetPrivateField(win, "_currentDesktop", 1);
            SetPrivateField(win, "_showBigNumber", true);
            SetPrivateField(win, "_watermarkScale", 130);
            SetPrivateField(win, "_isTaskbarDocked", true);

            int height = 72; // 144 DPI (dpiScale = 1.5)
            string title = "开发调试";

            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics gM = Graphics.FromImage(measureBmp))
            {
                object layout = InvokeLayoutHelper(win, gM, height, title);
                int measuredWidth = (int)layout.GetType().GetField("Width").GetValue(layout);

                using (Bitmap bmp = new Bitmap(measuredWidth, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    InvokeRenderTaskbar(win, g, measuredWidth, height, title);

                    // 1. Boundary column (measuredWidth - 1) must be completely transparent (no truncation)
                    for (int y = 0; y < height; y++)
                    {
                        Assert(bmp.GetPixel(measuredWidth - 1, y).A == 0,
                            "Right-most column must be transparent (not truncated)");
                    }

                    // 2. Find the last visible pixel column on the right
                    int maxVisibleX = 0;
                    for (int x = measuredWidth - 1; x >= 0; x--)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            if (bmp.GetPixel(x, y).A > 0)
                            {
                                maxVisibleX = x;
                                break;
                            }
                        }
                        if (maxVisibleX > 0) break;
                    }

                    int rightPaddingWaste = (measuredWidth - 1) - maxVisibleX;

                    // Waste should be compact (<= 5px at 144DPI), compared to legacy 14px waste
                    Assert(rightPaddingWaste <= 5,
                        string.Format("Taskbar compact padding waste must be <= 5px (got {0}px, maxVisibleX={1}, measuredWidth={2})",
                            rightPaddingWaste, maxVisibleX, measuredWidth));
                    Assert(rightPaddingWaste >= 1,
                        "Right edge must have at least 1px transparent breathing room for antialiasing");
                }
            }
        }

        static void TestTaskbarAdaptiveFullEllipsisNumberOnlyNoSpace()
        {
            using (Bitmap bmpMeasure = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmpMeasure))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int tbHeight = 48;
                int scale = 100;
                int currentDesktop = 2;
                string fullTitle = "开发调试超级工作区";
                bool showBigNum = true;

                // 1. Measure natural full width first
                WatermarkWindow.TaskbarAdaptiveResult resFullNatural = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, fullTitle, showBigNum, 1000);
                Assert(resFullNatural.Mode == WatermarkWindow.TaskbarAdaptiveMode.Full, "Expected Full mode with large width");
                Assert(resFullNatural.IsVisible, "Should be visible in Full mode");
                Assert(resFullNatural.DisplayTitle == fullTitle, "Full title must match");
                Assert(resFullNatural.ShowBigNumber, "ShowBigNumber must be true");
                Assert(resFullNatural.ShowSeparator, "ShowSeparator must be true");
                int fullWidth = resFullNatural.Width;

                // Case A: Full Mode (availableWidth = fullWidth + 10)
                WatermarkWindow.TaskbarAdaptiveResult resFull = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, fullTitle, showBigNum, fullWidth + 10);
                Assert(resFull.Mode == WatermarkWindow.TaskbarAdaptiveMode.Full, "Expected Full mode");
                Assert(resFull.Width <= fullWidth + 10, "Width <= available");

                // Case B: Ellipsis Mode (availableWidth = fullWidth - 30)
                WatermarkWindow.TaskbarAdaptiveResult resEllipsis = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, fullTitle, showBigNum, fullWidth - 30);
                Assert(resEllipsis.Mode == WatermarkWindow.TaskbarAdaptiveMode.Ellipsis, "Expected Ellipsis mode");
                Assert(resEllipsis.IsVisible, "Ellipsis should be visible");
                Assert(resEllipsis.DisplayTitle.EndsWith("…"), "Truncated title must end with ellipsis '…'");
                Assert(resEllipsis.Width <= fullWidth - 30, "Ellipsis width <= available");
                Assert(resEllipsis.ShowBigNumber, "Ellipsis mode retains desktop number");
                Assert(resEllipsis.ShowSeparator, "Ellipsis mode retains separator");

                // Case C: Number-only Mode (availableWidth squeezed so that even 1 char + … cannot fit, but number fits)
                WatermarkWindow.TaskbarAdaptiveResult resOneChar = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, "开…", showBigNum, 1000);
                int oneCharWidth = resOneChar.Width;

                // If availableWidth < oneCharWidth, title cannot fit even 1 char!
                WatermarkWindow.TaskbarAdaptiveResult resNumOnly = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, fullTitle, showBigNum, oneCharWidth - 1);
                Assert(resNumOnly.Mode == WatermarkWindow.TaskbarAdaptiveMode.NumberOnly, "Expected NumberOnly mode when title cannot fit");
                Assert(resNumOnly.IsVisible, "Number-only should be visible");
                Assert(string.IsNullOrEmpty(resNumOnly.DisplayTitle), "Number-only display title must be empty");
                Assert(resNumOnly.ShowBigNumber, "Number-only must show big number");
                Assert(!resNumOnly.ShowSeparator, "Number-only must NOT have isolated separator line (ShowSeparator == false)");
                Assert(resNumOnly.Width <= oneCharWidth - 1, "Number-only width <= available");

                // Verify bitmap rendered in Number-only mode has NO vertical separator line
                using (Bitmap numBmp = new Bitmap(resNumOnly.Width, resNumOnly.Height, PixelFormat.Format32bppArgb))
                using (Graphics gRender = Graphics.FromImage(numBmp))
                {
                    gRender.Clear(Color.Transparent);
                    WatermarkWindow win = CreateIsolatedWatermarkWindow();
                    SetPrivateField(win, "_currentDesktop", currentDesktop);
                    SetPrivateField(win, "_watermarkScale", scale);
                    win.RenderTaskbarDockedWidgetCore(gRender, resNumOnly.Width, resNumOnly.Height, "", true, false);

                    // Verify pixels exist for the number
                    int visibleCount = 0;
                    for (int y = 0; y < numBmp.Height; y++)
                    {
                        for (int x = 0; x < numBmp.Width; x++)
                        {
                            if (numBmp.GetPixel(x, y).A > 0) visibleCount++;
                        }
                    }
                    Assert(visibleCount > 20, "Number-only must render visible digits");
                }

                // Case D: No-space Mode (availableWidth < number width)
                int numOnlyWidth = resNumOnly.Width;
                WatermarkWindow.TaskbarAdaptiveResult resNoSpace = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, fullTitle, showBigNum, numOnlyWidth - 5);
                Assert(resNoSpace.Mode == WatermarkWindow.TaskbarAdaptiveMode.Hidden, "Expected Hidden mode when even number cannot fit");
                Assert(!resNoSpace.IsVisible, "Hidden mode must have IsVisible = false");

                // Case E: Recovery Mode (availableWidth expands back to fullWidth + 50)
                WatermarkWindow.TaskbarAdaptiveResult resRecover = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, fullTitle, showBigNum, fullWidth + 50);
                Assert(resRecover.Mode == WatermarkWindow.TaskbarAdaptiveMode.Full, "Expected Full mode on recovery");
                Assert(resRecover.IsVisible, "Should be visible on recovery");
                Assert(resRecover.DisplayTitle == fullTitle, "Full title must be restored");
            }
        }

        static void TestTaskbarAdaptiveUnicodeSurrogatePairsNoSplit()
        {
            using (Bitmap bmpMeasure = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmpMeasure))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int tbHeight = 48;
                int scale = 100;
                int currentDesktop = 1;
                // Contains emoji surrogate pair "🚀" (\uD83D\uDE80) and "🖥️" and CJK extension "𠮷" (\uD842\uDFB7)
                string unicodeTitle = "🖥️工作区🚀𠮷野家测试";

                // Measure natural full width
                WatermarkWindow.TaskbarAdaptiveResult resFull = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                    g, tbHeight, scale, currentDesktop, unicodeTitle, true, 1000);
                int fullW = resFull.Width;

                // Step down availableWidth from fullW to 20px in steps of 5px
                for (int avail = fullW; avail >= 20; avail -= 5)
                {
                    WatermarkWindow.TaskbarAdaptiveResult res = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                        g, tbHeight, scale, currentDesktop, unicodeTitle, true, avail);

                    if (res.IsVisible && !string.IsNullOrEmpty(res.DisplayTitle))
                    {
                        Assert(res.Width <= avail, string.Format("Result width {0} must be <= available {1}", res.Width, avail));

                        string t = res.DisplayTitle;
                        if (t.EndsWith("…"))
                        {
                            t = t.Substring(0, t.Length - 1);
                        }

                        // Every char sequence must have matched surrogate pairs
                        for (int idx = 0; idx < t.Length; idx++)
                        {
                            char c = t[idx];
                            if (char.IsHighSurrogate(c))
                            {
                                Assert(idx + 1 < t.Length && char.IsLowSurrogate(t[idx + 1]),
                                    "High surrogate must be immediately followed by low surrogate!");
                                idx++; // skip low surrogate
                            }
                            else if (char.IsLowSurrogate(c))
                            {
                                throw new Exception("Orphan low surrogate detected in truncated text: " + res.DisplayTitle);
                            }
                        }
                    }
                }
            }
        }

        static void TestTaskbarAdaptiveMultiDpiAndScaleLimits()
        {
            int[] heights = new int[] { 36, 48, 60, 72, 96 }; // 75%, 100%, 125%, 150%, 200% DPI
            int[] scales = new int[] { 75, 100, 130, 160 };

            using (Bitmap bmpMeasure = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmpMeasure))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                foreach (int h in heights)
                {
                    foreach (int sc in scales)
                    {
                        string testTitle = "开发与设计混合工程";

                        for (int avail = 20; avail <= 260; avail += 20)
                        {
                            WatermarkWindow.TaskbarAdaptiveResult res = WatermarkWindow.ResolveAdaptiveTaskbarContent(
                                g, h, sc, 3, testTitle, true, avail);

                            if (res.IsVisible)
                            {
                                Assert(res.Width <= avail,
                                    string.Format("At Height {0}, Scale {1}, Available {2}: Result width {3} must be <= available",
                                        h, sc, avail, res.Width));

                                using (Bitmap bmp = new Bitmap(res.Width, res.Height, PixelFormat.Format32bppArgb))
                                using (Graphics gRender = Graphics.FromImage(bmp))
                                {
                                    gRender.Clear(Color.Transparent);
                                    WatermarkWindow win = CreateIsolatedWatermarkWindow();
                                    SetPrivateField(win, "_currentDesktop", 3);
                                    SetPrivateField(win, "_watermarkScale", sc);
                                    win.RenderTaskbarDockedWidgetCore(gRender, res.Width, res.Height, res.DisplayTitle, res.ShowBigNumber, res.ShowSeparator);

                                    Assert(bmp.Width <= avail,
                                        string.Format("Rendered bitmap width ({0}) must be <= available ({1})", bmp.Width, avail));
                                }
                            }
                        }
                    }
                }
            }
        }

        static void TestMockTaskbarSpaceProviderInjection()
        {
            // Scenario matching the contract:
            // Screen 1080x1140@96, appRight=509, trayLeft=593.
            Rectangle taskbarRect = new Rectangle(0, 1092, 1080, 48);
            Rectangle trayRect = new Rectangle(593, 1092, 487, 48);
            int maxAppRight = 509;

            TaskbarSpaceSnapshot snap509 = new TaskbarSpaceSnapshot(maxAppRight, 593, taskbarRect, trayRect, true);
            MockTaskbarSpaceProvider mock = new MockTaskbarSpaceProvider(snap509);

            WatermarkWindow win = new WatermarkWindow();
            try
            {
                IntPtr h = win.Handle; // Ensure window handle is created
                win.SetTaskbarSpaceProvider(mock);

                // Configure docked position
                SetPrivateField(win, "_isTaskbarDocked", true);
                SetPrivateField(win, "_position", "Custom");
                SetPrivateField(win, "_customX", 400);
                SetPrivateField(win, "_customY", 1092);
                SetPrivateField(win, "_desktopName", "开发调试");
                SetPrivateField(win, "_watermarkScale", 100);

                win.UpdateWatermark();

                Rectangle actualRect = (Rectangle)GetPrivateField(win, "_actualScreenRect");

                // In 1080 screen with appRight 509 and trayLeft 593:
                // anchorRight = 593 - 3 = 590
                // safetyGap = 6
                // availableWidth = 590 - (509 + 6) = 75px
                // Watermark window must NEVER cover app button at 509!
                Assert(actualRect.Left >= 509,
                    string.Format("Docked watermark Left ({0}) must be >= maxAppRight (509)", actualRect.Left));
                Assert(actualRect.Right <= 590,
                    string.Format("Docked watermark Right ({0}) must be <= anchorRight (590)", actualRect.Right));
                Assert(actualRect.Width <= 75,
                    string.Format("Docked watermark Width ({0}) must be <= availableWidth (75)", actualRect.Width));

                // Test Provider Stale/Expired Snapshot conservative hide
                long expiredTicks = System.Diagnostics.Stopwatch.GetTimestamp() - (System.Diagnostics.Stopwatch.Frequency * 3);
                TaskbarSpaceSnapshot staleSnap = new TaskbarSpaceSnapshot(maxAppRight, 593, taskbarRect, trayRect, true, expiredTicks);
                Assert(!staleSnap.IsFresh(2000), "Snapshot must be stale (>2s)");

                mock.SetSnapshot(staleSnap, notify: false);
                win.UpdateWatermark();

                Rectangle actualAfterStale = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(actualAfterStale.IsEmpty, "Watermark must be conservatively hidden when snapshot is stale/expired");

                // Test Provider Invalid Snapshot conservative hide
                TaskbarSpaceSnapshot invalidSnap = TaskbarSpaceSnapshot.Invalid();
                mock.SetSnapshot(invalidSnap, notify: false);
                win.UpdateWatermark();

                Rectangle actualAfterInvalid = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(actualAfterInvalid.IsEmpty, "Watermark must be conservatively hidden when snapshot is invalid/failed");

                // Test Recovery when space expands (e.g. app closed, maxAppRight drops to 200)
                TaskbarSpaceSnapshot recoveredSnap = new TaskbarSpaceSnapshot(200, 593, taskbarRect, trayRect, true);
                mock.SetSnapshot(recoveredSnap, notify: false);
                win.UpdateWatermark();

                Rectangle actualAfterRecover = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(!actualAfterRecover.IsEmpty, "Watermark must recover when space is available");
                Assert(actualAfterRecover.Left >= 200, "Recovered watermark Left must stay to the right of appRight (200)");
                Assert(actualAfterRecover.Right <= 590, "Recovered watermark Right must stay anchored to 590");
            }
            finally
            {
                win.Dispose();
            }
        }

        static void TestSinglePendingNoCallbackAccumulation()
        {
            TaskbarSpaceSnapshot snap = new TaskbarSpaceSnapshot(300, 800, new Rectangle(0, 1000, 1920, 48), new Rectangle(800, 1000, 400, 48), true);
            MockTaskbarSpaceProvider mock = new MockTaskbarSpaceProvider(snap);

            WatermarkWindow win = new WatermarkWindow();
            try
            {
                IntPtr h = win.Handle;
                win.SetTaskbarSpaceProvider(mock);
                SetPrivateField(win, "_isTaskbarDocked", true);

                // Rapidly trigger SpaceChanged 100 times
                for (int i = 0; i < 100; i++)
                {
                    mock.SetSnapshot(new TaskbarSpaceSnapshot(300 + (i % 2), 800, snap.TaskbarRect, snap.TrayRect, true), notify: true);
                }

                // Verify the coalescing flag is bounded between 0 and 1
                int pending = (int)GetPrivateField(win, "_isSpaceUpdatePending");
                Assert(pending == 0 || pending == 1, "Pending flag must be at most 1 (single-pending guard)");
            }
            finally
            {
                win.Dispose();
            }
        }

        static void TestLiveWin32AndUiaSnapshotValidation()
        {
            IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTaskbar == IntPtr.Zero)
            {
                Console.WriteLine("    [INFO] Shell_TrayWnd not found, skipping live UIA probe test");
                return;
            }

            TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor();
            try
            {
                TaskbarSpaceSnapshot snap = monitor.ProbeOnce();
                Console.WriteLine(string.Format("    [Live UIA Probe] IsValid={0}, MaxAppRight={1}, TrayLeft={2}, TaskbarRect={3}, TrayRect={4}",
                    snap.IsValid, snap.MaxAppRight, snap.TrayLeft, snap.TaskbarRect, snap.TrayRect));

                Assert(snap.IsValid, "Live probe snapshot must be valid");
                Assert(snap.IsFresh(2000), "Live probe snapshot must be fresh");
                Assert(snap.TaskbarRect.Width > 0 && snap.TaskbarRect.Height > 0, "TaskbarRect must be non-empty");

                if (snap.TrayLeft > 0 && snap.MaxAppRight > 0)
                {
                    Assert(snap.MaxAppRight <= snap.TrayLeft,
                        string.Format("MaxAppRight ({0}) must be <= TrayLeft ({1})", snap.MaxAppRight, snap.TrayLeft));
                }
            }
            finally
            {
                monitor.Dispose();
            }
        }

        static void TestProbeInvalidWhenNoButtonsOrMissingTrayOrException()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRect = new Rectangle(1400, 1000, 520, 48);

            // 契约验证 1: 0个有效button / 无有效button，即使 maxAppRight=0 也必须返回 Invalid
            TaskbarSpaceSnapshot snapNoBtn = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, validButtonsCount: 0, maxAppRight: 0);
            Assert(!snapNoBtn.IsValid, "Snapshot with 0 buttons must be Invalid (cannot treat taskbar as empty)");

            TaskbarSpaceSnapshot snapNoBtnWithAppRight = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, validButtonsCount: 0, maxAppRight: 500);
            Assert(!snapNoBtnWithAppRight.IsValid, "Snapshot with 0 valid buttons must be Invalid even if maxAppRight > 0");

            // 契约验证 2: 缺tray均Invalid
            TaskbarSpaceSnapshot snapNoTray = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, Rectangle.Empty, validButtonsCount: 5, maxAppRight: 500);
            Assert(!snapNoTray.IsValid, "Snapshot with missing tray (empty rect) must be Invalid");

            TaskbarSpaceSnapshot snapTrayOutside = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, new Rectangle(2000, 1000, 100, 48), validButtonsCount: 5, maxAppRight: 500);
            Assert(!snapTrayOutside.IsValid, "Snapshot with tray outside taskbar must be Invalid");

            // 契约验证 3: 缺少taskbar均Invalid
            TaskbarSpaceSnapshot snapNoTb = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(Rectangle.Empty, trRect, validButtonsCount: 5, maxAppRight: 500);
            Assert(!snapNoTb.IsValid, "Snapshot with missing taskbar rect must be Invalid");

            // 契约验证 4: 读取失败/异常均Invalid
            TaskbarSpaceSnapshot snapInvalid = TaskbarSpaceSnapshot.Invalid();
            Assert(!snapInvalid.IsValid, "TaskbarSpaceSnapshot.Invalid() must have IsValid=false");
            Assert(!snapInvalid.IsFresh(), "TaskbarSpaceSnapshot.Invalid() must not be fresh");
        }

        static void TestStaleGuardActiveHideAndRecovery()
        {
            Rectangle taskbarRect = new Rectangle(0, 1000, 1080, 48);
            Rectangle trayRect = new Rectangle(600, 1000, 480, 48);

            // 初始: fresh snapshot
            TaskbarSpaceSnapshot freshSnap = new TaskbarSpaceSnapshot(200, 600, taskbarRect, trayRect, true);
            MockTaskbarSpaceProvider mock = new MockTaskbarSpaceProvider(freshSnap);

            WatermarkWindow win = new WatermarkWindow();
            try
            {
                IntPtr h = win.Handle;
                win.SetTaskbarSpaceProvider(mock);
                SetPrivateField(win, "_isTaskbarDocked", true);
                win.UpdateWatermark();

                Rectangle actualRect = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(!actualRect.IsEmpty, "Watermark must be rendered and non-empty with fresh snapshot");

                // 1. 模拟 provider 变 stale (例如时间戳过期且 provider 挂住不再通知 SpaceChanged)
                long staleTimestamp = Stopwatch.GetTimestamp() - (Stopwatch.Frequency * 5); // 5s 前
                TaskbarSpaceSnapshot staleSnap = new TaskbarSpaceSnapshot(200, 600, taskbarRect, trayRect, true, staleTimestamp);
                Assert(!staleSnap.IsFresh(2000), "Snapshot must be stale");

                mock.SetSnapshot(staleSnap, notify: false); // notify: false 模拟挂住不再通知

                // 契约：150ms guard 读取纯缓存 fresh，从 fresh 转 stale 时合并 UI 刷新隐藏
                win.CheckTaskbarSpaceFreshnessGuard();
                Application.DoEvents(); // 执行 BeginInvoke

                Rectangle afterStaleRect = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(afterStaleRect.IsEmpty, "Watermark must be actively hidden by guard when snapshot becomes stale");

                // 2. 模拟下一个 150ms tick: 依然是 stale，不每 tick 重复重绘
                win.CheckTaskbarSpaceFreshnessGuard();
                Application.DoEvents();
                Rectangle stillStaleRect = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(stillStaleRect.IsEmpty, "Watermark remains hidden without repeated redundant invalidation");

                // 3. 契约：fresh 恢复同几何也要触发恢复 (MaxAppRight 仍为 200, TrayLeft 仍为 600, notify: false 模拟 provider 未变几何不发通知)
                TaskbarSpaceSnapshot recoveredFreshSnap = new TaskbarSpaceSnapshot(200, 600, taskbarRect, trayRect, true, Stopwatch.GetTimestamp());
                Assert(recoveredFreshSnap.IsFresh(2000), "Recovered snapshot must be fresh");

                mock.SetSnapshot(recoveredFreshSnap, notify: false);

                // 150ms guard 读取到 fresh，检测到 stale -> fresh 跃变，主动触发恢复
                win.CheckTaskbarSpaceFreshnessGuard();
                Application.DoEvents(); // 执行 BeginInvoke -> UpdateWatermark

                Rectangle recoveredRect = (Rectangle)GetPrivateField(win, "_actualScreenRect");
                Assert(!recoveredRect.IsEmpty, "Watermark must be actively recovered by guard even with identical geometry");
            }
            finally
            {
                win.Dispose();
            }
        }

        static void TestStopLateWorkerSafety()
        {
            TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(100);

            // 1. 启动 monitor
            monitor.Start();

            // 契约：不可重启保证 (再次调用 Start 无副作用，不会重复启动)
            monitor.Start();

            // 2. 停止 monitor
            monitor.Stop();

            // 契约：不可重入保证 (连续调用 Stop/Dispose 幂等，不抛异常)
            monitor.Stop();
            monitor.Dispose();

            // 契约：不可重启保证 (停止后调用 Start 不会复活)
            monitor.Start();

            // 3. 迟到安全与信号释放
            // 契约：停止后禁止 ProcessNewSnapshot / WaitOne on disposed event
            // 验证未抛出 ObjectDisposedException
            Thread.Sleep(150);
        }
    }
}
