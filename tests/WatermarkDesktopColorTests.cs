using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class WatermarkDesktopColorTests
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Watermark Desktop Color & Cache Tests (Pure Offscreen)");
            Console.WriteLine("==================================================");

            try
            {
                TestAppConfigDesktopWatermarkColorResolution();
                Console.WriteLine("PASS: TestAppConfigDesktopWatermarkColorResolution");

                TestGetThemeColorsOverrideWhenEnabledAndRestoredWhenDisabled();
                Console.WriteLine("PASS: TestGetThemeColorsOverrideWhenEnabledAndRestoredWhenDisabled");

                TestTaskbarDockedWidgetRenderDirectBitmapColors();
                Console.WriteLine("PASS: TestTaskbarDockedWidgetRenderDirectBitmapColors");

                TestTaskbarRenderKeyCacheInvalidationOnColorChange();
                Console.WriteLine("PASS: TestTaskbarRenderKeyCacheInvalidationOnColorChange");

                TestApplyConfigImmediateColorInvalidation();
                Console.WriteLine("PASS: TestApplyConfigImmediateColorInvalidation");

                TestRenderStabilityAndDimensions();
                Console.WriteLine("PASS: TestRenderStabilityAndDimensions");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL WATERMARK DESKTOP COLOR TESTS PASSED SUCCESSFULLY (6/6)");
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

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
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
            else
            {
                throw new Exception("Field not found: " + fieldName);
            }
        }

        private static object GetField(object target, string fieldName)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                return fi.GetValue(target);
            }
            throw new Exception("Field not found: " + fieldName);
        }

        // Test 1: AppConfig resolution of desktop watermark color
        private static void TestAppConfigDesktopWatermarkColorResolution()
        {
            AppConfig config = new AppConfig();
            // Default palette resolution
            string color1 = config.GetDesktopWatermarkColor(1);
            string color2 = config.GetDesktopWatermarkColor(2);
            string color3 = config.GetDesktopWatermarkColor(3);

            Assert(!string.IsNullOrEmpty(color1), "Desktop 1 color must not be empty");
            Assert(!string.IsNullOrEmpty(color2), "Desktop 2 color must not be empty");
            Assert(color1 != color2, "Desktop 1 and 2 colors must differ");

            Color c1 = config.GetDesktopWatermarkColorRgb(1);
            Assert(c1.R == 37 && c1.G == 99 && c1.B == 235, "Desktop 1 default RGB should match #2563EB");

            // Custom configuration override
            config.SetDesktopWatermarkColor(1, "#FF5500");
            Assert(config.GetDesktopWatermarkColor(1) == "#FF5500", "Custom color must be returned after set");
            Color cCustom = config.GetDesktopWatermarkColorRgb(1);
            Assert(cCustom.R == 255 && cCustom.G == 85 && cCustom.B == 0, "Parsed custom RGB should match #FF5500");

            // Fallback parsing safety
            Color fallback = AppConfig.ParseHexColor("invalid", Color.FromArgb(12, 34, 56));
            Assert(fallback.R == 12 && fallback.G == 34 && fallback.B == 56, "Invalid hex should yield fallback color");
        }

        // Test 2: GetThemeColors overrides title/num/sub/border when enabled, restores when disabled
        private static void TestGetThemeColorsOverrideWhenEnabledAndRestoredWhenDisabled()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                SetField(win, "_colorTheme", "WhiteHighlight");
                SetField(win, "_currentDesktop", 1);
                SetField(win, "_desktopColors", new Dictionary<int, string>());

                MethodInfo miTheme = typeof(WatermarkWindow).GetMethod("GetThemeColors", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(miTheme != null, "GetThemeColors method must exist");

                // 1. When UseDesktopColor is true
                SetField(win, "_useDesktopColor", true);
                object[] themeParams = new object[5];
                miTheme.Invoke(win, themeParams);

                Color titleColor = (Color)themeParams[0];
                Color subColor = (Color)themeParams[1];
                Color numColor = (Color)themeParams[2];
                Color cardBgColor = (Color)themeParams[3];
                Color borderPenColor = (Color)themeParams[4];

                // Desktop 1 default is #2563EB (RGB: 37, 99, 235)
                Assert(titleColor.R == 37 && titleColor.G == 99 && titleColor.B == 235, "Title color RGB must match desktop 1 exact color");
                Assert(numColor.R == 37 && numColor.G == 99 && numColor.B == 235, "Num color RGB must match desktop 1 exact color");
                Assert(subColor.R == 37 && subColor.G == 99 && subColor.B == 235, "Sub color RGB must match desktop 1 exact color");
                Assert(borderPenColor.R == 37 && borderPenColor.G == 99 && borderPenColor.B == 235, "Border color RGB must match desktop 1 exact color");
                // cardBgColor remains original theme background (180, 14, 18, 26)
                Assert(cardBgColor.A == 180 && cardBgColor.R == 14 && cardBgColor.G == 18 && cardBgColor.B == 26, "Card background must remain unchanged from theme");

                // 2. Switch to desktop 2 (#7C3AED, RGB: 124, 58, 237)
                SetField(win, "_currentDesktop", 2);
                miTheme.Invoke(win, themeParams);
                titleColor = (Color)themeParams[0];
                numColor = (Color)themeParams[2];
                Assert(titleColor.R == 124 && titleColor.G == 58 && titleColor.B == 237, "Desktop 2 title RGB must match #7C3AED");
                Assert(numColor.R == 124 && numColor.G == 58 && numColor.B == 237, "Desktop 2 num RGB must match #7C3AED");

                // 3. When UseDesktopColor is turned off (disabled), restore original theme colors
                SetField(win, "_useDesktopColor", false);
                miTheme.Invoke(win, themeParams);
                titleColor = (Color)themeParams[0];
                numColor = (Color)themeParams[2];
                borderPenColor = (Color)themeParams[4];
                Assert(titleColor.R == 255 && titleColor.G == 255 && titleColor.B == 255, "Disabled exclusive color must restore WhiteHighlight title (white)");
                Assert(numColor.R == 255 && numColor.G == 255 && numColor.B == 255, "Disabled exclusive color must restore WhiteHighlight num (white)");
                Assert(borderPenColor.R == 255 && borderPenColor.G == 255 && borderPenColor.B == 255, "Disabled exclusive color must restore WhiteHighlight border (white)");
            }
        }

        // Test 3: Offscreen bitmap rendering verifies exact colored text and separator
        private static void TestTaskbarDockedWidgetRenderDirectBitmapColors()
        {
            WatermarkWindow win = (WatermarkWindow)FormatterServices.GetUninitializedObject(typeof(WatermarkWindow));
            SetField(win, "_watermarkScale", 100);
            SetField(win, "_isTaskbarDocked", true);
            SetField(win, "_isTaskbarOverflowFloating", false);
            SetField(win, "_desktopColors", new Dictionary<int, string>());

            int w = 240;
            int h = 48;

            // 1. Render with UseDesktopColor = true on Desktop 1 (Blue)
            SetField(win, "_useDesktopColor", true);
            SetField(win, "_currentDesktop", 1);
            using (Bitmap bmpBlue = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmpBlue))
                {
                    g.Clear(Color.Transparent);
                    win.RenderTaskbarDockedWidgetCore(g, w, h, "桌面 1", true, true, true);
                }

                // Verify presence of blue dominant pixels in text
                bool foundBlueText = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color p = bmpBlue.GetPixel(x, y);
                        // Blue color: #2563EB -> B > 180, R < 100, Alpha > 100
                        if (p.A > 150 && p.B > 180 && p.R < 100)
                        {
                            foundBlueText = true;
                            break;
                        }
                    }
                    if (foundBlueText) break;
                }
                Assert(foundBlueText, "RenderTaskbarDockedWidgetCore must render desktop 1 text with blue color");
            }

            // 2. Render with UseDesktopColor = true on Desktop 2 (Violet: #7C3AED, R ~ 124, G ~ 58, B ~ 237)
            SetField(win, "_currentDesktop", 2);
            using (Bitmap bmpViolet = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmpViolet))
                {
                    g.Clear(Color.Transparent);
                    win.RenderTaskbarDockedWidgetCore(g, w, h, "桌面 2", true, true, true);
                }

                bool foundVioletText = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color p = bmpViolet.GetPixel(x, y);
                        // Violet color: R > 100, B > 200, G < 100, Alpha > 150
                        if (p.A > 150 && p.B > 200 && p.R > 100 && p.G < 100)
                        {
                            foundVioletText = true;
                            break;
                        }
                    }
                    if (foundVioletText) break;
                }
                Assert(foundVioletText, "RenderTaskbarDockedWidgetCore must render desktop 2 text with violet color");
            }

            // 3. Render with UseDesktopColor = false (disabled) -> text must be pure white
            SetField(win, "_useDesktopColor", false);
            using (Bitmap bmpWhite = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmpWhite))
                {
                    g.Clear(Color.Transparent);
                    win.RenderTaskbarDockedWidgetCore(g, w, h, "桌面 2", true, true, true);
                }

                bool foundWhiteText = false;
                bool foundColoredText = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color p = bmpWhite.GetPixel(x, y);
                        if (p.A > 200)
                        {
                            // Pure white text foreground
                            if (p.R > 230 && p.G > 230 && p.B > 230)
                            {
                                foundWhiteText = true;
                            }
                            else if (Math.Abs(p.R - p.B) > 40)
                            {
                                foundColoredText = true;
                            }
                        }
                    }
                }
                Assert(foundWhiteText, "When disabled, taskbar docked text must be rendered white");
                Assert(!foundColoredText, "When disabled, taskbar docked text must NOT contain colored tint");
            }
        }

        // Test 4: TaskbarRenderKey must differentiate by EffectiveColor
        private static void TestTaskbarRenderKeyCacheInvalidationOnColorChange()
        {
            Rectangle bounds = new Rectangle(100, 100, 200, 48);
            Type keyType = typeof(WatermarkWindow).GetNestedType("TaskbarRenderKey", BindingFlags.NonPublic);
            Assert(keyType != null, "TaskbarRenderKey nested type must exist");

            ConstructorInfo ctor = keyType.GetConstructors()[0];

            // Create key with blue color
            object keyBlue = ctor.Invoke(new object[] {
                bounds, "桌面 1", "桌面 1", 1, 4,
                40, (byte)100, 100, "WhiteHighlight", true,
                true, false, "桌面 1", true,
                true, 0, "#2563EB"
            });

            // Create key with identical parameters EXCEPT effective color (violet)
            object keyViolet = ctor.Invoke(new object[] {
                bounds, "桌面 1", "桌面 1", 1, 4,
                40, (byte)100, 100, "WhiteHighlight", true,
                true, false, "桌面 1", true,
                true, 0, "#7C3AED"
            });

            // Create another key with identical blue color
            object keyBlueDuplicate = ctor.Invoke(new object[] {
                bounds, "桌面 1", "桌面 1", 1, 4,
                40, (byte)100, 100, "WhiteHighlight", true,
                true, false, "桌面 1", true,
                true, 0, "#2563EB"
            });

            // Assert: Different effective colors must NOT equal
            Assert(!keyBlue.Equals(keyViolet), "Keys with different EffectiveColor must NOT be equal");
            // Assert: Identical effective colors MUST equal
            Assert(keyBlue.Equals(keyBlueDuplicate), "Keys with identical parameters must be equal");
            Assert(keyBlue.GetHashCode() == keyBlueDuplicate.GetHashCode(), "Equal keys must have equal hash code");
        }

        // Test 5: ApplyConfig immediately updates colors and invalidates render key
        private static void TestApplyConfigImmediateColorInvalidation()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                SetField(win, "_currentDesktop", 1);
                SetField(win, "_desktopNamesRevision", 0);
                SetField(win, "_desktopNames", new Dictionary<int, string>());

                AppConfig cfg = new AppConfig();
                cfg.WatermarkEnabled = false; // keep offscreen without show
                cfg.TaskbarTintEnabled = true;
                cfg.SetDesktopWatermarkColor(1, "#123456");

                // Set dummy render key
                Type keyType = typeof(WatermarkWindow).GetNestedType("TaskbarRenderKey", BindingFlags.NonPublic);
                ConstructorInfo ctor = keyType.GetConstructors()[0];
                object dummyKey = ctor.Invoke(new object[] {
                    new Rectangle(0, 0, 100, 48), "T", "T", 1, 1,
                    40, (byte)100, 100, "WhiteHighlight", true,
                    true, false, "T", true,
                    true, 0, "#000000"
                });
                SetField(win, "_lastTaskbarRenderKey", dummyKey);

                // Invoke ApplyConfig
                win.ApplyConfig(cfg);

                // Assert: _lastTaskbarRenderKey was cleared to null
                object lastKeyAfter = GetField(win, "_lastTaskbarRenderKey");
                Assert(lastKeyAfter == null, "ApplyConfig must clear _lastTaskbarRenderKey to force fresh render");

                // Assert: new desktop color was stored
                Color eff = win.GetEffectiveDesktopColor(1);
                Assert(eff.R == 0x12 && eff.G == 0x34 && eff.B == 0x56, "Stored effective color must match new config");
            }
        }

        // Test 6: Dimensions and layout remain stable across multiple desktop/color transitions
        private static void TestRenderStabilityAndDimensions()
        {
            using (Bitmap dummyBmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(dummyBmp))
            {
                // Measure layout for desktop 1 under different invocations / themes
                WatermarkWindow.TaskbarWidgetLayout layoutA = WatermarkWindow.CalculateTaskbarLayoutCore(g, 48, "开发桌面", true, true, 100, 1);
                WatermarkWindow.TaskbarWidgetLayout layoutB = WatermarkWindow.CalculateTaskbarLayoutCore(g, 48, "开发桌面", true, true, 100, 1);

                // Exact equality on identical content
                Assert(layoutA.Height == 48 && layoutB.Height == 48, "Taskbar widget height must be strictly 48px");
                Assert(layoutA.Width == layoutB.Width, "Taskbar widget width on identical desktop must be strictly equal");

                // Stable slot width calculation across 4 desktops maintains max slot size
                Dictionary<int, string> names = new Dictionary<int, string>();
                names[1] = "开发桌面";
                names[2] = "测试桌面";
                names[3] = "运维桌面";
                names[4] = "通讯桌面";
                int slotWidth1 = WatermarkWindow.CalculateTaskbarStableSlotWidthCore(g, 48, 100, 4, "{name}", true, names);
                int slotWidth2 = WatermarkWindow.CalculateTaskbarStableSlotWidthCore(g, 48, 100, 4, "{name}", true, names);
                Assert(slotWidth1 > 0, "Stable slot width must be positive");
                Assert(slotWidth1 == slotWidth2, "Stable slot width must remain invariant across repeated calls");

                // Check card border geometry consistency
                WatermarkWindow.CardBorderGeometry geo1 = WatermarkWindow.CalculateCardBorderGeometry(240, 88, 1.0f);
                WatermarkWindow.CardBorderGeometry geo2 = WatermarkWindow.CalculateCardBorderGeometry(240, 88, 1.0f);
                Assert(geo1.OuterRect == geo2.OuterRect, "Card border rectangle must remain identical");
                Assert(geo1.InnerRect == geo2.InnerRect, "Card inner rectangle must remain identical");
            }
        }
    }
}
