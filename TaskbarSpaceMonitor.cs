using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Automation;

namespace WindicatorPlus
{
    public class TaskbarSpaceSnapshot
    {
        public int MaxAppRight { get; private set; }
        public int TrayLeft { get; private set; }
        public Rectangle TaskbarRect { get; private set; }
        public Rectangle TrayRect { get; private set; }
        public bool IsValid { get; private set; }
        public long TimestampTicks { get; private set; }

        public TaskbarSpaceSnapshot(int maxAppRight, int trayLeft, Rectangle taskbarRect, Rectangle trayRect, bool isValid)
            : this(maxAppRight, trayLeft, taskbarRect, trayRect, isValid, Stopwatch.GetTimestamp())
        {
        }

        public TaskbarSpaceSnapshot(int maxAppRight, int trayLeft, Rectangle taskbarRect, Rectangle trayRect, bool isValid, long timestampTicks)
        {
            MaxAppRight = maxAppRight;
            TrayLeft = trayLeft;
            TaskbarRect = taskbarRect;
            TrayRect = trayRect;
            IsValid = isValid;
            TimestampTicks = timestampTicks;
        }

        public static TaskbarSpaceSnapshot Invalid()
        {
            return new TaskbarSpaceSnapshot(0, 0, Rectangle.Empty, Rectangle.Empty, false, Stopwatch.GetTimestamp());
        }

        public bool IsFresh(long maxAgeMs = 2000)
        {
            if (!IsValid) return false;
            long elapsedTicks = Stopwatch.GetTimestamp() - TimestampTicks;
            long elapsedMs = (elapsedTicks * 1000) / Stopwatch.Frequency;
            return elapsedTicks >= 0 && elapsedMs <= maxAgeMs;
        }
    }

    internal enum TaskbarProbeStatus
    {
        Success,
        TransientFailure,
        PermanentFailure
    }

    internal class TaskbarProbeResult
    {
        public TaskbarProbeStatus Status { get; private set; }
        public TaskbarSpaceSnapshot Snapshot { get; private set; }
        public string Stage { get; private set; }
        public string ExceptionDetail { get; private set; }
        public Rectangle NativeTrayRect { get; private set; }
        public Rectangle ObservedChevronRect { get; private set; }
        public Rectangle EffectiveTrayRect { get; private set; }
        public int MaxAppRight { get; private set; }

        private TaskbarProbeResult(
            TaskbarProbeStatus status,
            TaskbarSpaceSnapshot snapshot,
            string stage,
            string exceptionDetail,
            Rectangle nativeTrayRect,
            Rectangle observedChevronRect,
            Rectangle effectiveTrayRect,
            int maxAppRight)
        {
            Status = status;
            Snapshot = snapshot ?? TaskbarSpaceSnapshot.Invalid();
            Stage = stage ?? "";
            ExceptionDetail = exceptionDetail ?? "";
            NativeTrayRect = nativeTrayRect;
            ObservedChevronRect = observedChevronRect;
            EffectiveTrayRect = effectiveTrayRect;
            MaxAppRight = maxAppRight;
        }

        public static TaskbarProbeResult Success(
            TaskbarSpaceSnapshot snapshot,
            Rectangle nativeTrayRect,
            Rectangle observedChevronRect,
            Rectangle effectiveTrayRect,
            int maxAppRight)
        {
            return new TaskbarProbeResult(
                TaskbarProbeStatus.Success,
                snapshot,
                "",
                "",
                nativeTrayRect,
                observedChevronRect,
                effectiveTrayRect,
                maxAppRight);
        }

        public static TaskbarProbeResult Transient(
            string stage,
            string exceptionDetail,
            Rectangle nativeTrayRect,
            Rectangle observedChevronRect,
            Rectangle effectiveTrayRect,
            int maxAppRight)
        {
            return new TaskbarProbeResult(
                TaskbarProbeStatus.TransientFailure,
                TaskbarSpaceSnapshot.Invalid(),
                stage,
                exceptionDetail,
                nativeTrayRect,
                observedChevronRect,
                effectiveTrayRect,
                maxAppRight);
        }

        public static TaskbarProbeResult Permanent(
            string stage,
            string exceptionDetail,
            Rectangle nativeTrayRect,
            Rectangle observedChevronRect,
            Rectangle effectiveTrayRect,
            int maxAppRight)
        {
            return new TaskbarProbeResult(
                TaskbarProbeStatus.PermanentFailure,
                TaskbarSpaceSnapshot.Invalid(),
                stage,
                exceptionDetail,
                nativeTrayRect,
                observedChevronRect,
                effectiveTrayRect,
                maxAppRight);
        }
    }

