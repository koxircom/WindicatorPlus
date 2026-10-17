using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace WindicatorPlus.Tests
{
    public class SettingsLayoutTests
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Settings Layout & DPI Acceptance");
            Console.WriteLine("==================================================");

            try
            {
                TestWindowInitialSizeFitsWorkingArea();
                Console.WriteLine("PASS: TestWindowInitialSizeFitsWorkingArea");

                TestSixPagesSwitchingIsolationWithReturn();
                Console.WriteLine("PASS: TestSixPagesSwitchingIsolationWithReturn");

                TestNoLeafSiblingOverlapsOnAllSevenSteps();
                Console.WriteLine("PASS: TestNoLeafSiblingOverlapsOnAllSevenSteps");

                TestControlsContainedWithinContainers();
                Console.WriteLine("PASS: TestControlsContainedWithinContainers");

                TestAutoScrollReachabilityAndBottomCapture();
                Console.WriteLine("PASS: TestAutoScrollReachabilityAndBottomCapture");

                TestExtremeConfigClampNoException();
                Console.WriteLine("PASS: TestExtremeConfigClampNoException");

                TestMultiDpiSimulation();
                Console.WriteLine("PASS: TestMultiDpiSimulation");

                TestZeroPersistenceSideEffects();
                Console.WriteLine("PASS: TestZeroPersistenceSideEffects");

                TestFullSixPagesBitmapCapture();
                Console.WriteLine("PASS: TestFullSixPagesBitmapCapture");

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL SETTINGS LAYOUT & DPI ACCEPTANCE TESTS PASSED");
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

        private static SettingsForm CreateTestForm(AppConfig customConfig = null)
        {
            AppConfig config = customConfig ?? new AppConfig();
            if (customConfig == null)
            {
                config.WatermarkScale = 100;
                config.WatermarkOpacity = 40;
                config.TitleTemplate = "{name}";
                config.SubtitleTemplate = "虚拟桌面 {n} (共 {total} 个)";
            }

            SettingsForm form = new SettingsForm(config, null, null, null);
            form.SuppressPersistenceForTesting = true;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            return form;
        }

        private static void InvokeSwitchTab(SettingsForm form, string tabKey)
        {
            MethodInfo mi = typeof(SettingsForm).GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(mi != null, "SwitchTab method must exist on SettingsForm");
            mi.Invoke(form, new object[] { tabKey });
            Application.DoEvents();
        }

        private static Panel GetRightContentPanel(SettingsForm form)
        {
            FieldInfo fi = typeof(SettingsForm).GetField("_rightContentPanel", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(fi != null, "_rightContentPanel field must exist");
            return (Panel)fi.GetValue(form);
        }

        private static Control GetViewField(SettingsForm form, string fieldName)
        {
            FieldInfo fi = typeof(SettingsForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(fi != null, fieldName + " field must exist");
            return (Control)fi.GetValue(form);
        }

        private static void CaptureSettingsBitmap(SettingsForm form, string fileName)
        {
            string outDir = @"C:\tmp\WindicatorAcceptance";
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string outPath = Path.Combine(outDir, fileName);
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("[SCREENSHOT] Saved: " + outPath);
        }

        // Test 1: Window Initial Size & Real DPI Benchmark
        private static void TestWindowInitialSizeFitsWorkingArea()
        {
            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Assert(form.Width <= workArea.Width, "Form width (" + form.Width + ") must fit inside WorkingArea (" + workArea.Width + ")");
                Assert(form.Height <= workArea.Height, "Form height (" + form.Height + ") must fit inside WorkingArea (" + workArea.Height + ")");
                Assert(form.AutoScaleMode == AutoScaleMode.Dpi, "Form.AutoScaleMode must be AutoScaleMode.Dpi");

                // In PerMonitorV2 mode with manifest, AutoScaleDimensions is updated to current monitor DPI (e.g. 144 for 150%)
                Assert(form.CurrentAutoScaleDimensions.Width > 0 && form.CurrentAutoScaleDimensions.Height > 0, "CurrentAutoScaleDimensions must be positive");
                Assert(Math.Abs(form.AutoScaleDimensions.Width - form.CurrentAutoScaleDimensions.Width) < 0.01F,
                    string.Format("Form.AutoScaleDimensions ({0}) must match CurrentAutoScaleDimensions ({1})",
                        form.AutoScaleDimensions.Width, form.CurrentAutoScaleDimensions.Width));

                // Baseline is 96 DPI design; on 150% display it scales to 144 DPI
                Assert(form.CurrentAutoScaleDimensions.Width >= 96F, "CurrentAutoScaleDimensions must be at least baseline 96 DPI");

                form.Close();
            }
        }

        // Test 2: Six pages switching isolation including return step (General->Watermark->Position->Names->System->About->Watermark)
        private static void TestSixPagesSwitchingIsolationWithReturn()
        {
            string[] tabs = new string[] { "General", "Watermark", "Position", "Names", "System", "About", "Watermark" };
            string[] viewFields = new string[] { "_viewGeneral", "_viewWatermark", "_viewPosition", "_viewNames", "_viewSystem", "_viewAbout", "_viewWatermark" };

            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                Assert(!form.TopMost, "Settings must be a normal non-topmost window");

                for (int i = 0; i < tabs.Length; i++)
                {
                    string tabKey = tabs[i];
                    InvokeSwitchTab(form, tabKey);

                    Panel contentPanel = GetRightContentPanel(form);
                    Control activeView = GetViewField(form, viewFields[i]);
                    Assert(activeView != null, "Active view " + viewFields[i] + " must exist");
                    Assert(activeView.Visible, "Active view " + tabKey + " must be Visible == true");
                    Assert(contentPanel.Controls.Contains(activeView), "Content panel must contain active view for " + tabKey);

                    int visibleViewsCount = 0;
                    foreach (Control c in contentPanel.Controls)
                    {
                        if (c.Visible) visibleViewsCount++;
                    }
                    Assert(visibleViewsCount == 1, "Only the active view must be Visible in content panel (count == 1)");

                    // Verify other views are Visible == false in memory
                    string[] allViewFields = new string[] { "_viewGeneral", "_viewWatermark", "_viewPosition", "_viewNames", "_viewSystem", "_viewAbout" };
                    foreach (string field in allViewFields)
                    {
                        if (field == viewFields[i]) continue;
                        Control inactiveView = GetViewField(form, field);
                        if (inactiveView != null)
                        {
                            Assert(!inactiveView.Visible, "Inactive view " + field + " must be Visible == false");
                        }
                    }
                }

                form.Close();
            }
        }

        // Test 3: No leaf sibling overlaps on all seven steps (including return to Watermark)
        private static void TestNoLeafSiblingOverlapsOnAllSevenSteps()
        {
            string[] tabs = new string[] { "General", "Watermark", "Position", "Names", "System", "About", "Watermark" };

            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                for (int s = 0; s < tabs.Length; s++)
                {
                    string tab = tabs[s];
                    InvokeSwitchTab(form, tab);
                    CheckNoOverlapsRecursive(form, tab + "(Step" + (s + 1) + ")");
                }

                form.Close();
            }
        }

        private static void CheckNoOverlapsRecursive(Control parent, string tabContext)
        {
            if (parent.Controls.Count > 1)
            {
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c1 = parent.Controls[i];
                    if (!c1.Visible) continue;

                    for (int j = i + 1; j < parent.Controls.Count; j++)
                    {
                        Control c2 = parent.Controls[j];
                        if (!c2.Visible) continue;

                        if (c1.Bounds.IntersectsWith(c2.Bounds))
                        {
                            throw new Exception(string.Format(
                                "Overlap collision detected on tab [{0}] in container [{1}]: {2} (Bounds={3}, Text='{4}') collides with {5} (Bounds={6}, Text='{7}')",
                                tabContext,
                                parent.GetType().Name,
                                c1.GetType().Name,
                                c1.Bounds,
                                c1.Text,
                                c2.GetType().Name,
                                c2.Bounds,
                                c2.Text));
                        }
                    }
                }
            }

            foreach (Control child in parent.Controls)
            {
                if (child.Visible)
                {
                    CheckNoOverlapsRecursive(child, tabContext);
                }
            }
        }

        // Test 4: Controls contained within parent containers, label no-truncation, page header no vertical stacking
        private static void TestControlsContainedWithinContainers()
        {
            string[] tabs = new string[] { "General", "Watermark", "Position", "Names", "System", "About", "Watermark" };

            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                foreach (string tab in tabs)
                {
                    InvokeSwitchTab(form, tab);

                    // Check page header geometry
                    FieldInfo fiHeader = typeof(SettingsForm).GetField("_lblPageTitle", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fiHeader != null)
                    {
                        Label lblHeader = (Label)fiHeader.GetValue(form);
                        if (lblHeader != null && lblHeader.Visible)
                        {
                            Assert(lblHeader.Width > lblHeader.Height, "Page header must be horizontal, not narrow vertical stacked: " + lblHeader.Text);
                            Assert(lblHeader.Height <= lblHeader.Font.Height * 2.2f, "Page header height should not wrap into vertical rows");
                        }
                    }

                    CheckControlsContainedRecursive(form, tab);
                }

                form.Close();
            }
        }

        private static void CheckControlsContainedRecursive(Control parent, string tabContext)
        {
            bool isScrollContainer = (parent is ScrollableControl && ((ScrollableControl)parent).AutoScroll);

            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;

                Assert(child.Left >= 0, string.Format(
                    "Control {0} on tab [{1}] has negative Left ({2}) in parent {3}",
                    child.GetType().Name, tabContext, child.Left, parent.GetType().Name));

                Assert(child.Top >= 0, string.Format(
                    "Control {0} on tab [{1}] has negative Top ({2}) in parent {3}",
                    child.GetType().Name, tabContext, child.Top, parent.GetType().Name));

                // Right boundary check (allow 1px tolerance for DPI rounding)
                Assert(child.Right <= parent.ClientSize.Width + 1, string.Format(
                    "Control {0} (Text='{1}') on tab [{2}] overflows parent {3} right boundary: Right={4}, ParentClientWidth={5}",
                    child.GetType().Name, child.Text, tabContext, parent.GetType().Name, child.Right, parent.ClientSize.Width));

                // Bottom boundary check (scroll containers intentionally allow vertical overflow for scrolling)
                if (!isScrollContainer)
                {
                    Assert(child.Bottom <= parent.ClientSize.Height + 1, string.Format(
                        "Control {0} (Text='{1}') on tab [{2}] overflows parent {3} bottom boundary: Bottom={4}, ParentClientHeight={5}",
                        child.GetType().Name, child.Text, tabContext, parent.GetType().Name, child.Bottom, parent.ClientSize.Height));
                }

                // Label truncation check: label width/height must satisfy preferred size
                if (child is Label)
                {
                    Label lbl = (Label)child;
                    if (!string.IsNullOrEmpty(lbl.Text))
                    {
                        if (!lbl.Text.Contains("\n"))
                        {
                            Size pref = lbl.GetPreferredSize(Size.Empty);
                            Assert(lbl.Width >= pref.Width - 3, string.Format(
                                "Single-line label truncated on tab [{0}]: Text='{1}', Width={2}, PreferredWidth={3}",
                                tabContext, lbl.Text, lbl.Width, pref.Width));
                        }
                        else
                        {
                            Size pref = lbl.GetPreferredSize(new Size(lbl.Width, 0));
                            Assert(lbl.Height >= pref.Height - 3, string.Format(
                                "Multi-line label truncated on tab [{0}]: Text='{1}', Height={2}, PreferredHeight={3}",
                                tabContext, lbl.Text, lbl.Height, pref.Height));
                        }
                    }
                }

                // Toggle Switch geometry check
                if (child is DarkToggleSwitch)
                {
                    DarkToggleSwitch sw = (DarkToggleSwitch)child;
                    Assert(sw.Width >= 50, "DarkToggleSwitch width must be >= 50px");
                    Assert(sw.Height >= 24, "DarkToggleSwitch height must be >= 24px");
                }

                CheckControlsContainedRecursive(child, tabContext);
            }
        }

        // Test 5: AutoScroll Reachability and Watermark Bottom Control Reachable
        private static void TestAutoScrollReachabilityAndBottomCapture()
        {
            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                Panel contentPanel = GetRightContentPanel(form);
                Assert(contentPanel.AutoScroll, "Content panel must have AutoScroll enabled");

                InvokeSwitchTab(form, "Watermark");
                Control watermarkView = GetViewField(form, "_viewWatermark");
                Assert(watermarkView != null, "Watermark view must exist");

                // Ensure bottom control is reachable
                int totalContentHeight = watermarkView.Height;
                Assert(totalContentHeight > 200, "View content height must be properly measured (>200px), actual: " + totalContentHeight);

                // Find bottommost leaf control
                List<Control> leaves = new List<Control>();
                CollectLeafControls(watermarkView, leaves);
                Assert(leaves.Count > 0, "Watermark view must have leaf controls");

                Control bottomLeaf = leaves[0];
                int maxScreenY = int.MinValue;
                foreach (Control leaf in leaves)
                {
                    Point screenPt = leaf.Parent.PointToScreen(new Point(leaf.Left, leaf.Bottom));
                    if (screenPt.Y > maxScreenY)
                    {
                        maxScreenY = screenPt.Y;
                        bottomLeaf = leaf;
                    }
                }

                // Scroll bottom control into view
                contentPanel.ScrollControlIntoView(bottomLeaf);
                Application.DoEvents();

                // Verify bottomLeaf is now within contentPanel visible viewport
                Point clientPt = contentPanel.PointToClient(bottomLeaf.Parent.PointToScreen(bottomLeaf.Location));
                Assert(clientPt.Y >= -10 && clientPt.Y < contentPanel.ClientSize.Height,
                    string.Format("Bottom control {0} (Text='{1}') must be visible in viewport after scroll, actual client Y={2}, ViewportHeight={3}",
                        bottomLeaf.GetType().Name, bottomLeaf.Text, clientPt.Y, contentPanel.ClientSize.Height));

                // Capture scrolled bottom bitmap
                CaptureSettingsBitmap(form, "settings-watermark-scrolled-bottom.png");

                form.Close();
            }
        }

        private static void CollectLeafControls(Control parent, List<Control> list)
        {
            if (parent.Controls.Count == 0 && parent.Visible)
            {
                list.Add(parent);
                return;
            }
            foreach (Control c in parent.Controls)
            {
                if (c.Visible)
                {
                    CollectLeafControls(c, list);
                }
            }
        }

        // Test 6: Extreme Config Clamp No Exception (WatermarkScale 70/180, Opacity 5/100)
        private static void TestExtremeConfigClampNoException()
        {
            int[][] testCases = new int[][]
            {
                new int[] { 70, 5 },    // Low bounds
                new int[] { 180, 100 },  // High bounds
                new int[] { 50, 0 },    // Extra extreme low
                new int[] { 250, 150 }   // Extra extreme high
            };

            foreach (int[] tc in testCases)
            {
                AppConfig cfg = new AppConfig();
                cfg.WatermarkScale = tc[0];
                cfg.WatermarkOpacity = tc[1];

                using (SettingsForm form = CreateTestForm(cfg))
                {
                    form.Show();
                    Application.DoEvents();

                    // Switch to Watermark tab which initializes AddSliderRow
                    InvokeSwitchTab(form, "Watermark");

                    FieldInfo fiScale = typeof(SettingsForm).GetField("_tbScale", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo fiOpacity = typeof(SettingsForm).GetField("_tbOpacity", BindingFlags.Instance | BindingFlags.NonPublic);

                    Assert(fiScale != null && fiOpacity != null, "Sliders fields must exist");
                    TrackBar tbScale = (TrackBar)fiScale.GetValue(form);
                    TrackBar tbOpacity = (TrackBar)fiOpacity.GetValue(form);

                    Assert(tbScale.Value >= tbScale.Minimum && tbScale.Value <= tbScale.Maximum,
                        string.Format("Scale value {0} must be clamped to [{1}, {2}]", tbScale.Value, tbScale.Minimum, tbScale.Maximum));

                    Assert(tbOpacity.Value >= tbOpacity.Minimum && tbOpacity.Value <= tbOpacity.Maximum,
                        string.Format("Opacity value {0} must be clamped to [{1}, {2}]", tbOpacity.Value, tbOpacity.Minimum, tbOpacity.Maximum));

                    form.Close();
                }
            }
        }

        // Test 7: Multi-DPI simulation (100%, 125%, 150%)
        private static void TestMultiDpiSimulation()
        {
            float[] scaleFactors = new float[] { 1.0f, 1.25f, 1.5f };
            string[] tabs = new string[] { "General", "Watermark", "Position", "Names", "System", "About", "Watermark" };

            foreach (float factor in scaleFactors)
            {
                using (SettingsForm form = CreateTestForm())
                {
                    form.Show();
                    Application.DoEvents();

                    if (Math.Abs(factor - 1.0f) > 0.01f)
                    {
                        form.Scale(new SizeF(factor, factor));
                        Application.DoEvents();
                    }

                    foreach (string tab in tabs)
                    {
                        InvokeSwitchTab(form, tab);
                        CheckNoOverlapsRecursive(form, tab + "@" + (int)(factor * 100) + "%DPI");
                    }

                    form.Close();
                }
            }
        }

        // Test 8: Zero persistence side effects
        private static void TestZeroPersistenceSideEffects()
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindicatorPlus");
            string configPath = Path.Combine(configDir, "config.ini");

            DateTime beforeWrite = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            long beforeLength = File.Exists(configPath) ? new FileInfo(configPath).Length : 0;

            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                InvokeSwitchTab(form, "Watermark");

                FieldInfo fiTxt = typeof(SettingsForm).GetField("_txtTitleTpl", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fiTxt != null)
                {
                    TextBox txt = (TextBox)fiTxt.GetValue(form);
                    if (txt != null)
                    {
                        txt.Text = "UnitTestModifiedTitle";
                        Application.DoEvents();
                    }
                }

                FieldInfo fiScale = typeof(SettingsForm).GetField("_tbScale", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fiScale != null)
                {
                    TrackBar tb = (TrackBar)fiScale.GetValue(form);
                    if (tb != null)
                    {
                        tb.Value = 135;
                        Application.DoEvents();
                    }
                }

                form.Close();
            }

            DateTime afterWrite = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            long afterLength = File.Exists(configPath) ? new FileInfo(configPath).Length : 0;

            Assert(beforeWrite == afterWrite && beforeLength == afterLength,
                "Config file must not be modified during test run (Zero persistence invariant)");
        }

        // Test 9: Full Six Pages Bitmap Capture (DrawToBitmap to C:/tmp/WindicatorAcceptance/settings-*.png)
        private static void TestFullSixPagesBitmapCapture()
        {
            string[] tabs = new string[] { "General", "Watermark", "Position", "Names", "System", "About", "Watermark" };
            string[] fileNames = new string[] {
                "settings-general.png",
                "settings-watermark.png",
                "settings-position.png",
                "settings-names.png",
                "settings-system.png",
                "settings-about.png",
                "settings-watermark-returned.png"
            };

            using (SettingsForm form = CreateTestForm())
            {
                form.Show();
                Application.DoEvents();

                for (int i = 0; i < tabs.Length; i++)
                {
                    InvokeSwitchTab(form, tabs[i]);
                    Application.DoEvents();
                    CaptureSettingsBitmap(form, fileNames[i]);
                }

                form.Close();
            }
        }
    }
}
