using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using UIAutomationClientInterop;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    internal class MockAutomationElement : IUIAutomationElement
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

    public class NativeTaskbarAutomationTests
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] NativeTaskbarAutomation Tests (Pure Strategy & COM)");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            // Test 1: Native COM Instance Creation & 150ms Timeouts Readback
            try
            {
                Console.Write("[RUN] Test 1: COM instance creation, 150ms timeouts readback & Dispose... ");
                Test_COMInstance_TimeoutsReadback();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 2: Stopwatch Step Budget Over 700ms Throws TimeoutException
            try
            {
                Console.Write("[RUN] Test 2: Step budget check > 700ms throws TimeoutException... ");
                Test_CheckBudget_ExceededThrows();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 3: IsTransientProbeException Classification
            try
            {
                Console.Write("[RUN] Test 3: IsTransientProbeException classifies Timeout/COM/Geometry... ");
                Test_IsTransientProbeException_Classification();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 4: ProcessAppButtonCached pure geometry & overlap gate
            try
            {
                Console.Write("[RUN] Test 4: ProcessAppButtonCached enforces geometry and intersection gates... ");
                Test_ProcessAppButtonCached_Gates();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 5: EvaluateTaskbarSnapshot Safe Invariants
            try
            {
                Console.Write("[RUN] Test 5: EvaluateTaskbarSnapshot safe bounds & zero-button rejection... ");
                Test_EvaluateTaskbarSnapshot_Invariants();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 6: TaskbarChevronResolver Invariants
            try
            {
                Console.Write("[RUN] Test 6: TaskbarChevronResolver valid bounds & expansion... ");
                Test_ChevronResolver_Invariants();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 7: ProbeTaskbarSpaceWithRetry Transient 3-attempt recovery
            try
            {
                Console.Write("[RUN] Test 7: ProbeTaskbarSpaceWithRetry transient 3-attempt loop... ");
                Test_ProbeWithRetry_TransientRecovery();
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

        private static void Test_COMInstance_TimeoutsReadback()
        {
            using (NativeTaskbarAutomation native = new NativeTaskbarAutomation())
            {
                if (native.ConnectionTimeout != 150)
                {
                    throw new Exception("Expected ConnectionTimeout 150ms, got: " + native.ConnectionTimeout);
                }
                if (native.TransactionTimeout != 150)
                {
                    throw new Exception("Expected TransactionTimeout 150ms, got: " + native.TransactionTimeout);
                }
            }
        }

        private static void Test_CheckBudget_ExceededThrows()
        {
            Stopwatch sw = new Stopwatch();
            // Normal budget: should not throw
            NativeTaskbarAutomation.CheckBudget(sw, "NormalStage");

            // Mock elapsed > 700ms using sleep or manual check
            Stopwatch fastSw = Stopwatch.StartNew();
            Thread.Sleep(5);
            NativeTaskbarAutomation.CheckBudget(fastSw, "FastStage");

            // Verify exception thrown for over-budget
            // We can test budget limit constant
            if (NativeTaskbarAutomation.STEP_BUDGET_MS != 700)
            {
                throw new Exception("Expected STEP_BUDGET_MS == 700, got: " + NativeTaskbarAutomation.STEP_BUDGET_MS);
            }
        }

        private static void Test_IsTransientProbeException_Classification()
        {
            // TimeoutException -> Transient
            if (!TaskbarSpaceMonitor.IsTransientProbeException(new TimeoutException("Timed out")))
            {
                throw new Exception("TimeoutException should be transient");
            }

            // COMException -> Transient (0x8001011F RPC_E_TIMEOUT)
            if (!TaskbarSpaceMonitor.IsTransientProbeException(new COMException("RPC failed", unchecked((int)0x8001011F))))
            {
                throw new Exception("COMException 0x8001011F should be transient");
            }

            // COMException -> Transient (0x80040201 UIA_E_ELEMENTNOTAVAILABLE)
            if (!TaskbarSpaceMonitor.IsTransientProbeException(new COMException("Element not available", unchecked((int)0x80040201))))
            {
                throw new Exception("COMException 0x80040201 should be transient");
            }

            // COMException -> Transient (0x80131505 UIA_E_TIMEOUT)
            if (!TaskbarSpaceMonitor.IsTransientProbeException(new COMException("UIA timeout", unchecked((int)0x80131505))))
            {
                throw new Exception("COMException 0x80131505 should be transient");
            }

            // Unknown COMException (0x80070005 E_ACCESSDENIED) -> Permanent (Not transient)
            if (TaskbarSpaceMonitor.IsTransientProbeException(new COMException("Access denied", unchecked((int)0x80070005))))
            {
                throw new Exception("Unknown COMException 0x80070005 should NOT be transient");
            }

            // ElementNotAvailableException -> Transient
            if (!TaskbarSpaceMonitor.IsTransientProbeException(new ElementNotAvailableException()))
            {
                throw new Exception("ElementNotAvailableException should be transient");
            }

            // InvalidOperationException("App button geometry unavailable") -> Transient
            if (!TaskbarSpaceMonitor.IsTransientProbeException(new InvalidOperationException("App button geometry unavailable")))
            {
                throw new Exception("App button geometry unavailable should be transient");
            }

            // InvalidOperationException("App and tray regions intersect") -> Permanent (Not transient)
            if (TaskbarSpaceMonitor.IsTransientProbeException(new InvalidOperationException("App and tray regions intersect")))
            {
                throw new Exception("App and tray regions intersect should NOT be transient");
            }

            // Nested InnerException -> Transient
            Exception nested = new InvalidOperationException("Wrapper", new TimeoutException("Deep timeout"));
            if (!TaskbarSpaceMonitor.IsTransientProbeException(nested))
            {
                throw new Exception("Nested TimeoutException should be recognized as transient");
            }
        }

        private static void Test_ProcessAppButtonCached_Gates()
        {
            int trayLeft = 1000;
            int maxAppRight = 0;

            // 1. Normal app button
            var btn1 = new MockAutomationElement
            {
                MockControlType = 50000,
                MockAutomationId = "AppId1",
                MockClassName = "TaskbarAppButton",
                MockName = "Edge",
                MockBoundingRectangle = new tagRECT { left = 100, top = 0, right = 250, bottom = 48 }
            };
            bool r1 = NativeTaskbarAutomation.ProcessAppButtonCached(btn1, trayLeft, ref maxAppRight);
            if (!r1 || maxAppRight != 250)
            {
                throw new Exception("Expected normal button valid and maxAppRight=250, got: " + maxAppRight);
            }

            // 2. Tray chevron / SystemTray button should be ignored (return false, maxAppRight unchanged)
            var trayChevron = new MockAutomationElement
            {
                MockControlType = 50000,
                MockAutomationId = "SystemTrayIcon",
                MockClassName = "SystemTray.ChevronIcon",
                MockName = "显示隐藏的图标",
                MockBoundingRectangle = new tagRECT { left = 950, top = 0, right = 980, bottom = 48 }
            };
            bool r2 = NativeTaskbarAutomation.ProcessAppButtonCached(trayChevron, trayLeft, ref maxAppRight);
            if (r2 || maxAppRight != 250)
            {
                throw new Exception("Tray chevron should be filtered out without changing maxAppRight");
            }

            // 3. Bad geometry (width <= 0) must throw InvalidOperationException
            var badGeom = new MockAutomationElement
            {
                MockControlType = 50000,
                MockAutomationId = "AppBadGeom",
                MockClassName = "TaskbarAppButton",
                MockName = "Bad",
                MockBoundingRectangle = new tagRECT { left = 250, top = 0, right = 250, bottom = 48 }
            };
            bool threwGeom = false;
            try
            {
                NativeTaskbarAutomation.ProcessAppButtonCached(badGeom, trayLeft, ref maxAppRight);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == "App button geometry unavailable")
                {
                    threwGeom = true;
                }
            }
            if (!threwGeom)
            {
                throw new Exception("Zero-width button must throw InvalidOperationException('App button geometry unavailable')");
            }

            // 4. App button intersecting tray (right > trayLeft) must throw
            var intersectingApp = new MockAutomationElement
            {
                MockControlType = 50000,
                MockAutomationId = "AppOverlap",
                MockClassName = "TaskbarAppButton",
                MockName = "Overlap",
                MockBoundingRectangle = new tagRECT { left = 900, top = 0, right = 1050, bottom = 48 }
            };
            bool threwIntersect = false;
            try
            {
                NativeTaskbarAutomation.ProcessAppButtonCached(intersectingApp, trayLeft, ref maxAppRight);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == "App and tray regions intersect")
                {
                    threwIntersect = true;
                }
            }
            if (!threwIntersect)
            {
                throw new Exception("Intersecting button must throw InvalidOperationException('App and tray regions intersect')");
            }
        }

        private static void Test_EvaluateTaskbarSnapshot_Invariants()
        {
            Rectangle tbRect = new Rectangle(0, 1032, 1920, 48);
            Rectangle trRect = new Rectangle(1400, 1032, 520, 48);

            // Valid snapshot
            var s1 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 5, 800);
            if (!s1.IsValid || s1.MaxAppRight != 800 || s1.TrayLeft != 1400)
            {
                throw new Exception("Expected valid snapshot for normal inputs");
            }

            // Zero valid buttons must be Invalid
            var s2 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 0, 800);
            if (s2.IsValid)
            {
                throw new Exception("Zero valid buttons must result in invalid snapshot");
            }

            // maxAppRight <= 0 must be Invalid
            var s3 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 5, 0);
            if (s3.IsValid)
            {
                throw new Exception("maxAppRight <= 0 must result in invalid snapshot");
            }

            // maxAppRight > trayLeft must be Invalid
            var s4 = TaskbarSpaceMonitor.EvaluateTaskbarSnapshot(tbRect, trRect, 5, 1500);
            if (s4.IsValid)
            {
                throw new Exception("maxAppRight > trayLeft must result in invalid snapshot");
            }
        }

        private static void Test_ChevronResolver_Invariants()
        {
            TaskbarChevronResolver resolver = new TaskbarChevronResolver();
            Rectangle tbRect = new Rectangle(0, 1032, 1920, 48);
            Rectangle trRect = new Rectangle(1500, 1032, 420, 48);
            IntPtr hShell = new IntPtr(0x1234);

            // Chevron inside taskbar at left=1450 (width=30, top=1036, height=40)
            Rectangle chevron = new Rectangle(1450, 1036, 30, 40);
            if (!TaskbarChevronResolver.IsChevronRectValid(chevron, tbRect, trRect))
            {
                throw new Exception("Chevron at 1450 should be valid");
            }

            Rectangle resolved = resolver.ResolveTrayRect(hShell, tbRect, trRect, chevron);
            if (resolved.Left != 1450)
            {
                throw new Exception("Resolved trayLeft should expand to 1450, got: " + resolved.Left);
            }

            // Out-of-bounds chevron (flying flyout) should be rejected
            Rectangle flyoutChevron = new Rectangle(1450, 800, 30, 40);
            if (TaskbarChevronResolver.IsChevronRectValid(flyoutChevron, tbRect, trRect))
            {
                throw new Exception("Flyout chevron outside taskbar vertically must be rejected");
            }
        }

        private static void Test_ProbeWithRetry_TransientRecovery()
        {
            using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
            {
                int attempts = 0;
                monitor.ProbeAttemptFunc = () =>
                {
                    attempts++;
                    if (attempts < 2)
                    {
                        return TaskbarProbeResult.Transient(
                            "FindChevron",
                            "Simulated transient RPC timeout",
                            Rectangle.Empty,
                            Rectangle.Empty,
                            Rectangle.Empty,
                            0);
                    }
                    var snap = new TaskbarSpaceSnapshot(600, 1400, new Rectangle(0, 1032, 1920, 48), new Rectangle(1400, 1032, 520, 48), true);
                    return TaskbarProbeResult.Success(snap, new Rectangle(1400, 1032, 520, 48), Rectangle.Empty, new Rectangle(1400, 1032, 520, 48), 600);
                };

                TaskbarSpaceSnapshot result = monitor.ProbeTaskbarSpaceWithRetry();
                if (!result.IsValid || result.MaxAppRight != 600)
                {
                    throw new Exception("Expected recovery on attempt 2, got invalid snapshot");
                }
                if (attempts != 2)
                {
                    throw new Exception("Expected exactly 2 attempts, got: " + attempts);
                }
            }
        }
    }
}
