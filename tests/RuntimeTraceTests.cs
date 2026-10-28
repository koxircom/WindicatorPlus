using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class RuntimeTraceTests
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] RuntimeTrace Headless Unit Tests");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            string testDir = @"C:\tmp\Windicator138";
            if (!Directory.Exists(testDir))
            {
                Directory.CreateDirectory(testDir);
            }

            // Test 1: Default disabled, zero files, no throws
            try
            {
                Console.Write("[RUN] Test 1: Default disabled, zero files, no throws... ");
                Test_DefaultDisabled_ZeroFiles_And_NoThrow(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 2: Enabled, writes valid json events, privacy check
            try
            {
                Console.Write("[RUN] Test 2: Enabled, writes events, privacy check... ");
                Test_Enabled_WritesValidEvents(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 3: Event budget limit shut down
            try
            {
                Console.Write("[RUN] Test 3: Event budget limit shut down... ");
                Test_EventBudgetExceeded(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 4: Time budget limit shut down
            try
            {
                Console.Write("[RUN] Test 4: Time budget limit shut down... ");
                Test_TimeBudgetExceeded(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 5: IOException isolation
            try
            {
                Console.Write("[RUN] Test 5: IOException isolation and no throw... ");
                Test_IOExceptionIsolation(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 6: CheckBudget without events shuts down on time expiration
            try
            {
                Console.Write("[RUN] Test 6: CheckBudget without events shuts down on timeout... ");
                Test_CheckBudgetWithoutEventsShutdown(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 7: FormatProbeException strictly whitelists only the two fixed InvalidOperationException messages
            try
            {
                Console.Write("[RUN] Test 7: FormatProbeException message whitelist... ");
                Test_FormatProbeExceptionWhitelist();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 8: Watermark clear records snapshot IsValid and age instead of dummy zeroes
            try
            {
                Console.Write("[RUN] Test 8: Watermark clear records snapshot age and valid... ");
                Test_WatermarkClearRecordsSnapshotMetadata(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 9: LogProbeChange records real diagnostic fields and button count
            try
            {
                Console.Write("[RUN] Test 9: LogProbeChange real fields and btnCount... ");
                Test_LogProbeChange_RealFieldsRecorded(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 10: ProbeFailure stage names and whitelisted exDetail format
            try
            {
                Console.Write("[RUN] Test 10: ProbeFailure stage names and exDetail... ");
                Test_ProbeFailureStageNamesAndExDetail(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine(string.Format("Summary: {0} passed, {1} failed", passed, failed));
            Console.WriteLine("==================================================");

            return failed == 0 ? 0 : 1;
        }

        private static void Test_DefaultDisabled_ZeroFiles_And_NoThrow(string testDir)
        {
            // Ensure no env var
            Environment.SetEnvironmentVariable("WINDICATOR_TRACE_FILE", null);
            RuntimeTrace.ResetForTesting();

            if (RuntimeTrace.IsEnabled)
            {
                throw new Exception("RuntimeTrace should be disabled by default when WINDICATOR_TRACE_FILE is not set");
            }

            string dummyPath = Path.Combine(testDir, "never_created.jsonl");
            if (File.Exists(dummyPath)) File.Delete(dummyPath);

            // Call all log APIs - none should throw or create files
            RuntimeTrace.LogAppStart(9999, "1.3.7");
            RuntimeTrace.LogProbeChange(true, new Rectangle(0, 0, 1920, 48), new Rectangle(1500, 0, 420, 48),
                Rectangle.Empty, new Rectangle(1500, 0, 420, 48), 1200, 5);
            RuntimeTrace.LogProbeFailure("FindShell", "NullReferenceException", Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
            RuntimeTrace.LogWatermarkUpdate("render", true, "dock_reuse_anchor", 1, 4, "桌面 1", "桌面 1",
                new Rectangle(1400, 4, 96, 40), new Rectangle(1400, 4, 96, 40), 1000, 50, true, true, false);
            RuntimeTrace.LogWindowPosChanged(new IntPtr(1001), IntPtr.Zero, 1400, 4, 96, 40, 0x14);
            RuntimeTrace.LogCoordinatorAdjust(new IntPtr(100), new IntPtr(200), new IntPtr(300), IntPtr.Zero,
                "Above", "Above", "None", "None", false, false, false);
            RuntimeTrace.LogMouseEvent("left", "down", 1450, 20, new IntPtr(500));
            RuntimeTrace.Flush();
            RuntimeTrace.Shutdown("Test");

            if (File.Exists(dummyPath))
            {
                throw new Exception("File was created despite trace being disabled!");
            }
        }

        private static void Test_Enabled_WritesValidEvents(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_enabled.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            RuntimeTrace.InitForTesting(traceFile, 10000, 600000);

            if (!RuntimeTrace.IsEnabled)
            {
                throw new Exception("RuntimeTrace should be enabled after InitForTesting");
            }

            RuntimeTrace.LogAppStart(1234, "1.3.7");
            RuntimeTrace.LogProbeChange(true, new Rectangle(0, 1032, 1920, 48), new Rectangle(1600, 1032, 320, 48),
                new Rectangle(1560, 1032, 40, 48), new Rectangle(1560, 1032, 360, 48), 1250, 8);
            RuntimeTrace.LogWatermarkUpdate("render", true, "dock_settle_moved", 2, 4, "桌面 2", "Dev",
                new Rectangle(1400, 1036, 150, 40), new Rectangle(1410, 1036, 150, 40), Stopwatch.GetTimestamp(), 20, true, true, false);
            RuntimeTrace.LogWindowPosChanged(new IntPtr(0x1234), new IntPtr(-1), 1410, 1036, 150, 40, 0x0040);
            RuntimeTrace.LogCoordinatorAdjust(new IntPtr(0x10), new IntPtr(0x20), new IntPtr(0x30), new IntPtr(0x40),
                "Above", "Above", "Above", "Above", true, true, false);
            RuntimeTrace.LogMouseEvent("left", "up", 1420, 1040, new IntPtr(0x99));

            RuntimeTrace.Flush();
            RuntimeTrace.Shutdown("NormalTestExit");

            if (!File.Exists(traceFile))
            {
                throw new Exception("Trace file was not created: " + traceFile);
            }

            string[] lines = File.ReadAllLines(traceFile);
            if (lines.Length < 7) // 6 logs + 1 exit
            {
                throw new Exception("Expected at least 7 event lines, got " + lines.Length);
            }

            // Verify json lines have seq, t, cat
            foreach (string line in lines)
            {
                if (!line.StartsWith("{") || !line.EndsWith("}"))
                {
                    throw new Exception("Line is not valid json: " + line);
                }
                if (!line.Contains("\"seq\":") || !line.Contains("\"t\":") || !line.Contains("\"cat\":"))
                {
                    throw new Exception("Missing required fields in line: " + line);
                }
            }

            // Privacy check: should NOT contain window title strings or random process names
            string allContent = File.ReadAllText(traceFile);
            if (allContent.Contains("chrome") || allContent.Contains("WeChat") || allContent.Contains("Visual Studio"))
            {
                throw new Exception("Privacy violation: found unexpected application names in trace");
            }
        }

        private static void Test_EventBudgetExceeded(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_event_budget.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            int maxEvents = 15;
            RuntimeTrace.InitForTesting(traceFile, maxEvents, 600000);

            // Attempt to write 40 events
            for (int i = 0; i < 40; i++)
            {
                RuntimeTrace.LogMouseEvent("left", "down", 100 + i, 200 + i, IntPtr.Zero);
            }

            if (RuntimeTrace.IsEnabled)
            {
                throw new Exception("RuntimeTrace should be auto-disabled after hitting event budget");
            }

            string[] lines = File.ReadAllLines(traceFile);
            // Exactly maxEvents data lines + 1 exit line with reason BudgetExceeded
            if (lines.Length != maxEvents + 1)
            {
                throw new Exception(string.Format("Expected {0} lines, got {1}", maxEvents + 1, lines.Length));
            }

            string lastLine = lines[lines.Length - 1];
            if (!lastLine.Contains("\"reason\":\"BudgetExceeded\""))
            {
                throw new Exception("Last event should have reason BudgetExceeded: " + lastLine);
            }
        }

        private static void Test_TimeBudgetExceeded(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_time_budget.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            long maxMs = 80;
            RuntimeTrace.InitForTesting(traceFile, 10000, maxMs);

            RuntimeTrace.LogMouseEvent("left", "down", 100, 200, IntPtr.Zero);
            Thread.Sleep(120);

            // Next write should detect time expiration and shutdown
            RuntimeTrace.LogMouseEvent("left", "up", 100, 200, IntPtr.Zero);

            if (RuntimeTrace.IsEnabled)
            {
                throw new Exception("RuntimeTrace should be auto-disabled after hitting time budget");
            }

            string[] lines = File.ReadAllLines(traceFile);
            string lastLine = lines[lines.Length - 1];
            if (!lastLine.Contains("\"reason\":\"BudgetExceeded\""))
            {
                throw new Exception("Last event should be BudgetExceeded: " + lastLine);
            }
        }

        private static void Test_IOExceptionIsolation(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_io_exception.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            // Lock file exclusively to simulate IOException
            using (FileStream locker = new FileStream(traceFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // Init should fail gracefully without throwing
                RuntimeTrace.InitForTesting(traceFile, 10000, 600000);

                if (RuntimeTrace.IsEnabled)
                {
                    throw new Exception("RuntimeTrace should fail to enable on locked file");
                }

                // Calling log methods must be completely safe
                RuntimeTrace.LogAppStart(111, "1.3.7");
                RuntimeTrace.LogMouseEvent("right", "down", 50, 50, IntPtr.Zero);
                RuntimeTrace.Flush();
                RuntimeTrace.Shutdown("Test");
            }
        }

        private static void Test_CheckBudgetWithoutEventsShutdown(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_check_budget_no_events.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            long maxMs = 50;
            RuntimeTrace.InitForTesting(traceFile, 10000, maxMs);

            if (!RuntimeTrace.IsEnabled)
            {
                throw new Exception("RuntimeTrace should be enabled initially");
            }

            // Wait until duration exceeds
            Thread.Sleep(90);

            // Call CheckBudget without writing any new events
            RuntimeTrace.CheckBudget();

            if (RuntimeTrace.IsEnabled)
            {
                throw new Exception("CheckBudget should shut down RuntimeTrace when duration exceeded");
            }

            if (!File.Exists(traceFile))
            {
                throw new Exception("Trace file was not created: " + traceFile);
            }

            string[] lines = File.ReadAllLines(traceFile);
            if (lines.Length == 0)
            {
                throw new Exception("Expected exit line in trace file");
            }

            string lastLine = lines[lines.Length - 1];
            if (!lastLine.Contains("\"reason\":\"BudgetExceeded\""))
            {
                throw new Exception("Expected BudgetExceeded in exit record: " + lastLine);
            }
        }

        private static void Test_FormatProbeExceptionWhitelist()
        {
            // Case 1: Whitelisted App button geometry unavailable
            var ex1 = new InvalidOperationException("App button geometry unavailable");
            string s1 = RuntimeTrace.FormatProbeException(ex1);
            if (s1 != "InvalidOperationException: App button geometry unavailable")
            {
                throw new Exception("Expected message included for ex1, got: " + s1);
            }

            // Case 2: Whitelisted App and tray regions intersect
            var ex2 = new InvalidOperationException("App and tray regions intersect");
            string s2 = RuntimeTrace.FormatProbeException(ex2);
            if (s2 != "InvalidOperationException: App and tray regions intersect")
            {
                throw new Exception("Expected message included for ex2, got: " + s2);
            }

            // Case 3: Other InvalidOperationException with potentially sensitive details -> ONLY Type
            var ex3 = new InvalidOperationException("Sensitive internal path C:\\secret\\foo");
            string s3 = RuntimeTrace.FormatProbeException(ex3);
            if (s3 != "InvalidOperationException")
            {
                throw new Exception("Expected only Type for non-whitelisted InvalidOperationException, got: " + s3);
            }

            // Case 4: Other exceptions (NullReference, Argument, etc.) -> ONLY Type
            var ex4 = new NullReferenceException("Sensitive stack info");
            string s4 = RuntimeTrace.FormatProbeException(ex4);
            if (s4 != "NullReferenceException")
            {
                throw new Exception("Expected only Type for NullReferenceException, got: " + s4);
            }

            var ex5 = new ArgumentException("Sensitive param value");
            string s5 = RuntimeTrace.FormatProbeException(ex5);
            if (s5 != "ArgumentException")
            {
                throw new Exception("Expected only Type for ArgumentException, got: " + s5);
            }
        }

        private static void Test_WatermarkClearRecordsSnapshotMetadata(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_water_clear_meta.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            RuntimeTrace.InitForTesting(traceFile, 1000, 600000);

            long fakeTicks = Stopwatch.GetTimestamp() - (Stopwatch.Frequency * 3); // 3000ms ago
            long ageMs = 3000;
            bool valid = true;

            RuntimeTrace.LogWatermarkUpdate(
                "clear",
                true,
                "snapshot_stale",
                1,
                4,
                "桌面 1",
                "",
                new Rectangle(1200, 1036, 120, 40),
                Rectangle.Empty,
                fakeTicks,
                ageMs,
                valid,
                true,
                false);

            RuntimeTrace.Flush();
            RuntimeTrace.Shutdown("Test");

            string[] lines = File.ReadAllLines(traceFile);
            string clearLine = null;
            foreach (var line in lines)
            {
                if (line.Contains("\"action\":\"clear\""))
                {
                    clearLine = line;
                    break;
                }
            }

            if (clearLine == null)
            {
                throw new Exception("Did not find clear line in trace");
            }

            if (!clearLine.Contains("\"decision\":\"snapshot_stale\""))
            {
                throw new Exception("Expected decision snapshot_stale, got: " + clearLine);
            }
            if (!clearLine.Contains("\"snapAgeMs\":3000"))
            {
                throw new Exception("Expected snapAgeMs 3000, got: " + clearLine);
            }
            if (!clearLine.Contains("\"snapValid\":true"))
            {
                throw new Exception("Expected snapValid true, got: " + clearLine);
            }
        }

        private static void Test_LogProbeChange_RealFieldsRecorded(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_probe_real_fields.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            RuntimeTrace.InitForTesting(traceFile, 1000, 600000);

            Rectangle tbRect = new Rectangle(0, 1032, 1920, 48);
            Rectangle nativeTray = new Rectangle(1600, 1032, 320, 48);
            Rectangle chevron = new Rectangle(1560, 1036, 32, 40);
            Rectangle effectiveTray = new Rectangle(1560, 1032, 360, 48);
            int maxApp = 1240;
            int realBtnCount = 7;

            RuntimeTrace.LogProbeChange(true, tbRect, nativeTray, chevron, effectiveTray, maxApp, realBtnCount);

            RuntimeTrace.Flush();
            RuntimeTrace.Shutdown("Test");

            string[] lines = File.ReadAllLines(traceFile);
            string probeLine = null;
            foreach (var line in lines)
            {
                if (line.Contains("\"cat\":\"probe_change\""))
                {
                    probeLine = line;
                    break;
                }
            }

            if (probeLine == null)
            {
                throw new Exception("Did not find probe_change line in trace");
            }

            if (!probeLine.Contains("\"nativeTray\":[1600,1032,320,48]"))
            {
                throw new Exception("Expected nativeTray recorded correctly, got: " + probeLine);
            }
            if (!probeLine.Contains("\"chevron\":[1560,1036,32,40]"))
            {
                throw new Exception("Expected chevron recorded correctly, got: " + probeLine);
            }
            if (!probeLine.Contains("\"effectiveTray\":[1560,1032,360,48]"))
            {
                throw new Exception("Expected effectiveTray recorded correctly, got: " + probeLine);
            }
            if (!probeLine.Contains("\"btnCount\":7"))
            {
                throw new Exception("Expected btnCount 7 recorded, got: " + probeLine);
            }
        }

        private static void Test_ProbeFailureStageNamesAndExDetail(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_probe_fail_stages.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);

            RuntimeTrace.InitForTesting(traceFile, 1000, 600000);

            string[] stages = new string[] { "FindChevron", "readChevron", "FindRepeater", "EnumerateButtons", "ProcessAppButton" };

            // Log failure for each required stage
            foreach (string stg in stages)
            {
                Exception ex;
                if (stg == "ProcessAppButton")
                {
                    ex = new InvalidOperationException("App button geometry unavailable");
                }
                else
                {
                    ex = new NullReferenceException("internal error");
                }

                string exDetail = RuntimeTrace.FormatProbeException(ex);
                RuntimeTrace.LogProbeFailure(stg, exDetail, new Rectangle(1600, 0, 320, 48), Rectangle.Empty, new Rectangle(1600, 0, 320, 48), 1200);
            }

            RuntimeTrace.Flush();
            RuntimeTrace.Shutdown("Test");

            string[] lines = File.ReadAllLines(traceFile);
            foreach (string stg in stages)
            {
                bool found = false;
                foreach (string line in lines)
                {
                    if (line.Contains("\"cat\":\"probe_fail\"") && line.Contains("\"stage\":\"" + stg + "\""))
                    {
                        found = true;
                        if (stg == "ProcessAppButton")
                        {
                            if (!line.Contains("InvalidOperationException: App button geometry unavailable"))
                            {
                                throw new Exception("Expected whitelisted message for ProcessAppButton, got: " + line);
                            }
                        }
                        else
                        {
                            if (line.Contains("internal error"))
                            {
                                throw new Exception("Privacy leak: unexpected non-whitelisted message found in line: " + line);
                            }
                            if (!line.Contains("\"exType\":\"NullReferenceException\""))
                            {
                                throw new Exception("Expected NullReferenceException Type only, got: " + line);
                            }
                        }
                        break;
                    }
                }

                if (!found)
                {
                    throw new Exception("Stage not found in trace output: " + stg);
                }
            }
        }
    }
}
