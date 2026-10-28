using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WindicatorPlus
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    public static class RuntimeTrace
    {
        public const int WM_WINDOWPOSCHANGED = 0x0047;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        private static readonly object _syncLock = new object();
        private static volatile bool _isEnabled = false;
        private static string _filePath = null;
        private static StreamWriter _writer = null;
        private static Stopwatch _stopwatch = null;
        private static int _eventCount = 0;
        private static int _maxEvents = 10000;
        private static long _maxDurationMs = 10 * 60 * 1000; // 10 minutes
        private static int _unflushedCount = 0;
        private static long _lastFlushTicks = 0;
        private static bool _processExitHooked = false;

        public static bool IsEnabled
        {
            get { return _isEnabled; }
        }

        static RuntimeTrace()
        {
            TryAutoInitialize();
        }

        public static void EnsureInitialized()
        {
            // Triggers static constructor
        }

        private static void TryAutoInitialize()
        {
            try
            {
                string envPath = Environment.GetEnvironmentVariable("WINDICATOR_TRACE_FILE");
                if (string.IsNullOrEmpty(envPath) || string.IsNullOrEmpty(envPath.Trim()))
                {
                    _isEnabled = false;
                    return;
                }

                InitInternal(envPath.Trim(), 10000, 10 * 60 * 1000);
            }
            catch
            {
                _isEnabled = false;
            }
        }

        internal static void InitForTesting(string filePath, int maxEvents, long maxDurationMs)
        {
            lock (_syncLock)
            {
                ShutdownInternal("ReinitTesting");
                InitInternal(filePath, maxEvents, maxDurationMs);
            }
        }

        internal static void ResetForTesting()
        {
            lock (_syncLock)
            {
                ShutdownInternal("ResetTesting");
                TryAutoInitialize();
            }
        }

        private static void InitInternal(string filePath, int maxEvents, long maxDurationMs)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, new UTF8Encoding(false), 4096);
                _filePath = filePath;
                _maxEvents = maxEvents > 0 ? maxEvents : 10000;
                _maxDurationMs = maxDurationMs > 0 ? maxDurationMs : 10 * 60 * 1000;
                _eventCount = 0;
                _unflushedCount = 0;
                _stopwatch = Stopwatch.StartNew();
                _lastFlushTicks = Stopwatch.GetTimestamp();
                _isEnabled = true;

                if (!_processExitHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown("ProcessExit");
                    _processExitHooked = true;
                }
            }
            catch
            {
                _isEnabled = false;
                if (_writer != null)
                {
                    try { _writer.Dispose(); } catch { }
                    _writer = null;
                }
            }
        }

        public static void Shutdown(string reason = "Explicit")
        {
            lock (_syncLock)
            {
                ShutdownInternal(reason);
            }
        }

        private static void ShutdownInternal(string reason)
        {
            if (!_isEnabled && _writer == null) return;
            try
            {
                if (_writer != null && _isEnabled)
                {
                    LogEventDirect("exit", string.Format("\"reason\":\"{0}\",\"totalEvents\":{1},\"elapsedMs\":{2}",
                        EscapeJson(reason), _eventCount, _stopwatch != null ? _stopwatch.ElapsedMilliseconds : 0));
                    _writer.Flush();
                }
            }
            catch { }
            finally
            {
                _isEnabled = false;
                if (_writer != null)
                {
                    try { _writer.Dispose(); } catch { }
                    _writer = null;
                }
            }
        }

        public static void Flush()
        {
            if (!_isEnabled) return;
            lock (_syncLock)
            {
                if (!_isEnabled || _writer == null) return;
                try
                {
                    _writer.Flush();
                    _unflushedCount = 0;
                    _lastFlushTicks = Stopwatch.GetTimestamp();
                }
                catch
                {
                    _isEnabled = false;
                    try { if (_writer != null) { _writer.Dispose(); _writer = null; } } catch { }
                }
            }
        }

        public static void CheckBudget()
        {
            if (!_isEnabled) return;
            if (_unflushedCount > 0 && Stopwatch.GetTimestamp() - _lastFlushTicks >= Stopwatch.Frequency / 2) Flush();
            if (_eventCount >= _maxEvents || (_stopwatch != null && _stopwatch.ElapsedMilliseconds > _maxDurationMs))
            {
                lock (_syncLock)
                {
                    if (_isEnabled && (_eventCount >= _maxEvents || (_stopwatch != null && _stopwatch.ElapsedMilliseconds > _maxDurationMs)))
                    {
                        ShutdownInternal("BudgetExceeded");
                    }
                }
            }
        }

        public static string FormatProbeException(Exception ex)
        {
            if (ex == null) return "";
            if (ex is InvalidOperationException &&
                (string.Equals(ex.Message, "App button geometry unavailable", StringComparison.Ordinal) ||
                 string.Equals(ex.Message, "App and tray regions intersect", StringComparison.Ordinal)))
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
            COMException com = ex as COMException;
            if (com != null) return "COMException HRESULT=0x" + com.ErrorCode.ToString("X8");
            return ex.GetType().Name;
        }

        public static void LogAppStart(int pid, string version)
        {
            if (!_isEnabled) return;
            WriteEvent("start", string.Format("\"pid\":{0},\"version\":\"{1}\"", pid, EscapeJson(version)));
        }

        public static void LogProbeChange(
            bool isValid,
            Rectangle taskbarRect,
            Rectangle nativeTrayRect,
            Rectangle observedChevronRect,
            Rectangle effectiveTrayRect,
            int maxAppRight,
            int validButtonsCount)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"valid\":{0},\"tb\":{1},\"nativeTray\":{2},\"chevron\":{3},\"effectiveTray\":{4},\"maxApp\":{5},\"btnCount\":{6}",
                isValid ? "true" : "false",
                FormatRect(taskbarRect),
                FormatRect(nativeTrayRect),
                FormatRect(observedChevronRect),
                FormatRect(effectiveTrayRect),
                maxAppRight,
                validButtonsCount);
            WriteEvent("probe_change", data);
        }

        public static void LogProbeFailure(
            string stage,
            string exceptionType,
            Rectangle nativeTrayRect,
            Rectangle observedChevronRect,
            Rectangle effectiveTrayRect,
            int maxAppRight)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"stage\":\"{0}\",\"exType\":\"{1}\",\"nativeTray\":{2},\"chevron\":{3},\"effectiveTray\":{4},\"maxApp\":{5}",
                EscapeJson(stage),
                EscapeJson(exceptionType ?? ""),
                FormatRect(nativeTrayRect),
                FormatRect(observedChevronRect),
                FormatRect(effectiveTrayRect),
                maxAppRight);
            WriteEvent("probe_fail", data);
        }

        public static void LogProbeRetryAttempt(
            int attempt,
            int maxAttempts,
            string stage,
            string exceptionType,
            int delayMs)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"attempt\":{0},\"maxAttempts\":{1},\"stage\":\"{2}\",\"exType\":\"{3}\",\"delayMs\":{4}",
                attempt,
                maxAttempts,
                EscapeJson(stage ?? ""),
                EscapeJson(exceptionType ?? ""),
                delayMs);
            WriteEvent("probe_retry_attempt", data);
        }

        public static void LogProbeRetryResult(
            bool recovered,
            int attempts,
            int maxAttempts,
            string stage)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"recovered\":{0},\"attempts\":{1},\"maxAttempts\":{2},\"stage\":\"{3}\"",
                recovered ? "true" : "false",
                attempts,
                maxAttempts,
                EscapeJson(stage ?? ""));
            WriteEvent("probe_retry_result", data);
        }

        public static void LogWatermarkUpdate(
            string action,
            bool success,
            string decision,
            int desktop,
            int totalDesktops,
            string desktopName,
            string titleText,
            Rectangle lastActualRect,
            Rectangle desiredRect,
            long snapshotTimestampTicks,
            long snapshotAgeMs,
            bool snapshotValid,
            bool isDocked,
            bool isFloating)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"action\":\"{0}\",\"success\":{1},\"decision\":\"{2}\",\"desktop\":{3},\"total\":{4},\"name\":\"{5}\",\"title\":\"{6}\",\"actual\":{7},\"desired\":{8},\"snapTicks\":{9},\"snapAgeMs\":{10},\"snapValid\":{11},\"docked\":{12},\"floating\":{13}",
                EscapeJson(action),
                success ? "true" : "false",
                EscapeJson(decision),
                desktop,
                totalDesktops,
                EscapeJson(desktopName ?? ""),
                EscapeJson(titleText ?? ""),
                FormatRect(lastActualRect),
                FormatRect(desiredRect),
                snapshotTimestampTicks,
                snapshotAgeMs,
                snapshotValid ? "true" : "false",
                isDocked ? "true" : "false",
                isFloating ? "true" : "false");
            WriteEvent("water_update", data);
        }

        public static void LogWindowPosChanged(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"hwnd\":\"0x{0:X}\",\"insertAfter\":\"0x{1:X}\",\"x\":{2},\"y\":{3},\"cx\":{4},\"cy\":{5},\"flags\":\"0x{6:X}\"",
                hwnd.ToInt64(),
                hwndInsertAfter.ToInt64(),
                x,
                y,
                cx,
                cy,
                flags);
            WriteEvent("wndproc_pos", data);
        }

        public static void LogCoordinatorAdjust(
            IntPtr hShell,
            IntPtr hWater,
            IntPtr hTint,
            IntPtr hMenu,
            string shellTintRel,
            string waterShellRel,
            string menuWaterRel,
            string menuShellRel,
            bool needTint,
            bool needWater,
            bool needMenu)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"shell\":\"0x{0:X}\",\"water\":\"0x{1:X}\",\"tint\":\"0x{2:X}\",\"menu\":\"0x{3:X}\",\"shellTint\":\"{4}\",\"waterShell\":\"{5}\",\"menuWater\":\"{6}\",\"menuShell\":\"{7}\",\"needTint\":{8},\"needWater\":{9},\"needMenu\":{10}",
                hShell.ToInt64(),
                hWater.ToInt64(),
                hTint.ToInt64(),
                hMenu.ToInt64(),
                EscapeJson(shellTintRel),
                EscapeJson(waterShellRel),
                EscapeJson(menuWaterRel),
                EscapeJson(menuShellRel),
                needTint ? "true" : "false",
                needWater ? "true" : "false",
                needMenu ? "true" : "false");
            WriteEvent("coordinator_adjust", data);
        }

        public static void LogMouseEvent(string button, string action, int x, int y, IntPtr foregroundHwnd)
        {
            if (!_isEnabled) return;
            string data = string.Format(
                "\"btn\":\"{0}\",\"act\":\"{1}\",\"x\":{2},\"y\":{3},\"fgHwnd\":\"0x{4:X}\"",
                EscapeJson(button),
                EscapeJson(action),
                x,
                y,
                foregroundHwnd.ToInt64());
            WriteEvent("mouse", data);
        }

        private static void WriteEvent(string category, string dataJson)
        {
            if (!_isEnabled) return;

            lock (_syncLock)
            {
                if (!_isEnabled || _writer == null) return;

                if (_eventCount >= _maxEvents || (_stopwatch != null && _stopwatch.ElapsedMilliseconds > _maxDurationMs))
                {
                    ShutdownInternal("BudgetExceeded");
                    return;
                }

                try
                {
                    LogEventDirect(category, dataJson);
                    _unflushedCount++;

                    long nowTicks = Stopwatch.GetTimestamp();
                    long elapsedSinceFlushMs = Stopwatch.Frequency > 0 ? ((nowTicks - _lastFlushTicks) * 1000 / Stopwatch.Frequency) : 0;
                    if (_unflushedCount >= 32 || elapsedSinceFlushMs >= 500)
                    {
                        _writer.Flush();
                        _unflushedCount = 0;
                        _lastFlushTicks = nowTicks;
                    }
                }
                catch
                {
                    _isEnabled = false;
                    try
                    {
                        if (_writer != null)
                        {
                            _writer.Dispose();
                            _writer = null;
                        }
                    }
                    catch { }
                }
            }
        }

        private static void LogEventDirect(string category, string dataJson)
        {
            _eventCount++;
            long elapsed = _stopwatch != null ? _stopwatch.ElapsedMilliseconds : 0;

            _writer.Write("{\"seq\":");
            _writer.Write(_eventCount);
            _writer.Write(",\"t\":");
            _writer.Write(elapsed);
            _writer.Write(",\"cat\":\"");
            _writer.Write(category);
            _writer.Write("\"");
            if (!string.IsNullOrEmpty(dataJson))
            {
                _writer.Write(",");
                _writer.Write(dataJson);
            }
            _writer.WriteLine("}");
        }

        private static string FormatRect(Rectangle r)
        {
            return string.Format("[{0},{1},{2},{3}]", r.X, r.Y, r.Width, r.Height);
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
