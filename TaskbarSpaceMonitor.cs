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

        private int _state = STATE_INIT;
        private Thread _workerThread;
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private TaskbarSpaceSnapshot _latestSnapshot = TaskbarSpaceSnapshot.Invalid();
        private readonly object _snapshotLock = new object();
        private readonly int _pollIntervalMs;

        public event Action<TaskbarSpaceSnapshot> SpaceChanged;

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
            try
            {
                while (Volatile.Read(ref _state) == STATE_RUNNING)
                {
                    TaskbarSpaceSnapshot snap;
                    try
                    {
                        snap = ProbeTaskbarSpaceCore();
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
            return ProbeTaskbarSpaceCore();
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

        private TaskbarSpaceSnapshot ProbeTaskbarSpaceCore()
        {
            IntPtr hTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hTaskbar == IntPtr.Zero)
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            NativeMethods.RECT rcTaskbar;
            if (!NativeMethods.GetWindowRect(hTaskbar, out rcTaskbar))
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            Rectangle tbRect = Rectangle.FromLTRB(rcTaskbar.Left, rcTaskbar.Top, rcTaskbar.Right, rcTaskbar.Bottom);
            if (tbRect.Width <= 0 || tbRect.Height <= 0)
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            // 契约：缺tray均Invalid
            IntPtr hTray = NativeMethods.FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (hTray == IntPtr.Zero)
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            NativeMethods.RECT rcTray;
            if (!NativeMethods.GetWindowRect(hTray, out rcTray))
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            Rectangle trRect = Rectangle.FromLTRB(rcTray.Left, rcTray.Top, rcTray.Right, rcTray.Bottom);
            if (trRect.Width <= 0 || trRect.Height <= 0)
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            int trayLeft = trRect.Left;
            if (trayLeft <= tbRect.Left || trayLeft > tbRect.Right)
            {
                return TaskbarSpaceSnapshot.Invalid();
            }

            int maxAppRight = 0;
            int validButtonsCount = 0;

            try
            {
                AutomationElement taskbarElem = AutomationElement.FromHandle(hTaskbar);
                if (taskbarElem == null)
                {
                    return TaskbarSpaceSnapshot.Invalid();
                }

                // 在 Windows 11 下，折叠小箭头往往位于 TrayNotifyWnd 左侧 30~50px
                // 自动探测折叠箭头精确坐标，将托盘真实左起始边界向左拓展到折叠小箭头左侧
                // 契约：限制 200 节点深 4~6，明确 Chevron/语言名匹配，不吞异常冒充有效，保留真实 trRect 基线
                AutomationElement chevronElem = FindChevronButton(taskbarElem, maxDepth: 6, maxNodes: 200);
                if (chevronElem != null)
                {
                    System.Windows.Rect cRect = chevronElem.Current.BoundingRectangle;
                    if (!cRect.IsEmpty && cRect.Left > tbRect.Left && cRect.Left < trayLeft)
                    {
                        trayLeft = (int)Math.Floor(cRect.Left);
                        trRect = new Rectangle(trayLeft, trRect.Top, trRect.Right - trayLeft, trRect.Height);
                    }
                }

                // 契约：bounded tree遍历，找AutomationId=TaskbarFrameRepeater(Group)优先，最多7层/合理节点上限
                AutomationElement repeaterElem = FindTaskbarFrameRepeater(taskbarElem, maxDepth: 4);
                if (repeaterElem != null)
                {
                    Condition btnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                    AutomationElementCollection children = repeaterElem.FindAll(TreeScope.Children, btnCond);
                    if (children != null && children.Count > 0)
                    {
                        foreach (AutomationElement btn in children)
                        {
                            if (ProcessAppButton(btn, trayLeft, ref maxAppRight))
                            {
                                validButtonsCount++;
                            }
                        }
                    }
                }

                // Unknown shell structure is not evidence of free space. Fail closed instead
                // of accepting a partial traversal that could miss the rightmost app.

                // 契约：0个有效button / 无有效buttons 均 Invalid，绝不允许 MaxAppRight=0 return valid=true 把整个taskbar当空白
                if (validButtonsCount == 0 || maxAppRight <= 0)
                {
                    return TaskbarSpaceSnapshot.Invalid();
                }

                return EvaluateTaskbarSnapshot(tbRect, trRect, validButtonsCount, maxAppRight);
            }
            catch
            {
                // 契约：读取任一app失败/任何UIA异常均Invalid，不吞错冒充通过
                return TaskbarSpaceSnapshot.Invalid();
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
}