    public interface ITaskbarSpaceProvider : IDisposable
    {
        TaskbarSpaceSnapshot GetLatestSnapshot();
        event Action<TaskbarSpaceSnapshot> SpaceChanged;
        void Start();
        void Stop();
    }

    public class TaskbarSpaceMonitor : ITaskbarSpaceProvider
    {
        private const int STATE_INIT = 0;
        private const int STATE_RUNNING = 1;
        private const int STATE_STOPPED = 2;

        public const int MAX_PROBE_ATTEMPTS = 3;
        public const int PROBE_RETRY_INTERVAL_MS = 40;
        public const long TOTAL_PROBE_BUDGET_MS = 900;

        public const int HRESULT_UIA_E_ELEMENTNOTAVAILABLE = unchecked((int)0x80040201);
        public const int HRESULT_UIA_E_TIMEOUT = unchecked((int)0x80131505);
        public const int HRESULT_RPC_E_TIMEOUT = unchecked((int)0x8001011F);

        internal Func<TaskbarProbeResult> ProbeAttemptFunc { get; set; }
        internal Func<int, bool> WaitFunc { get; set; }

        private int _state = STATE_INIT;
        private Thread _workerThread;
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private TaskbarSpaceSnapshot _latestSnapshot = TaskbarSpaceSnapshot.Invalid();
        private readonly object _snapshotLock = new object();
        private readonly int _pollIntervalMs;
        private Rectangle _lastObservedChevron = Rectangle.Empty;
        private Rectangle _lastNativeTrayRect = Rectangle.Empty;
        private Rectangle _lastEffectiveTrayRect = Rectangle.Empty;
        private int _lastValidButtonsCount = 0;

        private struct ProbeDiagnosticKey
        {
            public bool IsValid;
            public Rectangle TaskbarRect;
            public Rectangle NativeTrayRect;
            public Rectangle ObservedChevronRect;
            public Rectangle EffectiveTrayRect;
            public int MaxAppRight;
            public int ValidButtonsCount;
        }
        private ProbeDiagnosticKey _lastProbeDiagKey;
        private bool _hasProbeDiagKey = false;

        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

        private readonly TaskbarChevronResolver _chevronResolver = new TaskbarChevronResolver();

        internal TaskbarChevronResolver ChevronResolver
        {
            get { return _chevronResolver; }
        }

        public TaskbarSpaceMonitor(int pollIntervalMs = 500)
        {
            _pollIntervalMs = Math.Max(100, pollIntervalMs);
        }

        public TaskbarSpaceSnapshot GetLatestSnapshot()
        {
            lock (_snapshotLock)
            {
                return _latestSnapshot;
            }
        }

        public void Start()
        {
            // 不可重启保证：若已启动或已停止，绝不再启动新线程
            if (Interlocked.CompareExchange(ref _state, STATE_RUNNING, STATE_INIT) != STATE_INIT)
            {
                return;
            }

            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "TaskbarSpaceMonitorMTA"
            };
            _workerThread.SetApartmentState(ApartmentState.MTA);
            _workerThread.Start();
        }

        public void Stop()
        {
            // 不可重入保证：仅首次从 RUNNING 跃迁至 STOPPED
            int previous = Interlocked.Exchange(ref _state, STATE_STOPPED);
            if (previous == STATE_INIT)
            {
                _stopEvent.Dispose();
                return;
            }
            if (previous != STATE_RUNNING) return;

            try
            {
                _stopEvent.Set();
            }
            catch (ObjectDisposedException) { }

            Thread t = _workerThread;
            if (t != null && t.IsAlive)
            {
                t.Join(600);
            }
            // 契约：不强杀，signal Dispose 由线程自身 finally 持有释放，防止 UIA 线程 probe 时 WaitOne on disposed event
        }

        public void Dispose()
        {
            Stop();
            // 如果线程从未被启动，finally 不会被执行，此时安全清理 _stopEvent
            if (_workerThread == null)
            {
                try
                {
                    _stopEvent.Dispose();
                }
                catch { }
            }
        }

