using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WindicatorPlus.Tests
{
    public class SettingsSwitchPerformanceTests
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[BENCHMARK] Windicator+ Settings Switch Benchmark (Enhanced)");
            Console.WriteLine("==================================================");

            try
            {
                try { NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Stopwatch opening = Stopwatch.StartNew();
                AppConfig config = new AppConfig();
                using (SettingsForm form = new SettingsForm(config, null, null, null))
                {
                    form.SuppressPersistenceForTesting = true;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    form.Update();
                    Application.DoEvents();
                    Console.WriteLine("First open including construction and paint: " + opening.ElapsedMilliseconds + " ms");

                    MethodInfo mi = typeof(SettingsForm).GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (mi == null) throw new Exception("SwitchTab not found");

                    FieldInfo fiPanel = typeof(SettingsForm).GetField("_rightContentPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                    Panel rightPanel = fiPanel != null ? (Panel)fiPanel.GetValue(form) : null;

                    string[] viewNames = new string[] {
                        "_viewGeneral", "_viewWatermark", "_viewPosition", "_viewNames", "_viewSystem", "_viewAbout"
                    };

                    Dictionary<string, Control> initialViews = new Dictionary<string, Control>();
                    Dictionary<string, IntPtr> initialHandles = new Dictionary<string, IntPtr>();

                    int layoutEventCount = 0;
                    LayoutEventHandler layoutTracker = delegate(object sender, LayoutEventArgs e)
                    {
                        layoutEventCount++;
                    };

                    form.Layout += layoutTracker;
                    if (rightPanel != null)
                    {
                        rightPanel.Layout += layoutTracker;
                    }

                    foreach (string vName in viewNames)
                    {
                        FieldInfo fiView = typeof(SettingsForm).GetField(vName, BindingFlags.Instance | BindingFlags.NonPublic);
                        if (fiView != null)
                        {
                            Control c = (Control)fiView.GetValue(form);
                            if (c != null)
                            {
                                initialViews[vName] = c;
                                initialHandles[vName] = c.Handle;
                                c.Layout += layoutTracker;
                            }
                        }
                    }

                    // Two full rounds of all 6 tabs
                    string[] testTabs = new string[] {
                        // Round 1
                        "Watermark", "Position", "Names", "System", "About", "General",
                        // Round 2
                        "Watermark", "Position", "Names", "System", "About", "General"
                    };

                    Console.WriteLine(string.Format("Form Size: {0}x{1}, DPI AutoScaleDimensions: {2}x{3}",
                        form.Width, form.Height,
                        form.AutoScaleDimensions.Width, form.AutoScaleDimensions.Height));

                    Stopwatch sw = new Stopwatch();
                    List<string> failures = new List<string>();

                    for (int i = 0; i < testTabs.Length; i++)
                    {
                        string tab = testTabs[i];
                        int round = (i / 6) + 1;
                        int step = (i % 6) + 1;

                        layoutEventCount = 0;

                        sw.Restart();
                        mi.Invoke(form, new object[] { tab });
                        long tInvoke = sw.ElapsedMilliseconds;

                        Stopwatch swPaint = Stopwatch.StartNew();
                        form.Update();
                        Application.DoEvents();
                        swPaint.Stop();
                        long tPaint = swPaint.ElapsedMilliseconds;

                        long totalMs = tInvoke + tPaint;

                        // Verify controls and handles identity (no destruction / recreation)
                        bool handleCheckPassed = true;
                        string handleMismatchMsg = "";
                        foreach (string vName in viewNames)
                        {
                            FieldInfo fiView = typeof(SettingsForm).GetField(vName, BindingFlags.Instance | BindingFlags.NonPublic);
                            Control c = fiView != null ? (Control)fiView.GetValue(form) : null;
                            if (c == null || !initialViews.ContainsKey(vName) || !object.ReferenceEquals(c, initialViews[vName]))
                            {
                                handleCheckPassed = false;
                                handleMismatchMsg = string.Format("View object identity changed for {0}!", vName);
                                break;
                            }
                            if (!initialHandles.ContainsKey(vName) || c.Handle != initialHandles[vName])
                            {
                                handleCheckPassed = false;
                                handleMismatchMsg = string.Format("Window Handle changed for {0} (Old: {1}, New: {2})!",
                                    vName, initialHandles[vName], c.Handle);
                                break;
                            }
                        }

                        Console.WriteLine(string.Format("[R{0} S{1}] SwitchTab -> {2,-10} : Invoke={3,4} ms, Update+DoEvents={4,4} ms, Total={5,4} ms, LayoutEvents={6,3}{7}",
                            round, step, tab, tInvoke, tPaint, totalMs, layoutEventCount,
                            handleCheckPassed ? "" : " [HANDLE MISMATCH]"));

                        if (!handleCheckPassed)
                        {
                            failures.Add(string.Format("Round {0} Step {1} ({2}): {3}", round, step, tab, handleMismatchMsg));
                        }

                        if (totalMs >= 200)
                        {
                            failures.Add(string.Format("Round {0} Step {1} ({2}) took {3}ms (Invoke={4}ms, Paint={5}ms, Layouts={6}), exceeding 200ms limit!",
                                round, step, tab, totalMs, tInvoke, tPaint, layoutEventCount));
                        }
                    }

                    form.Close();

                    Console.WriteLine("==================================================");
                    if (failures.Count > 0)
                    {
                        Console.WriteLine(string.Format("BENCHMARK FAILED ({0} violation(s)):", failures.Count));
                        foreach (string f in failures)
                        {
                            Console.WriteLine("  - " + f);
                        }
                        Console.WriteLine("==================================================");
                        return 1;
                    }

                    Console.WriteLine("BENCHMARK COMPLETED SUCCESSFULLY (All 12 switches < 200ms, all handles verified)");
                    Console.WriteLine("==================================================");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }
    }
}
