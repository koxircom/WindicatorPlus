using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using UIAutomationClientInterop;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class MockOverflowSpaceProvider : ITaskbarSpaceProvider
    {
        private TaskbarSpaceSnapshot _snapshot;
        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

        public MockOverflowSpaceProvider(TaskbarSpaceSnapshot initialSnapshot)
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

    internal class MockOverflowAutomationElement : IUIAutomationElement
    {
        public int MockControlType { get; set; }
        public string MockAutomationId { get; set; }
        public string MockClassName { get; set; }
        public string MockName { get; set; }
        public tagRECT MockBoundingRectangle { get; set; }

        public int CachedControlType { get { return MockControlType; } }
        public string CachedAutomationId { get { return MockAutomationId; } }
        public string CachedClassName { get { return MockClassName; } }
        public string CachedName { get { return MockName; } }
        public tagRECT CachedBoundingRectangle { get { return MockBoundingRectangle; } }

        #pragma warning disable 0067
        public int CurrentProcessId { get { throw new NotImplementedException(); } }
        public int CurrentControlType { get { throw new NotImplementedException(); } }
        public string CurrentLocalizedControlType { get { throw new NotImplementedException(); } }
        public string CurrentName { get { throw new NotImplementedException(); } }
        public string CurrentAcceleratorKey { get { throw new NotImplementedException(); } }
        public string CurrentAccessKey { get { throw new NotImplementedException(); } }
        public int CurrentHasKeyboardFocus { get { throw new NotImplementedException(); } }
        public int CurrentIsKeyboardFocusable { get { throw new NotImplementedException(); } }
        public int CurrentIsEnabled { get { throw new NotImplementedException(); } }
        public string CurrentAutomationId { get { throw new NotImplementedException(); } }
        public string CurrentClassName { get { throw new NotImplementedException(); } }
        public string CurrentHelpText { get { throw new NotImplementedException(); } }
        public int CurrentCulture { get { throw new NotImplementedException(); } }
        public int CurrentIsControlElement { get { throw new NotImplementedException(); } }
        public int CurrentIsContentElement { get { throw new NotImplementedException(); } }
        public int CurrentIsPassword { get { throw new NotImplementedException(); } }
        public IntPtr CurrentNativeWindowHandle { get { throw new NotImplementedException(); } }
        public string CurrentItemType { get { throw new NotImplementedException(); } }
        public int CurrentIsOffscreen { get { throw new NotImplementedException(); } }
        public UIAutomationClientInterop.OrientationType CurrentOrientation { get { throw new NotImplementedException(); } }
        public string CurrentFrameworkId { get { throw new NotImplementedException(); } }
        public int CurrentIsRequiredForForm { get { throw new NotImplementedException(); } }
        public string CurrentItemStatus { get { throw new NotImplementedException(); } }
        public tagRECT CurrentBoundingRectangle { get { throw new NotImplementedException(); } }
        public IUIAutomationElement CurrentLabeledBy { get { throw new NotImplementedException(); } }
        public string CurrentAriaRole { get { throw new NotImplementedException(); } }
        public string CurrentAriaProperties { get { throw new NotImplementedException(); } }
        public int CurrentIsDataValidForForm { get { throw new NotImplementedException(); } }
        public IUIAutomationElementArray CurrentControllerFor { get { throw new NotImplementedException(); } }
        public IUIAutomationElementArray CurrentDescribedBy { get { throw new NotImplementedException(); } }
        public IUIAutomationElementArray CurrentFlowsTo { get { throw new NotImplementedException(); } }
        public string CurrentProviderDescription { get { throw new NotImplementedException(); } }

        public int CachedProcessId { get { throw new NotImplementedException(); } }
        public string CachedLocalizedControlType { get { throw new NotImplementedException(); } }
        public string CachedAcceleratorKey { get { throw new NotImplementedException(); } }
        public string CachedAccessKey { get { throw new NotImplementedException(); } }
        public int CachedHasKeyboardFocus { get { throw new NotImplementedException(); } }
        public int CachedIsKeyboardFocusable { get { throw new NotImplementedException(); } }
        public int CachedIsEnabled { get { throw new NotImplementedException(); } }
        public string CachedHelpText { get { throw new NotImplementedException(); } }
        public int CachedCulture { get { throw new NotImplementedException(); } }
        public int CachedIsControlElement { get { throw new NotImplementedException(); } }
        public int CachedIsContentElement { get { throw new NotImplementedException(); } }
        public int CachedIsPassword { get { throw new NotImplementedException(); } }
        public IntPtr CachedNativeWindowHandle { get { throw new NotImplementedException(); } }
        public string CachedItemType { get { throw new NotImplementedException(); } }
        public int CachedIsOffscreen { get { throw new NotImplementedException(); } }
        public UIAutomationClientInterop.OrientationType CachedOrientation { get { throw new NotImplementedException(); } }
        public string CachedFrameworkId { get { throw new NotImplementedException(); } }
        public int CachedIsRequiredForForm { get { throw new NotImplementedException(); } }
        public string CachedItemStatus { get { throw new NotImplementedException(); } }
        public IUIAutomationElement CachedLabeledBy { get { throw new NotImplementedException(); } }
        public string CachedAriaRole { get { throw new NotImplementedException(); } }
        public string CachedAriaProperties { get { throw new NotImplementedException(); } }
        public int CachedIsDataValidForForm { get { throw new NotImplementedException(); } }
        public IUIAutomationElementArray CachedControllerFor { get { throw new NotImplementedException(); } }
        public IUIAutomationElementArray CachedDescribedBy { get { throw new NotImplementedException(); } }
        public IUIAutomationElementArray CachedFlowsTo { get { throw new NotImplementedException(); } }
        public string CachedProviderDescription { get { throw new NotImplementedException(); } }

        public void SetFocus() { }
        public int[] GetRuntimeId() { return null; }
        public IUIAutomationElement FindFirst(UIAutomationClientInterop.TreeScope scope, IUIAutomationCondition condition) { return null; }
        public IUIAutomationElementArray FindAll(UIAutomationClientInterop.TreeScope scope, IUIAutomationCondition condition) { return null; }
        public IUIAutomationElement FindFirstBuildCache(UIAutomationClientInterop.TreeScope scope, IUIAutomationCondition condition, IUIAutomationCacheRequest cacheRequest) { return null; }
        public IUIAutomationElementArray FindAllBuildCache(UIAutomationClientInterop.TreeScope scope, IUIAutomationCondition condition, IUIAutomationCacheRequest cacheRequest) { return null; }
        public IUIAutomationElement BuildUpdatedCache(IUIAutomationCacheRequest cacheRequest) { return null; }
        public object GetCurrentPropertyValue(int propertyId) { return null; }
        public object GetCurrentPropertyValueEx(int propertyId, int ignoreDefaultValue) { return null; }
        public object GetCachedPropertyValue(int propertyId) { return null; }
        public object GetCachedPropertyValueEx(int propertyId, int ignoreDefaultValue) { return null; }
        public IntPtr GetCurrentPatternAs(int patternId, ref Guid riid) { return IntPtr.Zero; }
        public IntPtr GetCachedPatternAs(int patternId, ref Guid riid) { return IntPtr.Zero; }
        public object GetCurrentPattern(int patternId) { return null; }
        public object GetCachedPattern(int patternId) { return null; }
        public IUIAutomationElement GetCachedParent() { return null; }
        public IUIAutomationElementArray GetCachedChildren() { return null; }
        public int GetClickablePoint(out tagPOINT clickable) { clickable = new tagPOINT(); return 0; }
        #pragma warning restore 0067
    }

    public static class TaskbarOverflowTests
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(int dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] byte[] pvInfo, int nLength, out int lpnLengthNeeded);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, int dwDesiredAccess, IntPtr lpsa);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int UOI_NAME = 2;
        private const uint CREATE_NO_WINDOW = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

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
                    return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
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

        private static AppConfig CreateTestConfig()
        {
            AppConfig config = new AppConfig();
            config.WatermarkEnabled = true;
            config.WatermarkPosition = "Taskbar";
            config.WatermarkScale = 100;
            config.TitleTemplate = "{name}";
            config.ShowBigNumber = true;
            config.DesktopNames = new Dictionary<int, string>
            {
                { 1, "办公" },
                { 2, "Ai" },
                { 3, "看板" }
            };
            return config;
        }

        private sealed class MultiWriter : TextWriter
        {
            private readonly TextWriter _orig;
            private readonly StreamWriter _file;
            public MultiWriter(TextWriter orig, string filePath)
            {
                _orig = orig;
                _file = new StreamWriter(filePath, false, Encoding.UTF8) { AutoFlush = true };
            }
            public override Encoding Encoding { get { return Encoding.UTF8; } }
            public override void Write(char value)
            {
                _orig.Write(value);
                _file.Write(value);
            }
            public override void Write(string value)
            {
                _orig.Write(value);
                _file.Write(value);
            }
            public override void WriteLine(string value)
            {
                _orig.WriteLine(value);
                _file.WriteLine(value);
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing && _file != null)
                {
                    _file.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool checkBaselineDefect = false;
            string logFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--check-baseline-defect", StringComparison.OrdinalIgnoreCase))
                {
                    checkBaselineDefect = true;
                }
                else if (string.Equals(args[i], "--log-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    logFile = args[++i];
                }
            }

            if (!string.IsNullOrEmpty(logFile))
            {
                try
                {
                    MultiWriter mw = new MultiWriter(Console.Out, logFile);
                    Console.SetOut(mw);
                    Console.SetError(mw);
                }
                catch { }
            }

            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Overflow & Anti-Crowding Tests");
            Console.WriteLine("==================================================");

            string currentDesktop = GetCurrentDesktopName();
            if (!currentDesktop.StartsWith("WindicatorTest-", StringComparison.OrdinalIgnoreCase))
            {
                string testDesktopName = "WindicatorTest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Console.WriteLine("[SAFETY GUARD] Current desktop is '" + currentDesktop + "'. Spawning child in isolated desktop '" + testDesktopName + "' (no SwitchDesktop)...");

                IntPtr hDesktop = CreateDesktop(testDesktopName, IntPtr.Zero, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
                if (hDesktop == IntPtr.Zero)
                {
                    Console.WriteLine("[SAFETY GUARD ERROR] Failed to create isolated test desktop.");
                    return 2;
                }

                string childLogPath = Path.Combine(Path.GetTempPath(), "TaskbarOverflowTest-" + Guid.NewGuid().ToString("N") + ".log");

                try
                {
                    STARTUPINFO si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                    si.lpDesktop = testDesktopName;

                    string exePath = Assembly.GetExecutingAssembly().Location;
                    string cmdLine = "\"" + exePath + "\" --log-file \"" + childLogPath + "\"";
                    if (checkBaselineDefect)
                    {
                        cmdLine += " --check-baseline-defect";
                    }

                    PROCESS_INFORMATION pi;
                    bool ok = CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW, IntPtr.Zero, null, ref si, out pi);
                    if (!ok)
                    {
                        Console.WriteLine("[SAFETY GUARD ERROR] CreateProcess failed in isolated desktop.");
                        return 2;
                    }

                    WaitForSingleObject(pi.hProcess, 60000);
                    uint exitCode = 1;
                    GetExitCodeProcess(pi.hProcess, out exitCode);
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);

                    if (File.Exists(childLogPath))
                    {
                        string childLogs = File.ReadAllText(childLogPath, Encoding.UTF8);
                        Console.WriteLine("--- [ISOLATED CHILD LOGS BEGIN] ---");
                        Console.Write(childLogs);
                        Console.WriteLine("--- [ISOLATED CHILD LOGS END] ---");
                        try { File.Delete(childLogPath); } catch { }
                    }

                    Console.WriteLine("[SAFETY GUARD] Child finished with exit code: " + exitCode);
                    return (int)exitCode;
                }
                finally
                {
                    CloseDesktop(hDesktop);
                }
            }

            Console.WriteLine("[SAFETY GUARD VERIFIED] Running inside isolated desktop context: " + currentDesktop);

            if (checkBaselineDefect)
            {
                return RunBaselineDefectCheck();
            }

            return RunAllVerificationTests();
        }

        private static int RunBaselineDefectCheck()
        {
            Console.WriteLine("\n--- [BASELINE DEFECT CHECK] 验证旧版存在的缺陷（预期检测到失败） ---");
            bool defectDetected = false;

            // 缺陷 A: 当按钮越界 (right > trayLeft) 时，旧版抛出 InvalidOperationException("App and tray regions intersect")
            try
            {
                int maxApp = 0;
                var overlapBtn = new MockOverflowAutomationElement
                {
                    MockControlType = 50000,
                    MockAutomationId = "AppOverlap",
                    MockClassName = "TaskbarAppButton",
                    MockName = "Clash",
                    MockBoundingRectangle = new tagRECT { left = 1100, top = 0, right = 1250, bottom = 48 }
                };
                NativeTaskbarAutomation.ProcessAppButtonCached(overlapBtn, 1225, ref maxApp);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.Contains("App and tray regions intersect"))
                {
                    Console.WriteLine("[DEFECT DETECTED] ProcessAppButtonCached threw 'App and tray regions intersect' instead of saturating (exit=101)");
                    defectDetected = true;
                }
            }

            // 缺陷 B: 旧版 EvaluateTaskbarSnapshot 在 maxAppRight > trayLeft 时直接判 Invalid
            Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
            Rectangle trRect = new Rectangle(1225, 1002, 455, 48);
            var snap = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 5, 1250);
            if (!snap.IsValid)
            {
                Console.WriteLine("[DEFECT DETECTED] EvaluateTaskbarSnapshot returned Invalid on maxAppRight > trayLeft instead of safe saturation (exit=102)");
                defectDetected = true;
            }

            if (defectDetected)
            {
                Console.WriteLine("[BASELINE CONFIRMED] Baseline defect successfully observed.");
                return 0; // Baseline check passed (defect confirmed)
            }
            else
            {
                Console.WriteLine("[BASELINE FAILED] Defect was NOT detected in baseline code.");
                return 1;
            }
        }

        private static int RunAllVerificationTests()
        {
            int passed = 0;
            int failed = 0;

            // Test 1: Button Right > TrayLeft safely saturated to trayLeft without exception
            try
            {
                Console.Write("[RUN] Test 1: App button right > trayLeft saturates to trayLeft... ");
                Test_ProcessAppButtonCached_RightGreaterThanTrayLeft();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 2: Bad geometry (zero width/height) throws InvalidOperationException
            try
            {
                Console.Write("[RUN] Test 2: App button bad geometry throws unavailable exception... ");
                Test_ProcessAppButtonCached_BadGeometryThrows();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 3: EvaluateTaskbarSnapshot safe saturation
            try
            {
                Console.Write("[RUN] Test 3: EvaluateTaskbarSnapshot saturates maxAppRight > trayLeft... ");
                Test_EvaluateTaskbarSnapshot_Saturation();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 4: Real Evidence Crowded Taskbar -> Floats above taskbar, zero overlap with Clash
            try
            {
                Console.Write("[RUN] Test 4: Real evidence crowded taskbar floats above, zero Clash overlap... ");
                Test_RealEvidence_Crowded_FloatsAbove_ZeroOverlap();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 5: Rejection of Ellipsis & Number-only degradation when Title is configured
            try
            {
                Console.Write("[RUN] Test 5: Update decision rejects Ellipsis/Number-only degradation for title... ");
                Test_Reject_Ellipsis_Or_NumberOnly_Degradation();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 6: Auto recovery from floating to docked when space returns
            try
            {
                Console.Write("[RUN] Test 6: Auto recovery to taskbar docking when space returns... ");
                Test_AutoRecovery_WhenSpaceReturns();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 7: Empty title with Big Number docks properly if space fits
            try
            {
                Console.Write("[RUN] Test 7: Empty title with Big Number docks properly if space fits... ");
                Test_EmptyTitle_BigNumber_Docks_Properly();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine(string.Format("Results: {0} Passed, {1} Failed", passed, failed));
            Console.WriteLine("==================================================");

            return failed == 0 ? 0 : 1;
        }

        private static void Test_ProcessAppButtonCached_RightGreaterThanTrayLeft()
        {
            int trayLeft = 1225;
            int maxAppRight = 0;

            var overlapBtn = new MockOverflowAutomationElement
            {
                MockControlType = 50000,
                MockAutomationId = "AppOverlap",
                MockClassName = "TaskbarAppButton",
                MockName = "Clash",
                MockBoundingRectangle = new tagRECT { left = 1100, top = 0, right = 1250, bottom = 48 }
            };

            bool valid = NativeTaskbarAutomation.ProcessAppButtonCached(overlapBtn, trayLeft, ref maxAppRight);
            Assert(valid, "Button crossing trayLeft must be accepted as valid button");
            Assert(maxAppRight == 1225, "maxAppRight must be saturated/capped at trayLeft (1225), got: " + maxAppRight);
        }

        private static void Test_ProcessAppButtonCached_BadGeometryThrows()
        {
            int trayLeft = 1225;
            int maxAppRight = 0;

            var badBtn = new MockOverflowAutomationElement
            {
                MockControlType = 50000,
                MockAutomationId = "AppBadGeom",
                MockClassName = "TaskbarAppButton",
                MockName = "Bad",
                MockBoundingRectangle = new tagRECT { left = 500, top = 0, right = 500, bottom = 48 }
            };

            bool threw = false;
            try
            {
                NativeTaskbarAutomation.ProcessAppButtonCached(badBtn, trayLeft, ref maxAppRight);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == "App button geometry unavailable")
                {
                    threw = true;
                }
            }
            Assert(threw, "Zero-width button must throw InvalidOperationException('App button geometry unavailable')");
        }

        private static void Test_EvaluateTaskbarSnapshot_Saturation()
        {
            Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
            Rectangle trRect = new Rectangle(1225, 1002, 455, 48);

            // 1. maxAppRight > trayLeft must safely saturate to trayLeft
            var snap1 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 5, 1300);
            Assert(snap1.IsValid, "Snapshot with maxAppRight > trayLeft must be valid via saturation");
            Assert(snap1.MaxAppRight == 1225, "Snapshot MaxAppRight must be capped to trayLeft (1225), got: " + snap1.MaxAppRight);

            // 2. Zero valid buttons must be Invalid
            var snap2 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 0, 800);
            Assert(!snap2.IsValid, "Zero valid buttons must result in Invalid snapshot");

            // 3. maxAppRight <= 0 must be Invalid
            var snap3 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 5, 0);
            Assert(!snap3.IsValid, "maxAppRight <= 0 must result in Invalid snapshot");
        }

        private static void Test_RealEvidence_Crowded_FloatsAbove_ZeroOverlap()
        {
            using (WatermarkWindow window = new WatermarkWindow())
            {
                window.CreateControl();
                AppConfig config = CreateTestConfig();
                config.WatermarkPosition = "Taskbar";
                window.ApplyConfig(config);
                window.SetDesktop(2, 3, "Ai"); // Desktop 2 ("Ai")

                // 主控实证数据:
                // taskbar: [0, 1002, 1680, 48]
                // nativeTray: [1225, 1002, 455, 48]
                // maxAppRight: 1213 (Clash: [1101, 1002, 1213, 1050])
                Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
                Rectangle trRect = new Rectangle(1225, 1002, 455, 48);
                Rectangle clashRect = new Rectangle(1101, 1002, 112, 48);

                TaskbarSpaceSnapshot snap = new TaskbarSpaceSnapshot(1213, 1225, tbRect, trRect, true, Stopwatch.GetTimestamp());
                MockOverflowSpaceProvider provider = new MockOverflowSpaceProvider(snap);
                window.SetTaskbarSpaceProvider(provider);
                window.UpdateWatermark();

                Assert(window.IsTaskbarOverflowFloating, "Watermark must float when taskbar space is crowded (available width 12px < Full 82px)");

                Rectangle bounds = window.ActualScreenRect;
                Assert(!bounds.IsEmpty && bounds.Bottom <= tbRect.Top - 8, string.Format("Floating watermark Bottom ({0}) must be <= tb.Top - 8 ({1})", bounds.Bottom, tbRect.Top - 8));

                Rectangle intersect = Rectangle.Intersect(bounds, clashRect);
                Assert(intersect.IsEmpty, string.Format("Floating watermark must have ZERO overlap with Clash button ({0}), intersect={1}", clashRect, intersect));
            }
        }

        private static void Test_Reject_Ellipsis_Or_NumberOnly_Degradation()
        {
            using (WatermarkWindow window = new WatermarkWindow())
            {
                window.CreateControl();
                AppConfig config = CreateTestConfig();
                config.WatermarkPosition = "Taskbar";
                window.ApplyConfig(config);
                window.SetDesktop(2, 3, "Ai"); // "Ai"

                // 给定 35px 空间 (足以容纳纯数字，但容纳不下 Full "Ai")
                Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
                Rectangle trRect = new Rectangle(1225, 1002, 455, 48);
                // 1225 - 35 = 1190
                TaskbarSpaceSnapshot snap = new TaskbarSpaceSnapshot(1190, 1225, tbRect, trRect, true, Stopwatch.GetTimestamp());
                MockOverflowSpaceProvider provider = new MockOverflowSpaceProvider(snap);
                window.SetTaskbarSpaceProvider(provider);
                window.UpdateWatermark();

                Assert(window.IsTaskbarOverflowFloating, "Watermark must NOT degrade to Number-only or Ellipsis in taskbar; it MUST float above");
                Assert(window.ActualScreenRect.Bottom <= tbRect.Top - 8, "Must float above taskbar");
            }
        }

        private static void Test_AutoRecovery_WhenSpaceReturns()
        {
            using (WatermarkWindow window = new WatermarkWindow())
            {
                window.CreateControl();
                AppConfig config = CreateTestConfig();
                config.WatermarkPosition = "Taskbar";
                window.ApplyConfig(config);
                window.SetDesktop(2, 3, "Ai"); // "Ai"

                Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
                Rectangle trRect = new Rectangle(1225, 1002, 455, 48);

                // 阶段 1: 拥挤浮动
                TaskbarSpaceSnapshot crowdedSnap = new TaskbarSpaceSnapshot(1213, 1225, tbRect, trRect, true, Stopwatch.GetTimestamp());
                MockOverflowSpaceProvider provider = new MockOverflowSpaceProvider(crowdedSnap);
                window.SetTaskbarSpaceProvider(provider);
                window.UpdateWatermark();
                Assert(window.IsTaskbarOverflowFloating, "Must be floating when crowded");

                // 阶段 2: 空间释放，应用最右变为 800 (可用 425px)
                TaskbarSpaceSnapshot freeSnap = new TaskbarSpaceSnapshot(800, 1225, tbRect, trRect, true, Stopwatch.GetTimestamp());
                provider.SetSnapshot(freeSnap);
                window.UpdateWatermark();

                Assert(!window.IsTaskbarOverflowFloating, "Watermark must automatically recover to docked when space returns");
                Rectangle dockedBounds = window.ActualScreenRect;
                Assert(dockedBounds.Top >= tbRect.Top && dockedBounds.Bottom <= tbRect.Bottom,
                    string.Format("Docked watermark vertically must stay inside taskbar ([{0}, {1}]), got: Top={2}, Bottom={3}",
                        tbRect.Top, tbRect.Bottom, dockedBounds.Top, dockedBounds.Bottom));
                Assert(dockedBounds.Right <= 1225, "Docked watermark Right must be <= trayLeft (1225)");
                Assert(dockedBounds.Left >= 800, "Docked watermark Left must be >= maxAppRight (800)");
            }
        }

        private static void Test_EmptyTitle_BigNumber_Docks_Properly()
        {
            using (WatermarkWindow window = new WatermarkWindow())
            {
                window.CreateControl();
                AppConfig config = CreateTestConfig();
                config.WatermarkPosition = "Taskbar";
                config.TitleTemplate = ""; // 无标题
                config.DesktopNames.Clear();
                window.ApplyConfig(config);
                window.SetDesktop(2, 3, "");

                Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
                Rectangle trRect = new Rectangle(1225, 1002, 455, 48);

                // 提供 100px 充足空间
                TaskbarSpaceSnapshot freeSnap = new TaskbarSpaceSnapshot(1100, 1225, tbRect, trRect, true, Stopwatch.GetTimestamp());
                MockOverflowSpaceProvider provider = new MockOverflowSpaceProvider(freeSnap);
                window.SetTaskbarSpaceProvider(provider);
                window.UpdateWatermark();

                Assert(!window.IsTaskbarOverflowFloating, "Empty title with big number must dock normally when space is available");
            }
        }
    }
}
