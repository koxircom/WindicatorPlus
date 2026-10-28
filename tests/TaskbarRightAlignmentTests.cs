using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.Serialization;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class TaskbarRightAlignmentTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] " + message);
                Console.ResetColor();
                throw new Exception("Assertion Failed: " + message);
            }
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                fi.SetValue(target, value);
            }
        }

        private static int FindLastColoredPixelX(Bitmap bmp)
        {
            for (int x = bmp.Width - 1; x >= 0; x--)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 20 && (c.R > 20 || c.G > 20 || c.B > 20))
                    {
                        return x;
                    }
                }
            }
            return -1;
        }

        private static int FindFirstColoredPixelX(Bitmap bmp)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 20 && (c.R > 20 || c.G > 20 || c.B > 20))
                    {
                        return x;
                    }
                }
            }
            return -1;
        }

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Right Alignment Tests");
            Console.WriteLine("==================================================");

            try
            {
                TestPureOffsetCalculation();
                Console.WriteLine("PASS: 1. TestPureOffsetCalculation");

                TestNamesRightAlignmentInSlotBitmap();
                Console.WriteLine("PASS: 2. TestNamesRightAlignmentInSlotBitmap (Ai, 办公, 看板)");

                TestSameWidthNoShift();
                Console.WriteLine("PASS: 3. TestSameWidthNoShift");

                TestOverflowFloatingAndDesktopNoShift();
                Console.WriteLine("PASS: 4. TestOverflowFloatingAndDesktopNoShift");

                TestRelativeSpacingPreservation();
                Console.WriteLine("PASS: 5. TestRelativeSpacingPreservation");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("==================================================");
                Console.WriteLine("ALL 5 TASKBAR RIGHT ALIGNMENT TESTS PASSED SUCCESFULLY!");
                Console.WriteLine("==================================================");
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] " + ex.Message);
                Console.ResetColor();
                return 1;
            }
        }

        /// <summary>
        /// 1. 纯 layout 位移函数无窗口测试：
        /// 验证 CalculateTaskbarContentHorizontalOffset 对非 overflow 实际任务栏、溢出浮动、桌面模式的偏移量计算正确。
        /// </summary>
        static void TestPureOffsetCalculation()
        {
            // Case A: 实际任务栏非 overflow
            float offset1 = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 75, true);
            Assert(offset1 == 45f, "Slot 120 with natural 75 should shift right by 45");

            float offset2 = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 120, true);
            Assert(offset2 == 0f, "Slot 120 with natural 120 (same width) should not shift");

            float offset3 = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 140, true);
            Assert(offset3 == 0f, "Slot 120 with natural 140 (overflow width) should not shift negatively");

            // Case B: 非实际任务栏或 overflow 浮动状态
            float offset4 = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 75, false);
            Assert(offset4 == 0f, "Non-taskbar docked mode should never shift");

            // Case C: 重载版本 (targetWidth, layoutWidth, isTaskbarDocked, isTaskbarOverflowFloating)
            float offsetDockedNormal = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 88, true, false);
            Assert(offsetDockedNormal == 32f, "Docked normal taskbar should shift right by 120 - 88 = 32");

            float offsetOverflowFloating = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 88, true, true);
            Assert(offsetOverflowFloating == 0f, "Taskbar overflow floating card should not shift");

            float offsetDesktop = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, 88, false, false);
            Assert(offsetDesktop == 0f, "Desktop watermark should not shift");
        }

        /// <summary>
        /// 2. 真实离屏 Bitmap 像素级几何与右对齐验收：
        /// 针对 "Ai", "办公", "看板"，在 120 slot 比较同 name 自然 width 与 slot 最后有色像素位置差，
        /// 断言该差值严格等于空槽差 (w - naturalWidth)，验证短名不留槽内右空白且不越界。
        /// 使用 FormatterServices.GetUninitializedObject 反射实例化，完全无 Form Handle / 无桌面输入。
        /// </summary>
        static void TestNamesRightAlignmentInSlotBitmap()
        {
            WatermarkWindow win = (WatermarkWindow)FormatterServices.GetUninitializedObject(typeof(WatermarkWindow));
            SetField(win, "_watermarkScale", 100);
            SetField(win, "_currentDesktop", 1);
            SetField(win, "_showBigNumber", true);
            SetField(win, "_isTaskbarDocked", true);
            SetField(win, "_isTaskbarOverflowFloating", false);

            string[] testNames = new string[] { "Ai", "办公", "看板" };
            int slotWidth = 120;
            int tbHeight = 48;

            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics gMeasure = Graphics.FromImage(measureBmp))
            {
                gMeasure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                foreach (string name in testNames)
                {
                    WatermarkWindow.TaskbarWidgetLayout layout = WatermarkWindow.CalculateTaskbarLayoutCore(
                        gMeasure, tbHeight, name, true, true, 100, 1);
                    int naturalWidth = layout.Width;
                    int emptySlotDiff = Math.Max(0, slotWidth - naturalWidth);

                    // 1) 自然宽度渲染
                    int lastNatural;
                    using (Bitmap bmpNatural = new Bitmap(naturalWidth, tbHeight, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmpNatural))
                    {
                        g.Clear(Color.Transparent);
                        win.RenderTaskbarDockedWidgetCore(g, naturalWidth, tbHeight, name, true, true, true);
                        lastNatural = FindLastColoredPixelX(bmpNatural);
                    }

                    // 2) 120 slot 宽度渲染
                    int lastSlot;
                    using (Bitmap bmpSlot = new Bitmap(slotWidth, tbHeight, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmpSlot))
                    {
                        g.Clear(Color.Transparent);
                        win.RenderTaskbarDockedWidgetCore(g, slotWidth, tbHeight, name, true, true, true);
                        lastSlot = FindLastColoredPixelX(bmpSlot);
                    }

                    Assert(lastNatural > 0, string.Format("Natural render for '{0}' must produce colored pixels", name));
                    Assert(lastSlot > 0, string.Format("Slot render for '{0}' must produce colored pixels", name));

                    // 3) 契约几何验证：最后有色像素位置差必须严格等于空槽差
                    int actualDiff = lastSlot - lastNatural;
                    Console.WriteLine(string.Format("   [BITMAP CHECK] '{0}': naturalW={1}, slotW={2}, emptyDiff={3}, lastNat={4}, lastSlot={5}, actualDiff={6}",
                        name, naturalWidth, slotWidth, emptySlotDiff, lastNatural, lastSlot, actualDiff));

                    Assert(actualDiff == emptySlotDiff,
                        string.Format("For name '{0}', position diff ({1}) must equal empty slot diff ({2})",
                            name, actualDiff, emptySlotDiff));

                    // 4) 验证不越界
                    Assert(lastSlot < slotWidth,
                        string.Format("For name '{0}', last pixel ({1}) must be strictly within slot width ({2})",
                            name, lastSlot, slotWidth));

                    // 5) 验证短名在槽内不再停留在左侧（若存在空槽差，最后像素必须右移）
                    if (emptySlotDiff > 0)
                    {
                        Assert(lastSlot > lastNatural,
                            string.Format("For short name '{0}', content must shift right to eliminate right blank in slot", name));
                    }
                }
            }
        }

        /// <summary>
        /// 3. 相同宽不移测试：
        /// 当传入的目标宽度 w 等于 naturalWidth 时，偏移量为 0，最后像素位置完全相同。
        /// </summary>
        static void TestSameWidthNoShift()
        {
            WatermarkWindow win = (WatermarkWindow)FormatterServices.GetUninitializedObject(typeof(WatermarkWindow));
            SetField(win, "_watermarkScale", 100);
            SetField(win, "_currentDesktop", 1);
            SetField(win, "_showBigNumber", true);
            SetField(win, "_isTaskbarDocked", true);
            SetField(win, "_isTaskbarOverflowFloating", false);

            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics gMeasure = Graphics.FromImage(measureBmp))
            {
                gMeasure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                WatermarkWindow.TaskbarWidgetLayout layout = WatermarkWindow.CalculateTaskbarLayoutCore(
                    gMeasure, 48, "Ai", true, true, 100, 1);
                int naturalWidth = layout.Width;

                using (Bitmap bmp1 = new Bitmap(naturalWidth, 48, PixelFormat.Format32bppArgb))
                using (Graphics g1 = Graphics.FromImage(bmp1))
                using (Bitmap bmp2 = new Bitmap(naturalWidth, 48, PixelFormat.Format32bppArgb))
                using (Graphics g2 = Graphics.FromImage(bmp2))
                {
                    g1.Clear(Color.Transparent);
                    g2.Clear(Color.Transparent);

                    win.RenderTaskbarDockedWidgetCore(g1, naturalWidth, 48, "Ai", true, true, true);
                    win.RenderTaskbarDockedWidgetCore(g2, naturalWidth, 48, "Ai", true, true, false);

                    int last1 = FindLastColoredPixelX(bmp1);
                    int last2 = FindLastColoredPixelX(bmp2);

                    Assert(last1 == last2, "When slot width equals natural width, position must not shift at all");
                }
            }
        }

        /// <summary>
        /// 4. 浮动和桌面模式不偏移测试：
        /// 验证当处于 overflow floating 或桌面水印时，即使画布宽度大于 naturalWidth，内容依然靠原点渲染不向右偏移。
        /// </summary>
        static void TestOverflowFloatingAndDesktopNoShift()
        {
            WatermarkWindow win = (WatermarkWindow)FormatterServices.GetUninitializedObject(typeof(WatermarkWindow));
            SetField(win, "_watermarkScale", 100);
            SetField(win, "_currentDesktop", 1);
            SetField(win, "_showBigNumber", true);

            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics gMeasure = Graphics.FromImage(measureBmp))
            {
                gMeasure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                WatermarkWindow.TaskbarWidgetLayout layout = WatermarkWindow.CalculateTaskbarLayoutCore(
                    gMeasure, 48, "Ai", true, true, 100, 1);
                int naturalWidth = layout.Width;

                // 自然渲染
                int naturalLast;
                using (Bitmap bmpNat = new Bitmap(naturalWidth, 48, PixelFormat.Format32bppArgb))
                using (Graphics gNat = Graphics.FromImage(bmpNat))
                {
                    gNat.Clear(Color.Transparent);
                    win.RenderTaskbarDockedWidgetCore(gNat, naturalWidth, 48, "Ai", true, true, false);
                    naturalLast = FindLastColoredPixelX(bmpNat);
                }

                // 传入 120 宽度但 isNonOverflowTaskbar = false
                int nonOverflowLast;
                using (Bitmap bmpNon = new Bitmap(120, 48, PixelFormat.Format32bppArgb))
                using (Graphics gNon = Graphics.FromImage(bmpNon))
                {
                    gNon.Clear(Color.Transparent);
                    win.RenderTaskbarDockedWidgetCore(gNon, 120, 48, "Ai", true, true, false);
                    nonOverflowLast = FindLastColoredPixelX(bmpNon);
                }

                Assert(nonOverflowLast == naturalLast,
                    "In overflow floating or desktop mode, content must stay at natural coordinates without right shift");
            }
        }

        /// <summary>
        /// 5. 内部相对最小间距保持测试：
        /// 验证整体平移后，数字、分隔符和标题文字的内部相对间距严格保持不变。
        /// </summary>
        static void TestRelativeSpacingPreservation()
        {
            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(measureBmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                WatermarkWindow.TaskbarWidgetLayout layout = WatermarkWindow.CalculateTaskbarLayoutCore(
                    g, 48, "Ai", true, true, 100, 1);

                float naturalSepDelta = layout.SepX - layout.NumX;
                float naturalTitleDelta = layout.TitleX - layout.SepX;

                float offset = WatermarkWindow.CalculateTaskbarContentHorizontalOffset(120, layout.Width, true);
                float shiftedNumX = layout.NumX + offset;
                float shiftedSepX = layout.SepX + offset;
                float shiftedTitleX = layout.TitleX + offset;

                float shiftedSepDelta = shiftedSepX - shiftedNumX;
                float shiftedTitleDelta = shiftedTitleX - shiftedSepX;

                Assert(Math.Abs(naturalSepDelta - shiftedSepDelta) < 0.001f,
                    "Relative spacing between number and separator must remain identical after shift");
                Assert(Math.Abs(naturalTitleDelta - shiftedTitleDelta) < 0.001f,
                    "Relative spacing between separator and title must remain identical after shift");
            }
        }
    }
}