        private void WorkerLoop()
        {
            NativeTaskbarAutomation nativeAutomation = null;
            try
            {
                try
                {
                    nativeAutomation = new NativeTaskbarAutomation();
                }
                catch (Exception ex)
                {
                    RuntimeTrace.LogProbeFailure("NativeAutomationInit", RuntimeTrace.FormatProbeException(ex), Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                }

                while (Volatile.Read(ref _state) == STATE_RUNNING)
                {
                    TaskbarSpaceSnapshot snap;
                    try
                    {
                        snap = ProbeTaskbarSpaceCore(nativeAutomation);
                    }
                    catch
                    {
                        snap = TaskbarSpaceSnapshot.Invalid();
                    }

                    // 契约：停止后禁止 ProcessNewSnapshot
                    if (Volatile.Read(ref _state) != STATE_RUNNING)
                    {
                        break;
                    }

                    ProcessNewSnapshot(snap);

                    // 契约：停止后禁止 WaitOne disposed
                    if (Volatile.Read(ref _state) != STATE_RUNNING)
                    {
                        break;
                    }

                    try
                    {
                        if (_stopEvent.WaitOne(_pollIntervalMs))
                        {
                            break;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (nativeAutomation != null)
                {
                    try
                    {
                        nativeAutomation.Dispose();
                    }
                    catch { }
                    nativeAutomation = null;
                }

                // 契约：用线程 finally 持有 signal 释放
                try
                {
                    _stopEvent.Dispose();
                }
                catch { }
            }
        }

        public TaskbarSpaceSnapshot ProbeOnce()
        {
            if (ProbeAttemptFunc != null)
            {
                return ProbeTaskbarSpaceCore(null);
            }

            using (NativeTaskbarAutomation nativeAutomation = new NativeTaskbarAutomation())
            {
                return ProbeTaskbarSpaceCore(nativeAutomation);
            }
        }

        private void ProcessNewSnapshot(TaskbarSpaceSnapshot snap)
        {
            // 停止后禁止向外部或内部状态处理新快照
            if (Volatile.Read(ref _state) != STATE_RUNNING) return;

            bool changed = false;
            lock (_snapshotLock)
            {
                if (_latestSnapshot == null ||
                    _latestSnapshot.IsValid != snap.IsValid ||
                    _latestSnapshot.MaxAppRight != snap.MaxAppRight ||
                    _latestSnapshot.TrayLeft != snap.TrayLeft ||
                    _latestSnapshot.TaskbarRect != snap.TaskbarRect ||
                    _latestSnapshot.TrayRect != snap.TrayRect)
                {
                    _latestSnapshot = snap;
                    changed = true;
                }
                else
                {
                    // Refresh timestamp so snapshot remains fresh
                    _latestSnapshot = new TaskbarSpaceSnapshot(
                        snap.MaxAppRight,
                        snap.TrayLeft,
                        snap.TaskbarRect,
                        snap.TrayRect,
                        snap.IsValid,
                        Stopwatch.GetTimestamp());
                }
            }

            if (changed)
            {
                Rectangle nativeTr = snap.IsValid ? _lastNativeTrayRect : Rectangle.Empty;
                Rectangle chevron = snap.IsValid ? _lastObservedChevron : Rectangle.Empty;
                Rectangle effectiveTr = snap.IsValid ? _lastEffectiveTrayRect : snap.TrayRect;
                int btnCount = snap.IsValid ? _lastValidButtonsCount : 0;

                RuntimeTrace.LogProbeChange(
                    snap.IsValid,
                    snap.TaskbarRect,
                    nativeTr,
                    chevron,
                    effectiveTr,
                    snap.MaxAppRight,
                    btnCount);

                _lastProbeDiagKey = new ProbeDiagnosticKey
                {
                    IsValid = snap.IsValid,
                    TaskbarRect = snap.TaskbarRect,
                    NativeTrayRect = nativeTr,
                    ObservedChevronRect = chevron,
                    EffectiveTrayRect = effectiveTr,
                    MaxAppRight = snap.MaxAppRight,
                    ValidButtonsCount = btnCount
                };
                _hasProbeDiagKey = true;
            }

            if (changed && SpaceChanged != null && Volatile.Read(ref _state) == STATE_RUNNING)
            {
                try
                {
                    SpaceChanged(snap);
                }
                catch { }
            }
        }

        public static TaskbarSpaceSnapshot EvaluateTaskbarSnapshot(
            Rectangle tbRect,
            Rectangle trRect,
            int validButtonsCount,
            int maxAppRight)
        {
            if (tbRect.Width <= 0 || tbRect.Height <= 0) return TaskbarSpaceSnapshot.Invalid();
            if (trRect.Width <= 0 || trRect.Height <= 0) return TaskbarSpaceSnapshot.Invalid();

            int trayLeft = trRect.Left;
            if (trayLeft <= tbRect.Left || trayLeft > tbRect.Right) return TaskbarSpaceSnapshot.Invalid();
            if (validButtonsCount <= 0 || maxAppRight <= 0 || maxAppRight > trayLeft) return TaskbarSpaceSnapshot.Invalid();

            return new TaskbarSpaceSnapshot(maxAppRight, trayLeft, tbRect, trRect, true);
        }

        private TaskbarSpaceSnapshot ProbeTaskbarSpaceCore(NativeTaskbarAutomation nativeAutomation = null)
        {
            return ProbeTaskbarSpaceWithRetry(nativeAutomation);
        }

        internal static bool IsTransientProbeException(Exception ex)
        {
            while (ex != null)
            {
                if (ex is ElementNotAvailableException)
                {
                    return true;
                }

                if (ex is TimeoutException)
                {
                    return true;
                }

                if (ex is System.Runtime.InteropServices.COMException)
                {
                    int hr = ((System.Runtime.InteropServices.COMException)ex).ErrorCode;
                    if (hr == HRESULT_UIA_E_ELEMENTNOTAVAILABLE ||
                        hr == HRESULT_UIA_E_TIMEOUT ||
                        hr == HRESULT_RPC_E_TIMEOUT)
                    {
                        return true;
                    }
                    return false;
                }

                if (ex is InvalidOperationException &&
                    string.Equals(ex.Message, "App button geometry unavailable", StringComparison.Ordinal))
                {
                    return true;
                }

                ex = ex.InnerException;
            }

            return false;
        }

        internal bool WaitForRetry(int delayMs)
        {
            if (WaitFunc != null)
            {
                return WaitFunc(delayMs);
            }

            if (Volatile.Read(ref _state) == STATE_RUNNING)
            {
                try
                {
                    return _stopEvent.WaitOne(delayMs);
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }
            else
            {
                try
                {
                    if (_stopEvent != null)
                    {
                        return _stopEvent.WaitOne(delayMs);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }

                Thread.Sleep(delayMs);
                return false;
            }
        }

        internal TaskbarSpaceSnapshot ProbeTaskbarSpaceWithRetry(NativeTaskbarAutomation nativeAutomation = null)
        {
            Stopwatch overallSw = Stopwatch.StartNew();
            TaskbarProbeResult lastResult = null;

            for (int attempt = 1; attempt <= MAX_PROBE_ATTEMPTS; attempt++)
            {
                if (Volatile.Read(ref _state) == STATE_STOPPED)
                {
                    return TaskbarSpaceSnapshot.Invalid();
                }

                if (overallSw.ElapsedMilliseconds > TOTAL_PROBE_BUDGET_MS)
                {
                    RuntimeTrace.LogProbeFailure("OverallBudgetExceeded",
                        string.Format("Total probe budget exceeded: {0}ms", overallSw.ElapsedMilliseconds),
                        Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                    return TaskbarSpaceSnapshot.Invalid();
                }

                TaskbarProbeResult result;
                try
                {
                    result = ProbeAttemptFunc != null ? ProbeAttemptFunc() : ProbeTaskbarSpaceAttempt(nativeAutomation, overallSw);
                }
                catch (Exception ex)
                {
                    string exDetail = RuntimeTrace.FormatProbeException(ex);
                    if (IsTransientProbeException(ex))
                    {
                        result = TaskbarProbeResult.Transient("ProbeExecution", exDetail, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                    }
                    else
                    {
                        RuntimeTrace.LogProbeFailure("ProbeExecution", exDetail, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                        result = TaskbarProbeResult.Permanent("ProbeExecution", exDetail, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, 0);
                    }
                }

                TaskbarProbeResult previousResult = lastResult;
                lastResult = result;

                if (result.Status == TaskbarProbeStatus.Success)
                {
                    if (attempt > 1)
                    {
                        string recoveredStage = !string.IsNullOrEmpty(result.Stage) ? result.Stage : (previousResult != null ? previousResult.Stage : "");
                        RuntimeTrace.LogProbeRetryResult(true, attempt, MAX_PROBE_ATTEMPTS, recoveredStage);
                    }
                    return result.Snapshot;
                }

                if (result.Status == TaskbarProbeStatus.PermanentFailure)
                {
                    if (attempt > 1)
                    {
                        RuntimeTrace.LogProbeRetryResult(false, attempt, MAX_PROBE_ATTEMPTS, result.Stage);
                    }
                    RuntimeTrace.LogProbeFailure(
                        result.Stage,
                        result.ExceptionDetail,
                        result.NativeTrayRect,
                        result.ObservedChevronRect,
                        result.EffectiveTrayRect,
                        result.MaxAppRight);
                    return TaskbarSpaceSnapshot.Invalid();
                }

                // Transient failure
                RuntimeTrace.LogProbeRetryAttempt(attempt, MAX_PROBE_ATTEMPTS, result.Stage, result.ExceptionDetail, PROBE_RETRY_INTERVAL_MS);

                if (attempt < MAX_PROBE_ATTEMPTS)
                {
                    long remaining = TOTAL_PROBE_BUDGET_MS - overallSw.ElapsedMilliseconds;
                    if (remaining <= PROBE_RETRY_INTERVAL_MS)
                    {
                        return TaskbarSpaceSnapshot.Invalid();
                    }

                    bool aborted = WaitForRetry(PROBE_RETRY_INTERVAL_MS);
                    if (aborted || Volatile.Read(ref _state) == STATE_STOPPED)
                    {
                        return TaskbarSpaceSnapshot.Invalid();
                    }
                }
            }

            if (lastResult != null)
            {
                RuntimeTrace.LogProbeRetryResult(false, MAX_PROBE_ATTEMPTS, MAX_PROBE_ATTEMPTS, lastResult.Stage);
                RuntimeTrace.LogProbeFailure(
                    lastResult.Stage,
                    lastResult.ExceptionDetail,
                    lastResult.NativeTrayRect,
                    lastResult.ObservedChevronRect,
                    lastResult.EffectiveTrayRect,
                    lastResult.MaxAppRight);
            }

            return TaskbarSpaceSnapshot.Invalid();
        }

        internal TaskbarProbeResult ProbeTaskbarSpaceAttempt()
        {
            return ProbeTaskbarSpaceAttempt(null, null);
        }

        internal TaskbarProbeResult ProbeTaskbarSpaceAttempt(NativeTaskbarAutomation nativeAutomation, Stopwatch overallSw = null)
        {
            Rectangle nativeTrRect = Rectangle.Empty;
            Rectangle observedChevronRect = Rectangle.Empty;
            Rectangle effectiveTrRect = Rectangle.Empty;
            int maxAppRight = 0;
            int validButtonsCount = 0;

            IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTaskbar == IntPtr.Zero)
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("FindShell", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            NativeMethods.RECT rcTaskbar;
            if (!NativeMethods.GetWindowRect(hTaskbar, out rcTaskbar))
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("GetShellRect", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            Rectangle tbRect = Rectangle.FromLTRB(rcTaskbar.Left, rcTaskbar.Top, rcTaskbar.Right, rcTaskbar.Bottom);
            if (tbRect.Width <= 0 || tbRect.Height <= 0)
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("InvalidShellRect", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            // 契约：缺tray均Invalid
            IntPtr hTray = NativeMethods.FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (hTray == IntPtr.Zero)
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("FindTray", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            NativeMethods.RECT rcTray;
            if (!NativeMethods.GetWindowRect(hTray, out rcTray))
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("GetTrayRect", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            Rectangle trRect = Rectangle.FromLTRB(rcTray.Left, rcTray.Top, rcTray.Right, rcTray.Bottom);
            nativeTrRect = trRect;
            effectiveTrRect = trRect;

            if (trRect.Width <= 0 || trRect.Height <= 0)
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("InvalidTrayRect", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            int trayLeft = trRect.Left;
            if (trayLeft <= tbRect.Left || trayLeft > tbRect.Right)
            {
                ChevronResolver.Reset();
                _lastObservedChevron = Rectangle.Empty;
                return TaskbarProbeResult.Permanent("TrayBoundsOutsideTaskbar", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }

            string currentStage = "FindChevron";
            NativeTaskbarAutomation automationToUse = nativeAutomation;
            bool ownsAutomation = false;
            try
            {
                if (automationToUse == null)
                {
                    automationToUse = new NativeTaskbarAutomation();
                    ownsAutomation = true;
                }

                Stopwatch probeSw = overallSw ?? Stopwatch.StartNew();
                NativeProbeRawResult rawResult = automationToUse.ProbeTaskbarElements(
                    hTaskbar,
                    hTray,
                    trayLeft,
                    probeSw,
                    out currentStage);

                observedChevronRect = rawResult.ObservedChevronRect;
                validButtonsCount = rawResult.ValidButtonsCount;
                maxAppRight = rawResult.MaxAppRight;

                _lastObservedChevron = observedChevronRect;
                trRect = ChevronResolver.ResolveTrayRect(hTaskbar, tbRect, trRect, observedChevronRect);
                effectiveTrRect = trRect;
                trayLeft = trRect.Left;

                // 契约：0个有效button / 无有效buttons 均 Invalid，绝不允许 MaxAppRight=0 return valid=true 把整个taskbar当空白
                if (validButtonsCount == 0 || maxAppRight <= 0)
                {
                    return TaskbarProbeResult.Permanent("NoValidAppButtons", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
                }

                _lastNativeTrayRect = nativeTrRect;
                _lastEffectiveTrayRect = effectiveTrRect;
                _lastValidButtonsCount = validButtonsCount;

                // 原生/chevron变化但effective没变也记录（在probe末用独立diagnostic last-key比较，不影响SpaceChanged）
                if (_hasProbeDiagKey && _lastProbeDiagKey.IsValid)
                {
                    bool nativeOrChevronChanged = (nativeTrRect != _lastProbeDiagKey.NativeTrayRect ||
                                                   observedChevronRect != _lastProbeDiagKey.ObservedChevronRect);
                    bool effectiveUnchanged = (effectiveTrRect == _lastProbeDiagKey.EffectiveTrayRect &&
                                               tbRect == _lastProbeDiagKey.TaskbarRect &&
                                               maxAppRight == _lastProbeDiagKey.MaxAppRight);

                    if (nativeOrChevronChanged && effectiveUnchanged)
                    {
                        RuntimeTrace.LogProbeChange(
                            true,
                            tbRect,
                            nativeTrRect,
                            observedChevronRect,
                            effectiveTrRect,
                            maxAppRight,
                            validButtonsCount);

                        _lastProbeDiagKey.NativeTrayRect = nativeTrRect;
                        _lastProbeDiagKey.ObservedChevronRect = observedChevronRect;
                    }
                }

                TaskbarSpaceSnapshot snapshot = EvaluateTaskbarSnapshot(tbRect, trRect, validButtonsCount, maxAppRight);
                if (!snapshot.IsValid)
                {
                    return TaskbarProbeResult.Permanent("EvaluateSnapshotFailed", null, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
                }

                return TaskbarProbeResult.Success(snapshot, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }
            catch (Exception ex)
            {
                // 契约：读取任一app失败/任何UIA异常均Invalid，不吞错冒充通过
                string exDetail = RuntimeTrace.FormatProbeException(ex);
                if (IsTransientProbeException(ex))
                {
                    return TaskbarProbeResult.Transient(currentStage, exDetail, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
                }

                return TaskbarProbeResult.Permanent(currentStage, exDetail, nativeTrRect, observedChevronRect, effectiveTrRect, maxAppRight);
            }
            finally
            {
                if (ownsAutomation && automationToUse != null)
                {
                    try
                    {
                        automationToUse.Dispose();
                    }
                    catch { }
                }
            }
        }

        private static bool ProcessAppButton(AutomationElement elem, int trayLeft, ref int maxAppRight)
        {
            if (elem == null) return false;

            // 契约：直接app button含Start/TaskView/Window/Appid/Overflow，不要把TaskbarFrame下面仅Group误当按钮
            if (!Equals(elem.Current.ControlType, ControlType.Button))
            {
                return false;
            }

            string autoId = elem.Current.AutomationId ?? "";
            string className = elem.Current.ClassName ?? "";
            string name = elem.Current.Name ?? "";

            // 过滤托盘内部的所有按钮及折叠箭头、菜单项
            if (autoId.IndexOf("SystemTray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                autoId.IndexOf("NotifyItem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("SystemTray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("DropDown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("隐藏的图标", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Show hidden icons", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            System.Windows.Rect rect = elem.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                throw new InvalidOperationException("App button geometry unavailable");
            }

            int r = (int)Math.Ceiling(rect.Right);
            if (r > trayLeft)
            {
                throw new InvalidOperationException("App and tray regions intersect");
            }

            if (r > maxAppRight)
            {
                maxAppRight = r;
            }

            return true;
        }

        private static void TraverseTaskbarTree(
            AutomationElement elem,
            TreeWalker walker,
            int depth,
            int maxDepth,
            int maxNodes,
            ref int visitedNodes,
            int trayLeft,
            ref int maxAppRight,
            ref int validButtonsCount)
        {
            if (elem == null || depth > maxDepth || visitedNodes >= maxNodes)
            {
                return;
            }

            visitedNodes++;

            AutomationElement child = walker.GetFirstChild(elem);
            while (child != null)
            {
                if (visitedNodes >= maxNodes) break;

                if (Equals(child.Current.ControlType, ControlType.Button))
                {
                    if (ProcessAppButton(child, trayLeft, ref maxAppRight))
                    {
                        validButtonsCount++;
                    }
                }
                else
                {
                    string aid = child.Current.AutomationId ?? "";
                    string cls = child.Current.ClassName ?? "";
                    bool isTraySubtree = aid.IndexOf("SystemTray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         cls.IndexOf("TrayNotifyWnd", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!isTraySubtree)
                    {
                        TraverseTaskbarTree(child, walker, depth + 1, maxDepth, maxNodes, ref visitedNodes, trayLeft, ref maxAppRight, ref validButtonsCount);
                    }
                }

                child = walker.GetNextSibling(child);
            }
        }

        private static AutomationElement FindChevronButton(AutomationElement root, int maxDepth = 6, int maxNodes = 200)
        {
            if (root == null) return null;
            TreeWalker walker = TreeWalker.ControlViewWalker;
            Queue<Tuple<AutomationElement, int>> queue = new Queue<Tuple<AutomationElement, int>>();
            queue.Enqueue(Tuple.Create(root, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < maxNodes)
            {
                Tuple<AutomationElement, int> current = queue.Dequeue();
                AutomationElement elem = current.Item1;
                int depth = current.Item2;
                visited++;

                if (depth > 0)
                {
                    if (Equals(elem.Current.ControlType, ControlType.Button))
                    {
                        string aid = elem.Current.AutomationId ?? "";
                        string cls = elem.Current.ClassName ?? "";
                        string nm = elem.Current.Name ?? "";

                        bool isChevron = string.Equals(cls, "SystemTray.ChevronIcon", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nm, "显示隐藏的图标", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nm, "Show hidden icons", StringComparison.OrdinalIgnoreCase) ||
                            (string.Equals(aid, "SystemTrayIcon", StringComparison.OrdinalIgnoreCase) &&
                             (string.Equals(cls, "SystemTray.ChevronIcon", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(nm, "显示隐藏的图标", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(nm, "Show hidden icons", StringComparison.OrdinalIgnoreCase)));

                        if (isChevron)
                        {
                            return elem;
                        }
                    }
                }

                if (depth < maxDepth)
                {
                    AutomationElement child = walker.GetFirstChild(elem);
                    while (child != null && queue.Count + visited < maxNodes)
                    {
                        queue.Enqueue(Tuple.Create(child, depth + 1));
                        child = walker.GetNextSibling(child);
                    }
                }
            }

            return null;
        }

        private static AutomationElement FindTaskbarFrameRepeater(AutomationElement root, int maxDepth)
        {
            TreeWalker walker = TreeWalker.ControlViewWalker;
            Queue<Tuple<AutomationElement, int>> queue = new Queue<Tuple<AutomationElement, int>>();
            queue.Enqueue(Tuple.Create(root, 0));

            int visited = 0;
            while (queue.Count > 0 && visited++ < 200)
            {
                Tuple<AutomationElement, int> current = queue.Dequeue();
                AutomationElement elem = current.Item1;
                int depth = current.Item2;

                if (depth > 0)
                {
                    string aid = elem.Current.AutomationId ?? "";
                    if (string.Equals(aid, "TaskbarFrameRepeater", StringComparison.OrdinalIgnoreCase))
                    {
                        return elem;
                    }
                    if (string.Equals(aid, "TaskbarFrame", StringComparison.OrdinalIgnoreCase))
                    {
                        AutomationElement child = walker.GetFirstChild(elem);
                        while (child != null && visited++ < 200)
                        {
                            string childAid = child.Current.AutomationId ?? "";
                            if (string.Equals(childAid, "TaskbarFrameRepeater", StringComparison.OrdinalIgnoreCase))
                            {
                                return child;
                            }
                            child = walker.GetNextSibling(child);
                        }
                        // ControlView can flatten the repeater, leaving app buttons
                        // directly under TaskbarFrame on Windows 11.
                        return elem;
                    }
                }

                if (depth < maxDepth)
                {
                    AutomationElement child = walker.GetFirstChild(elem);
                    while (child != null && queue.Count + visited < 200)
                    {
                        queue.Enqueue(Tuple.Create(child, depth + 1));
                        child = walker.GetNextSibling(child);
                    }
                }
            }

            return null;
        }
    }

    internal class TaskbarChevronResolver
    {
        private IntPtr _cachedShellHwnd = IntPtr.Zero;
        private Rectangle _cachedTaskbarRect = Rectangle.Empty;
        private Rectangle _cachedTrayRect = Rectangle.Empty;
        private int? _cachedChevronLeft = null;

        internal IntPtr CachedShellHwnd
        {
            get { return _cachedShellHwnd; }
        }

        internal Rectangle CachedTaskbarRect
        {
            get { return _cachedTaskbarRect; }
        }

        internal Rectangle CachedTrayRect
        {
            get { return _cachedTrayRect; }
        }

        internal int? CachedChevronLeft
        {
            get { return _cachedChevronLeft; }
        }

        public void Reset()
        {
            _cachedShellHwnd = IntPtr.Zero;
            _cachedTaskbarRect = Rectangle.Empty;
            _cachedTrayRect = Rectangle.Empty;
            _cachedChevronLeft = null;
        }

        public Rectangle ResolveTrayRect(IntPtr shellHwnd, Rectangle nativeTaskbarRect, Rectangle nativeTrayRect, Rectangle observedChevronRect)
        {
            if (shellHwnd == IntPtr.Zero ||
                nativeTaskbarRect.IsEmpty || nativeTaskbarRect.Width <= 0 || nativeTaskbarRect.Height <= 0 ||
                nativeTrayRect.IsEmpty || nativeTrayRect.Width <= 0 || nativeTrayRect.Height <= 0)
            {
                Reset();
                return nativeTrayRect;
            }

            if (_cachedShellHwnd != shellHwnd ||
                _cachedTaskbarRect != nativeTaskbarRect ||
                _cachedTrayRect != nativeTrayRect)
            {
                _cachedShellHwnd = shellHwnd;
                _cachedTaskbarRect = nativeTaskbarRect;
                _cachedTrayRect = nativeTrayRect;
                _cachedChevronLeft = null;
            }

            if (IsChevronRectValid(observedChevronRect, nativeTaskbarRect, nativeTrayRect))
            {
                if (_cachedChevronLeft.HasValue)
                {
                    _cachedChevronLeft = Math.Min(_cachedChevronLeft.Value, observedChevronRect.Left);
                }
                else
                {
                    _cachedChevronLeft = observedChevronRect.Left;
                }
            }

            if (_cachedChevronLeft.HasValue &&
                _cachedChevronLeft.Value < nativeTrayRect.Left &&
                _cachedChevronLeft.Value > nativeTaskbarRect.Left)
            {
                int effectiveLeft = _cachedChevronLeft.Value;
                return new Rectangle(effectiveLeft, nativeTrayRect.Top, nativeTrayRect.Right - effectiveLeft, nativeTrayRect.Height);
            }

            return nativeTrayRect;
        }

        public static bool IsChevronRectValid(Rectangle chevronRect, Rectangle tbRect, Rectangle trRect)
        {
            if (chevronRect.IsEmpty || chevronRect.Width <= 0 || chevronRect.Height <= 0)
            {
                return false;
            }

            // 水平必须位于 taskbar 边界内
            if (chevronRect.Left < tbRect.Left || chevronRect.Right > tbRect.Right)
            {
                return false;
            }

            // 垂直必须位于 taskbar 边界内 (弹出面板 flyout top/bottom 越界在此被拒绝)
            if (chevronRect.Top < tbRect.Top || chevronRect.Bottom > tbRect.Bottom)
            {
                return false;
            }

            // 垂直相交校验
            if (chevronRect.Top >= tbRect.Bottom || chevronRect.Bottom <= tbRect.Top)
            {
                return false;
            }

            // 必须位于原生托盘左侧
            if (chevronRect.Left >= trRect.Left)
            {
                return false;
            }

            return true;
        }
    }
}
