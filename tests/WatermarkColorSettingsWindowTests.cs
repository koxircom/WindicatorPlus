using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class WatermarkColorSettingsWindowTests
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
                    return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
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
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 1000, 1920, 48,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                TopMost = true;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }
        }

        private sealed class MockSpaceProvider : ITaskbarSpaceProvider
        {
            private int _maxAppRight;
            private int _trayLeft;
            private Rectangle _taskbarRect;
            private Rectangle _trayRect;
            private bool _isValid;

#pragma warning disable 0067
            public event Action<TaskbarSpaceSnapshot> SpaceChanged;
#pragma warning restore 0067

            public MockSpaceProvider(int maxAppRight, int trayLeft, Rectangle taskbarRect, Rectangle trayRect, bool isValid)
            {
                _maxAppRight = maxAppRight;
                _trayLeft = trayLeft;
                _taskbarRect = taskbarRect;
                _trayRect = trayRect;
                _isValid = isValid;
            }

            public TaskbarSpaceSnapshot GetLatestSnapshot()
            {
                return new TaskbarSpaceSnapshot(_maxAppRight, _trayLeft, _taskbarRect, _trayRect, _isValid, Stopwatch.GetTimestamp());
            }

            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Watermark Color & Settings Window Tests");
            Console.WriteLine("==================================================");

            // 契约安全守卫：必须检查当前 thread desktop name 是否前缀 WindicatorTest-，否则强制返回退出码 2
            // 契约红线：禁止在非隔离桌面运行窗口、禁止自建真实窗口干扰用户
            string desktopName = GetCurrentDesktopName();
            if (string.IsNullOrEmpty(desktopName) || !desktopName.StartsWith("WindicatorTest-", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SAFETY GUARD] WatermarkColorSettingsWindowTests requires an isolated desktop (prefix 'WindicatorTest-') to avoid disturbing user foreground window. Current desktop: " + desktopName);
                Console.WriteLine("[SAFETY GUARD] Execution rejected on default/non-test desktop. Exit code 2.");
                return 2;
            }

            try
            {
                TestWatermarkApplyConfigColorSwitchAndDesktop2ExclusiveColor();
                Console.WriteLine("PASS: TestWatermarkApplyConfigColorSwitchAndDesktop2ExclusiveColor");

                TestSettingsFormIsolationTabSwitchingAndCardBounds();
                Console.WriteLine("PASS: TestSettingsFormIsolationTabSwitchingAndCardBounds");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL WATERMARK COLOR & SETTINGS TESTS PASSED (2/2)");
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

        /// <summary>
        /// 测试 1: 在 fake shell + snapshot 下测试 ApplyConfig 颜色 #00DD88 -> #FF8844 同 desktop、开关 off/on，
        /// CaptureRenderedBitmapForTesting 实时取 bitmap 比 rgb/alpha 像素并保持 ActualScreenRect/固定 slot 不变；
        /// SetDesktop(2) 验证其专色（不切真实桌面）。生成 watermark-colors.png 图片。
        /// </summary>
        private static void TestWatermarkApplyConfigColorSwitchAndDesktop2ExclusiveColor()
        {
            Rectangle tbRect = new Rectangle(0, 1000, 1920, 48);
            Rectangle trRect = new Rectangle(1500, 1000, 420, 48);

            AppConfig config = new AppConfig();
            config.WatermarkEnabled = true;
            config.WatermarkPosition = "Taskbar";
            config.CustomY = 1000;
            config.WatermarkScale = 100;
            config.WatermarkOpacity = 100; // Exact RGB assertions must not use default 40% baked alpha.
            config.TitleTemplate = "{name}";
            config.ShowBigNumber = true;
            config.ShowBackgroundCard = false;
            config.TaskbarTintEnabled = true; // 开启专属色
            config.DesktopNames = new Dictionary<int, string>
            {
                { 1, "办公" },
                { 2, "研发" },
                { 3, "看板" }
            };

            // 1. 初始配置颜色：#00DD88 (青绿色: R=0, G=221, B=136)
            config.SetDesktopWatermarkColor(1, "#00DD88");
            config.SetDesktopWatermarkColor(2, "#7C3AED"); // 桌面2专色: #7C3AED (紫色: R=124, G=58, B=237)

            using (SyntheticShellForm fakeShell = new SyntheticShellForm())
            using (MockSpaceProvider spaceProvider = new MockSpaceProvider(500, trRect.Left, tbRect, trRect, true))
            using (WatermarkWindow watermark = new WatermarkWindow())
            {
                fakeShell.Show();
                watermark.HasTaskbarIntent = true;
                watermark.CaptureRenderedBitmapForTesting = true;
                watermark.SetTaskbarSpaceProvider(spaceProvider);
                watermark.ApplyConfig(config);
                watermark.CreateControl();
                watermark.Show();

                // 初始呈现桌面 1 (#00DD88)
                watermark.SetDesktop(1, 3, "办公");
                Application.DoEvents();

                Rectangle baseSlotRect = watermark.ActualScreenRect;
                Assert(!baseSlotRect.IsEmpty, "ActualScreenRect must not be empty after initial render");

                Bitmap bmpGreen = (Bitmap)watermark.LastRenderedBitmapForTest.Clone();
                Assert(bmpGreen != null, "Rendered bitmap must be captured for #00DD88");
                Assert(bmpGreen.Width == baseSlotRect.Width && bmpGreen.Height == baseSlotRect.Height, "Bitmap size must match ActualScreenRect");

                // 验证 #00DD88 像素特征 (G 占绝对主导: G > 140, G > R + 60, Alpha > 100)
                bool foundGreenPixel = false;
                for (int y = 0; y < bmpGreen.Height; y++)
                {
                    for (int x = 0; x < bmpGreen.Width; x++)
                    {
                        Color p = bmpGreen.GetPixel(x, y);
                        if (p.A > 100 && p.G > 140 && p.G > p.R + 60 && p.B > p.R)
                        {
                            foundGreenPixel = true;
                            break;
                        }
                    }
                    if (foundGreenPixel) break;
                }
                Assert(foundGreenPixel, "Bitmap must contain distinct green-cyan pixels corresponding to #00DD88");

                // 2. 颜色切换: #FF8844 (同桌面 1, 橙红色: R=255, G=136, B=68)
                config.SetDesktopWatermarkColor(1, "#FF8844");
                watermark.ApplyConfig(config);
                Application.DoEvents();

                // 保持 ActualScreenRect / 固定 slot 不变
                Assert(watermark.ActualScreenRect == baseSlotRect, "ActualScreenRect must remain unchanged across color switch #00DD88 -> #FF8844");

                Bitmap bmpOrange = (Bitmap)watermark.LastRenderedBitmapForTest.Clone();
                Assert(bmpOrange != null, "Rendered bitmap must be captured for #FF8844");

                // 验证 #FF8844 像素特征 (R 占绝对主导: R > 160, R > G + 40, R > B + 60, Alpha > 100)
                bool foundOrangePixel = false;
                for (int y = 0; y < bmpOrange.Height; y++)
                {
                    for (int x = 0; x < bmpOrange.Width; x++)
                    {
                        Color p = bmpOrange.GetPixel(x, y);
                        if (p.A > 100 && p.R > 160 && p.R > p.G + 40 && p.R > p.B + 60)
                        {
                            foundOrangePixel = true;
                            break;
                        }
                    }
                    if (foundOrangePixel) break;
                }
                Assert(foundOrangePixel, "Bitmap must contain distinct orange-red pixels corresponding to #FF8844");

                // 3. 开关测试: 关闭专色 (off)
                config.TaskbarTintEnabled = false;
                watermark.ApplyConfig(config);
                Application.DoEvents();

                Assert(watermark.ActualScreenRect == baseSlotRect, "ActualScreenRect must remain unchanged when exclusive color is turned off");
                Bitmap bmpOff = (Bitmap)watermark.LastRenderedBitmapForTest.Clone();
                Assert(bmpOff != null, "Rendered bitmap must be captured when exclusive color is off");

                // 关闭专色时，文本应恢复为 WhiteHighlight 主题色（纯白色，无明显偏色）
                bool foundWhitePixel = false;
                bool foundTintInOff = false;
                for (int y = 0; y < bmpOff.Height; y++)
                {
                    for (int x = 0; x < bmpOff.Width; x++)
                    {
                        Color p = bmpOff.GetPixel(x, y);
                        if (p.A > 180)
                        {
                            if (p.R > 200 && p.G > 200 && p.B > 200 && Math.Abs(p.R - p.B) < 25 && Math.Abs(p.G - p.B) < 25)
                            {
                                foundWhitePixel = true;
                            }
                            else if (p.R > p.G + 60 || p.G > p.R + 60)
                            {
                                foundTintInOff = true;
                            }
                        }
                    }
                }
                Assert(foundWhitePixel, "Bitmap must contain white text pixels when exclusive color is turned off");
                Assert(!foundTintInOff, "Bitmap must NOT contain strong colored tint when exclusive color is turned off");

                // 开关测试: 重新开启专色 (on)
                config.TaskbarTintEnabled = true;
                watermark.ApplyConfig(config);
                Application.DoEvents();

                Assert(watermark.ActualScreenRect == baseSlotRect, "ActualScreenRect must remain unchanged when exclusive color is turned back on");
                Bitmap bmpOn = watermark.LastRenderedBitmapForTest;
                Assert(bmpOn != null, "Rendered bitmap must be captured when exclusive color is turned on");
                bool foundOrangeAgain = false;
                for (int y = 0; y < bmpOn.Height; y++)
                {
                    for (int x = 0; x < bmpOn.Width; x++)
                    {
                        Color p = bmpOn.GetPixel(x, y);
                        if (p.A > 100 && p.R > 160 && p.R > p.G + 40 && p.R > p.B + 60)
                        {
                            foundOrangeAgain = true;
                            break;
                        }
                    }
                    if (foundOrangeAgain) break;
                }
                Assert(foundOrangeAgain, "Bitmap must restore orange tint when exclusive color is turned back on");

                // 4. SetDesktop(2) 验证桌面 2 的专色 (#7C3AED 紫色: R=124, G=58, B=237) (不切真实桌面)
                watermark.SetDesktop(2, 3, "研发");
                Application.DoEvents();

                Assert(watermark.ActualScreenRect == baseSlotRect, "ActualScreenRect must remain unchanged after SetDesktop(2)");
                Bitmap bmpViolet = (Bitmap)watermark.LastRenderedBitmapForTest.Clone();
                Assert(bmpViolet != null, "Rendered bitmap must be captured for Desktop 2 (#7C3AED)");

                // 验证紫色特征 (B > 160, R > 90, B > G + 50, Alpha > 100)
                bool foundVioletPixel = false;
                for (int y = 0; y < bmpViolet.Height; y++)
                {
                    for (int x = 0; x < bmpViolet.Width; x++)
                    {
                        Color p = bmpViolet.GetPixel(x, y);
                        if (p.A > 100 && p.B > 160 && p.R > 90 && p.B > p.G + 50)
                        {
                            foundVioletPixel = true;
                            break;
                        }
                    }
                    if (foundVioletPixel) break;
                }
                Assert(foundVioletPixel, "Bitmap must contain distinct violet pixels for Desktop 2 (#7C3AED)");

                // 5. 合成并保存 watermark-colors.png 到 C:/tmp/Windicator143
                string outDir = @"C:\tmp\Windicator143";
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "watermark-colors.png");

                int slotW = baseSlotRect.Width;
                int slotH = baseSlotRect.Height;
                int totalH = slotH * 4 + 40;
                using (Bitmap stripBmp = new Bitmap(slotW, totalH, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(stripBmp))
                    {
                        g.Clear(Color.FromArgb(24, 24, 28));
                        g.DrawImage(bmpGreen, 0, 0);
                        g.DrawImage(bmpOrange, 0, slotH + 10);
                        g.DrawImage(bmpOff, 0, (slotH + 10) * 2);
                        g.DrawImage(bmpViolet, 0, (slotH + 10) * 3);
                    }
                    stripBmp.Save(outPath, ImageFormat.Png);
                }
                bmpGreen.Dispose(); bmpOrange.Dispose(); bmpOff.Dispose(); bmpViolet.Dispose();
                Console.WriteLine("Saved watermark colors capture to: " + outPath);
            }
        }

        /// <summary>
        /// 测试 2: SettingsForm(config,wm,tray,null) 隔离 show，找水印颜色开关/无浓度 slider，
        /// Tab 往返 Position/Names/Watermark 用已有 SelectTab 反射（先查实际方法），
        /// 检查所有 3 个相邻 card bounds 无重叠(只同父 visible children)，所有非当前 panel 不可见。
        /// 不要触发保存/真实 ColorDialog。输出 settings.png。
        /// </summary>
        private static void TestSettingsFormIsolationTabSwitchingAndCardBounds()
        {
            AppConfig config = new AppConfig();
            config.WatermarkEnabled = true;
            config.WatermarkPosition = "Taskbar";
            config.TaskbarTintEnabled = true;
            config.SetDesktopWatermarkColor(1, "#00DD88");
            config.SetDesktopWatermarkColor(2, "#7C3AED");

            using (WatermarkWindow watermark = new WatermarkWindow())
            using (TrayIconManager tray = new TrayIconManager(config, watermark, null))
            using (SettingsForm form = new SettingsForm(config, watermark, tray, null))
            {
                // 契约红线：禁止真实保存，禁止弹窗干扰桌面
                form.SuppressPersistenceForTesting = true;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();

                // 1. 查找水印颜色开关与确认无浓度 slider
                FieldInfo fiSwTint = typeof(SettingsForm).GetField("_swTaskbarTint", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(fiSwTint != null, "_swTaskbarTint field must exist on SettingsForm");
                DarkToggleSwitch swTint = fiSwTint.GetValue(form) as DarkToggleSwitch;
                Assert(swTint != null, "_swTaskbarTint must be an instance of DarkToggleSwitch");

                // 获取所属卡片 (cardTint)
                Control cardTint = swTint.Parent;
                while (cardTint != null && !(cardTint is DarkCardPanel))
                {
                    cardTint = cardTint.Parent;
                }
                Assert(cardTint != null, "Parent card (DarkCardPanel) of _swTaskbarTint must exist");

                // 遍历 cardTint 控件树，断言无任何 TrackBar (无浓度 slider)
                AssertNoTrackBarRecursive(cardTint);

                // 2. Tab 往返 Position/Names/Watermark 反射方法（先查实际方法）
                MethodInfo miSelectTab = typeof(SettingsForm).GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                      ?? typeof(SettingsForm).GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert(miSelectTab != null, "SettingsForm must have SelectTab or SwitchTab method");
                Console.WriteLine("Tab switching method resolved: " + miSelectTab.Name);

                FieldInfo fiScrollers = typeof(SettingsForm).GetField("_pageScrollers", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(fiScrollers != null, "_pageScrollers field must exist on SettingsForm");
                var pageScrollers = fiScrollers.GetValue(form) as System.Collections.IDictionary;
                Assert(pageScrollers != null, "_pageScrollers must be valid dictionary");

                FieldInfo fiRightPanel = typeof(SettingsForm).GetField("_rightContentPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(fiRightPanel != null, "_rightContentPanel field must exist on SettingsForm");

                // 往返切换序列: Position -> Names -> Watermark -> Position -> Watermark -> Names -> Position
                string[] tabSequence = new string[] { "Position", "Names", "Watermark", "Position", "Watermark", "Names", "Position" };

                foreach (string tabKey in tabSequence)
                {
                    miSelectTab.Invoke(form, new object[] { tabKey });
                    Application.DoEvents();

                    Panel activePanel = fiRightPanel.GetValue(form) as Panel;
                    Assert(activePanel != null, "Active right content panel must not be null");
                    Assert(activePanel.Visible, string.Format("Active panel for tab [{0}] must be visible", tabKey));

                    // 检查所有非当前 panel 不可见
                    foreach (System.Collections.DictionaryEntry entry in pageScrollers)
                    {
                        Panel p = entry.Value as Panel;
                        if (p != null && p != activePanel)
                        {
                            Assert(!p.Visible, string.Format("Non-current page panel [{0}] must NOT be visible on tab [{1}]", p.Name, tabKey));
                        }
                    }

                    // 检查当前页面所有同父 visible children (相邻 card bounds 无重叠)
                    CheckAdjacentChildrenBoundsNoOverlap(activePanel, tabKey);
                }

                // 3. 在 Position 页面特别校验 3 个直接相邻 card 的 bounds 无重叠
                miSelectTab.Invoke(form, new object[] { "Position" });
                Application.DoEvents();
                Panel posPanel = fiRightPanel.GetValue(form) as Panel;
                Assert(posPanel != null, "Position panel must be active");
                CheckPositionThreeCardsNoOverlap(posPanel);

                // 4. DrawToBitmap 输出 C:/tmp/Windicator143/settings.png (仅隔离桌面图片)
                string outDir = @"C:\tmp\Windicator143";
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                string settingsPath = Path.Combine(outDir, "settings.png");
                using (Bitmap bmpForm = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmpForm, new Rectangle(0, 0, form.Width, form.Height));
                    bmpForm.Save(settingsPath, ImageFormat.Png);
                }
                Console.WriteLine("Saved settings form capture to: " + settingsPath);
            }
        }

        private static void AssertNoTrackBarRecursive(Control parent)
        {
            if (parent is TrackBar)
            {
                throw new Exception("Unexpected TrackBar (slider) found in exclusive color card: " + parent.Name);
            }
            foreach (Control child in parent.Controls)
            {
                AssertNoTrackBarRecursive(child);
            }
        }

        private static void CheckPositionThreeCardsNoOverlap(Panel pagePanel)
        {
            // Position 页面的直接子控件是 TableLayoutPanel root，其包含 3 个 card:
            // 1. 任务栏停靠设计
            // 2. 水印交互与按键穿透
            // 3. 水印桌面专属颜色 (随虚拟桌面变色)
            Control container = (pagePanel.Controls.Count > 0) ? pagePanel.Controls[0] : pagePanel;
            List<Control> visibleCards = new List<Control>();
            foreach (Control c in container.Controls)
            {
                if (c.Visible && c is DarkCardPanel)
                {
                    visibleCards.Add(c);
                }
            }

            Assert(visibleCards.Count == 3, "Position page must contain exactly 3 visible DarkCardPanel instances, found: " + visibleCards.Count);

            // 按 Y 坐标排序
            visibleCards.Sort((a, b) => a.Top.CompareTo(b.Top));

            for (int i = 0; i < visibleCards.Count - 1; i++)
            {
                Control c1 = visibleCards[i];
                Control c2 = visibleCards[i + 1];

                Assert(c2.Top >= c1.Bottom, string.Format("Adjacent card bounds overlap! Card [{0}] (bottom={1}) overlaps with Card [{2}] (top={3})",
                    c1.Text, c1.Bottom, c2.Text, c2.Top));
                Assert(!c1.Bounds.IntersectsWith(c2.Bounds), string.Format("Adjacent cards intersect: [{0}] ({1}) and [{2}] ({3})",
                    c1.Text, c1.Bounds, c2.Text, c2.Bounds));
            }
        }

        private static void CheckAdjacentChildrenBoundsNoOverlap(Control parent, string tabKey)
        {
            Control container = (parent.Controls.Count > 0) ? parent.Controls[0] : parent;
            List<Control> visibleChildren = new List<Control>();
            foreach (Control c in container.Controls)
            {
                if (c.Visible) visibleChildren.Add(c);
            }

            if (visibleChildren.Count > 1)
            {
                visibleChildren.Sort((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
                for (int i = 0; i < visibleChildren.Count - 1; i++)
                {
                    Control c1 = visibleChildren[i];
                    Control c2 = visibleChildren[i + 1];

                    // 仅当同处于一列或水平重合时检查垂直相邻无重合
                    if (c1.Left < c2.Right && c1.Right > c2.Left)
                    {
                        Assert(c2.Top >= c1.Bottom, string.Format("Tab [{0}] container [{1}]: child [{2}] (bottom={3}) overlaps vertically with child [{4}] (top={5})",
                            tabKey, container.GetType().Name, c1.GetType().Name, c1.Bottom, c2.GetType().Name, c2.Top));
                    }
                    Assert(!c1.Bounds.IntersectsWith(c2.Bounds), string.Format("Tab [{0}] container [{1}]: children intersect: [{2}] ({3}) and [{4}] ({5})",
                        tabKey, container.GetType().Name, c1.GetType().Name, c1.Bounds, c2.GetType().Name, c2.Bounds));
                }
            }
        }
    }
}
