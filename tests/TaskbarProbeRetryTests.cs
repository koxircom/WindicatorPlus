using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class TaskbarProbeRetryTests
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] TaskbarProbeRetry Tests (Pure Delegate, Headless)");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            string testDir = @"C:\tmp\Windicator138";
            if (!Directory.Exists(testDir))
            {
                Directory.CreateDirectory(testDir);
            }

            // Test 1: transient -> valid 恢复成功，仅发布 1 次快照且无 invalid 清屏
            try
            {
                Console.Write("[RUN] Test 1: Transient -> Valid recovered, exactly 1 publish, no invalid... ");
                Test_TransientThenValid_SinglePublish(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 2: 3次尝试全部失败，返回 Invalid，包含 2 次 40ms 等待
            try
            {
                Console.Write("[RUN] Test 2: Three consecutive transient failures result in Invalid... ");
                Test_ThreeFailures_ResultsInInvalid(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 3: transient 失败不刷新旧快照 timestamp
            try
            {
                Console.Write("[RUN] Test 3: Transient failure does not refresh snapshot timestamp... ");
                Test_TransientFailure_DoesNotRefreshSnapshotTimestamp();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 4: permanent 错误仅执行 1 次，不重试
            try
            {
                Console.Write("[RUN] Test 4: Permanent failure attempts once without retry... ");
                Test_PermanentFailure_NoRetry(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 5: stop 信号立即中断重试
            try
            {
                Console.Write("[RUN] Test 5: Stop event immediately interrupts retry loop... ");
                Test_StopInterruptsRetry();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 6: 旧快照自然老化与真实 2sec 守卫语义
            try
            {
                Console.Write("[RUN] Test 6: Old snapshot still ages, strictly enforcing 2sec guard... ");
                Test_SnapshotAges_RealTwoSecondGuard();
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 7: ElementNotAvailableException 准确识别为 transient 并触发重试
            try
            {
                Console.Write("[RUN] Test 7: ElementNotAvailableException identified as transient... ");
                Test_ElementNotAvailableException_TransientRetry(testDir);
                Console.WriteLine("PASS");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                failed++;
            }

            // Test 8: ProbeOnce 独立无 Stop 模式的有界重试
            try
            {
                Console.Write("[RUN] Test 8: ProbeOnce standalone bounded retry... ");
                Test_ProbeOnce_StandaloneRetry();
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

            return failed > 0 ? 1 : 0;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed: " + message);
            }
        }

        private static string[] ReadTraceLines(string path)
        {
            RuntimeTrace.Flush();
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                List<string> lines = new List<string>();
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    lines.Add(line);
                }
                return lines.ToArray();
            }
        }

        private static void Test_TransientThenValid_SinglePublish(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_retry_transient_then_valid.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);
            RuntimeTrace.InitForTesting(traceFile, 1000, 60000);

            try
            {
                using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(100))
                {
                    int probeCalls = 0;
                    List<int> waitDelays = new List<int>();
                    List<TaskbarSpaceSnapshot> published = new List<TaskbarSpaceSnapshot>();

                    Rectangle tbRect = new Rectangle(0, 2088, 3840, 72);
                    Rectangle trRect = new Rectangle(3061, 2088, 779, 72);
                    Rectangle chevronRect = new Rectangle(3061, 2088, 47, 72);
                    TaskbarSpaceSnapshot validSnap = new TaskbarSpaceSnapshot(828, 3061, tbRect, trRect, true);

                    monitor.ProbeAttemptFunc = () =>
                    {
                        probeCalls++;
                        if (probeCalls == 1)
                        {
                            return TaskbarProbeResult.Transient(
                                "ProcessAppButton",
                                "InvalidOperationException: App button geometry unavailable",
                                trRect,
                                chevronRect,
                                trRect,
                                215);
                        }
                        return TaskbarProbeResult.Success(validSnap, trRect, chevronRect, trRect, 828);
                    };

                    monitor.WaitFunc = (delay) =>
                    {
                        waitDelays.Add(delay);
                        return false; // 不中止，正常继续
                    };

                    monitor.SpaceChanged += (snap) =>
                    {
                        published.Add(snap);
                    };

                    // 执行一次探针重试
                    TaskbarSpaceSnapshot result = monitor.ProbeOnce();

                    Assert(result.IsValid, "Result snapshot should be valid");
                    Assert(result.MaxAppRight == 828, "Result MaxAppRight should be 828");
                    Assert(probeCalls == 2, "Probe should have been called exactly twice (1 initial + 1 retry)");
                    Assert(waitDelays.Count == 1, "Wait should have been called exactly once");
                    Assert(waitDelays[0] == 40, "Wait delay should be 40ms");

                    // 启动 MTA 工作线程验证实际事件发布
                    probeCalls = 0;
                    waitDelays.Clear();
                    monitor.Start();
                    Thread.Sleep(80); // 等待一次 worker 循环处理
                    monitor.Stop();

                    Assert(published.Count == 1, "SpaceChanged should publish exactly once across transient recovery, but was " + published.Count);
                    Assert(published[0].IsValid, "Published snapshot should be valid");
                    Assert(published[0].MaxAppRight == 828, "Published snapshot MaxAppRight should be 828");
                }

                string[] lines = ReadTraceLines(traceFile);
                bool hasRetryAttempt = false;
                bool hasRetryRecovered = false;
                bool hasProbeFail = false;

                foreach (string line in lines)
                {
                    if (line.Contains("\"cat\":\"probe_retry_attempt\""))
                    {
                        hasRetryAttempt = true;
                        Assert(line.Contains("\"attempt\":1"), "Retry attempt should be 1");
                        Assert(line.Contains("\"delayMs\":40"), "Delay should be 40ms");
                        Assert(line.Contains("\"stage\":\"ProcessAppButton\""), "Stage should be ProcessAppButton");
                    }
                    if (line.Contains("\"cat\":\"probe_retry_result\""))
                    {
                        hasRetryRecovered = true;
                        Assert(line.Contains("\"recovered\":true"), "Retry result should be recovered:true");
                        Assert(line.Contains("\"attempts\":2"), "Attempts should be 2");
                    }
                    if (line.Contains("\"cat\":\"probe_fail\""))
                    {
                        hasProbeFail = true;
                    }
                }

                Assert(hasRetryAttempt, "Trace must log probe_retry_attempt");
                Assert(hasRetryRecovered, "Trace must log probe_retry_result as recovered");
                Assert(!hasProbeFail, "Transient recovery must NOT log probe_fail");
            }
            finally
            {
                RuntimeTrace.ResetForTesting();
            }
        }

        private static void Test_ThreeFailures_ResultsInInvalid(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_retry_three_failures.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);
            RuntimeTrace.InitForTesting(traceFile, 1000, 60000);

            try
            {
                using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
                {
                    int probeCalls = 0;
                    List<int> waitDelays = new List<int>();

                    Rectangle trRect = new Rectangle(3061, 2088, 779, 72);
                    Rectangle chevronRect = new Rectangle(3061, 2088, 47, 72);

                    monitor.ProbeAttemptFunc = () =>
                    {
                        probeCalls++;
                        return TaskbarProbeResult.Transient(
                            "ProcessAppButton",
                            "InvalidOperationException: App button geometry unavailable",
                            trRect,
                            chevronRect,
                            trRect,
                            215);
                    };

                    monitor.WaitFunc = (delay) =>
                    {
                        waitDelays.Add(delay);
                        return false;
                    };

                    TaskbarSpaceSnapshot result = monitor.ProbeOnce();

                    Assert(!result.IsValid, "Result should be Invalid after 3 failures");
                    Assert(probeCalls == 3, "Probe should have been attempted exactly 3 times");
                    Assert(waitDelays.Count == 2, "Wait should have occurred exactly twice (2 * 40ms)");
                    Assert(waitDelays[0] == 40 && waitDelays[1] == 40, "Wait delays must both be 40ms");
                }

                string[] lines = ReadTraceLines(traceFile);
                int retryAttempts = 0;
                bool hasFailedResult = false;
                bool hasProbeFail = false;

                foreach (string line in lines)
                {
                    if (line.Contains("\"cat\":\"probe_retry_attempt\""))
                    {
                        retryAttempts++;
                    }
                    if (line.Contains("\"cat\":\"probe_retry_result\""))
                    {
                        hasFailedResult = true;
                        Assert(line.Contains("\"recovered\":false"), "Result must be recovered:false");
                        Assert(line.Contains("\"attempts\":3"), "Total attempts must be 3");
                    }
                    if (line.Contains("\"cat\":\"probe_fail\""))
                    {
                        hasProbeFail = true;
                        Assert(line.Contains("\"stage\":\"ProcessAppButton\""), "Probe fail stage must be ProcessAppButton");
                    }
                }

                Assert(retryAttempts == 3, "Should log 3 retry attempts before exhausting budget");
                Assert(hasFailedResult, "Should log retry failure result");
                Assert(hasProbeFail, "Exhausted retry must log probe_fail");
            }
            finally
            {
                RuntimeTrace.ResetForTesting();
            }
        }

        private static void Test_TransientFailure_DoesNotRefreshSnapshotTimestamp()
        {
            using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
            {
                Rectangle tbRect = new Rectangle(0, 2088, 3840, 72);
                Rectangle trRect = new Rectangle(3061, 2088, 779, 72);

                long originalTicks = Stopwatch.GetTimestamp() - 50000;
                TaskbarSpaceSnapshot initialSnap = new TaskbarSpaceSnapshot(828, 3061, tbRect, trRect, true, originalTicks);

                monitor.ProbeAttemptFunc = () =>
                {
                    return TaskbarProbeResult.Success(initialSnap, trRect, Rectangle.Empty, trRect, 828);
                };

                // 首次成功 Probe
                TaskbarSpaceSnapshot first = monitor.ProbeOnce();
                Assert(first.IsValid, "First snapshot should be valid");
                Assert(first.TimestampTicks == originalTicks, "Original timestamp ticks must match");

                // 后续 Probe 遇到 Transient failure
                monitor.ProbeAttemptFunc = () =>
                {
                    return TaskbarProbeResult.Transient(
                        "ProcessAppButton",
                        "InvalidOperationException: App button geometry unavailable",
                        trRect,
                        Rectangle.Empty,
                        trRect,
                        215);
                };
                monitor.WaitFunc = (delay) => false;

                TaskbarSpaceSnapshot failed = monitor.ProbeOnce();
                Assert(!failed.IsValid, "Failed probe must return Invalid");

                // 验证原有的快照时间戳绝不能被篡改/假刷新
                Assert(first.TimestampTicks == originalTicks, "Snapshot timestamp must not be refreshed during transient failures");
            }
        }

        private static void Test_PermanentFailure_NoRetry(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_retry_permanent.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);
            RuntimeTrace.InitForTesting(traceFile, 1000, 60000);

            try
            {
                using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
                {
                    int probeCalls = 0;
                    int waitCalls = 0;

                    monitor.ProbeAttemptFunc = () =>
                    {
                        probeCalls++;
                        // 真实 intersection 或 FindShell 均为 permanent failure
                        return TaskbarProbeResult.Permanent("FindShell", null, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                    };

                    monitor.WaitFunc = (delay) =>
                    {
                        waitCalls++;
                        return false;
                    };

                    TaskbarSpaceSnapshot result = monitor.ProbeOnce();

                    Assert(!result.IsValid, "Result must be Invalid");
                    Assert(probeCalls == 1, "Permanent failure must attempt exactly once without retry");
                    Assert(waitCalls == 0, "Permanent failure must not wait");
                }

                string[] lines = ReadTraceLines(traceFile);
                bool hasProbeFail = false;
                bool hasRetry = false;

                foreach (string line in lines)
                {
                    if (line.Contains("\"cat\":\"probe_fail\""))
                    {
                        hasProbeFail = true;
                        Assert(line.Contains("\"stage\":\"FindShell\""), "Fail stage should be FindShell");
                    }
                    if (line.Contains("probe_retry"))
                    {
                        hasRetry = true;
                    }
                }

                Assert(hasProbeFail, "Permanent failure must log probe_fail");
                Assert(!hasRetry, "Permanent failure must not log any retry events");
            }
            finally
            {
                RuntimeTrace.ResetForTesting();
            }
        }

        private static void Test_StopInterruptsRetry()
        {
            using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
            {
                int probeCalls = 0;
                monitor.ProbeAttemptFunc = () =>
                {
                    probeCalls++;
                    return TaskbarProbeResult.Transient("ProcessAppButton", "InvalidOperationException: App button geometry unavailable", Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                };

                monitor.WaitFunc = (delay) =>
                {
                    // 模拟在等待重试时接收到停止信号
                    monitor.Stop();
                    return true; // 中止
                };

                TaskbarSpaceSnapshot result = monitor.ProbeOnce();

                Assert(!result.IsValid, "Result must be Invalid when stopped");
                Assert(probeCalls == 1, "Should immediately interrupt on stop without continuing further attempts");
            }
        }

        private static void Test_SnapshotAges_RealTwoSecondGuard()
        {
            Rectangle tbRect = new Rectangle(0, 2088, 3840, 72);
            Rectangle trRect = new Rectangle(3061, 2088, 779, 72);

            long nowTicks = Stopwatch.GetTimestamp();

            // 新快照应该在 2000ms 内有效
            TaskbarSpaceSnapshot freshSnap = new TaskbarSpaceSnapshot(828, 3061, tbRect, trRect, true, nowTicks);
            Assert(freshSnap.IsFresh(2000), "Newly created snapshot must be fresh");

            // 1 秒前的快照仍然有效
            long oneSecOldTicks = nowTicks - (Stopwatch.Frequency * 1000 / 1000);
            TaskbarSpaceSnapshot oneSecSnap = new TaskbarSpaceSnapshot(828, 3061, tbRect, trRect, true, oneSecOldTicks);
            Assert(oneSecSnap.IsFresh(2000), "1 second old snapshot must be fresh within 2000ms window");

            // 2.5 秒前的快照已经过期，IsFresh(2000) 必须返回 false
            long twoAndHalfSecOldTicks = nowTicks - (Stopwatch.Frequency * 2500 / 1000);
            TaskbarSpaceSnapshot expiredSnap = new TaskbarSpaceSnapshot(828, 3061, tbRect, trRect, true, twoAndHalfSecOldTicks);
            Assert(!expiredSnap.IsFresh(2000), "2.5 second old snapshot must NOT be fresh (strictly enforcing 2sec guard)");

            // Invalid 快照永不 fresh
            TaskbarSpaceSnapshot invalidSnap = TaskbarSpaceSnapshot.Invalid();
            Assert(!invalidSnap.IsFresh(2000), "Invalid snapshot must never be fresh");
        }

        private static void Test_ElementNotAvailableException_TransientRetry(string testDir)
        {
            string traceFile = Path.Combine(testDir, "test_retry_element_not_available.jsonl");
            if (File.Exists(traceFile)) File.Delete(traceFile);
            RuntimeTrace.InitForTesting(traceFile, 1000, 60000);

            try
            {
                using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
                {
                    int probeCalls = 0;
                    Rectangle tbRect = new Rectangle(0, 2088, 3840, 72);
                    Rectangle trRect = new Rectangle(3061, 2088, 779, 72);
                    TaskbarSpaceSnapshot validSnap = new TaskbarSpaceSnapshot(1013, 3061, tbRect, trRect, true);

                    monitor.ProbeAttemptFunc = () =>
                    {
                        probeCalls++;
                        if (probeCalls == 1)
                        {
                            // 模拟 UIA ElementNotAvailableException
                            throw new ElementNotAvailableException("UIA element detached during layout transition");
                        }
                        return TaskbarProbeResult.Success(validSnap, trRect, Rectangle.Empty, trRect, 1013);
                    };

                    monitor.WaitFunc = (delay) => false;

                    TaskbarSpaceSnapshot result = monitor.ProbeOnce();

                    Assert(result.IsValid, "Should recover after ElementNotAvailableException transient");
                    Assert(result.MaxAppRight == 1013, "MaxAppRight should be 1013");
                    Assert(probeCalls == 2, "Should have retried once");
                }

                string[] lines = ReadTraceLines(traceFile);
                bool hasRetry = false;
                bool hasRecovered = false;

                foreach (string line in lines)
                {
                    if (line.Contains("\"cat\":\"probe_retry_attempt\"") && line.Contains("ElementNotAvailableException"))
                    {
                        hasRetry = true;
                    }
                    if (line.Contains("\"cat\":\"probe_retry_result\"") && line.Contains("\"recovered\":true"))
                    {
                        hasRecovered = true;
                    }
                }

                Assert(hasRetry, "Should log ElementNotAvailableException retry attempt");
                Assert(hasRecovered, "Should log recovered result");
            }
            finally
            {
                RuntimeTrace.ResetForTesting();
            }
        }

        private static void Test_ProbeOnce_StandaloneRetry()
        {
            using (TaskbarSpaceMonitor monitor = new TaskbarSpaceMonitor(500))
            {
                int attempts = 0;
                List<int> delays = new List<int>();

                Rectangle tbRect = new Rectangle(0, 2088, 3840, 72);
                Rectangle trRect = new Rectangle(3061, 2088, 779, 72);
                TaskbarSpaceSnapshot validSnap = new TaskbarSpaceSnapshot(900, 3061, tbRect, trRect, true);

                monitor.ProbeAttemptFunc = () =>
                {
                    attempts++;
                    if (attempts <= 2)
                    {
                        return TaskbarProbeResult.Transient("ProcessAppButton", "InvalidOperationException: App button geometry unavailable", trRect, Rectangle.Empty, trRect, 200);
                    }
                    return TaskbarProbeResult.Success(validSnap, trRect, Rectangle.Empty, trRect, 900);
                };

                monitor.WaitFunc = (delay) =>
                {
                    delays.Add(delay);
                    return false;
                };

                // 在未调用 Start() 的纯单次探测场景下运行
                TaskbarSpaceSnapshot snap = monitor.ProbeOnce();

                Assert(snap.IsValid, "Should succeed on 3rd attempt");
                Assert(attempts == 3, "Should take exactly 3 attempts");
                Assert(delays.Count == 2, "Should wait twice");
                Assert(delays[0] == 40 && delays[1] == 40, "Delays should be 40ms");
            }
        }
    }
}
