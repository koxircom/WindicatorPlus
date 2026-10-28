using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using UIAutomationClientInterop;

namespace WindicatorPlus
{
    public struct NativeProbeRawResult
    {
        public Rectangle ObservedChevronRect;
        public int ValidButtonsCount;
        public int MaxAppRight;

        public NativeProbeRawResult(Rectangle observedChevronRect, int validButtonsCount, int maxAppRight)
        {
            ObservedChevronRect = observedChevronRect;
            ValidButtonsCount = validButtonsCount;
            MaxAppRight = maxAppRight;
        }
    }

    public class NativeTaskbarAutomation : IDisposable
    {
        private static readonly Guid ClsidCUIAutomation8 = new Guid("E22AD333-B25F-460C-83D0-0581107395C9");

        public const uint DEFAULT_TIMEOUT_MS = 150;
        public const long STEP_BUDGET_MS = 700;

        public const int UIA_BoundingRectanglePropertyId = 30001;
        public const int UIA_ControlTypePropertyId = 30003;
        public const int UIA_NamePropertyId = 30005;
        public const int UIA_AutomationIdPropertyId = 30011;
        public const int UIA_ClassNamePropertyId = 30012;
        public const int UIA_ButtonControlTypeId = 50000;

        private IUIAutomation2 _uia;
        private IUIAutomationCacheRequest _buttonCacheRequest;
        private bool _disposed;

        public uint ConnectionTimeout
        {
            get { return _uia != null ? _uia.ConnectionTimeout : 0; }
        }

        public uint TransactionTimeout
        {
            get { return _uia != null ? _uia.TransactionTimeout : 0; }
        }

        public NativeTaskbarAutomation()
        {
            try
            {
                Type uiaType = Type.GetTypeFromCLSID(ClsidCUIAutomation8, true);
                object rawObj = Activator.CreateInstance(uiaType);
                _uia = rawObj as IUIAutomation2;
                if (_uia == null)
                {
                    if (rawObj != null && Marshal.IsComObject(rawObj))
                    {
                        Marshal.ReleaseComObject(rawObj);
                    }
                    throw new InvalidOperationException("CLSID_CUIAutomation8 does not implement IUIAutomation2");
                }

                _uia.ConnectionTimeout = DEFAULT_TIMEOUT_MS;
                _uia.TransactionTimeout = DEFAULT_TIMEOUT_MS;

                if (_uia.ConnectionTimeout != DEFAULT_TIMEOUT_MS || _uia.TransactionTimeout != DEFAULT_TIMEOUT_MS)
                {
                    throw new InvalidOperationException(string.Format(
                        "Failed to set UIA timeouts to {0}ms; readback: conn={1}, trans={2}",
                        DEFAULT_TIMEOUT_MS, _uia.ConnectionTimeout, _uia.TransactionTimeout));
                }

                _buttonCacheRequest = _uia.CreateCacheRequest();
                _buttonCacheRequest.AddProperty(UIA_ControlTypePropertyId);
                _buttonCacheRequest.AddProperty(UIA_AutomationIdPropertyId);
                _buttonCacheRequest.AddProperty(UIA_ClassNamePropertyId);
                _buttonCacheRequest.AddProperty(UIA_NamePropertyId);
                _buttonCacheRequest.AddProperty(UIA_BoundingRectanglePropertyId);
                _buttonCacheRequest.AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static void CheckBudget(Stopwatch sw, string stage)
        {
            if (sw != null && sw.ElapsedMilliseconds > STEP_BUDGET_MS)
            {
                throw new TimeoutException(string.Format("Step budget exceeded: {0}ms at stage {1}", sw.ElapsedMilliseconds, stage));
            }
        }

        private sealed class RcwTracker : IDisposable
        {
            private readonly List<object> _tracked = new List<object>(128);

            public T Track<T>(T comObj) where T : class
            {
                if (comObj == null || !Marshal.IsComObject(comObj))
                {
                    return comObj;
                }

                for (int i = 0; i < _tracked.Count; i++)
                {
                    if (object.ReferenceEquals(_tracked[i], comObj))
                    {
                        return comObj;
                    }
                }

                _tracked.Add(comObj);
                return comObj;
            }

            public void Dispose()
            {
                for (int i = _tracked.Count - 1; i >= 0; i--)
                {
                    object obj = _tracked[i];
                    if (obj != null && Marshal.IsComObject(obj))
                    {
                        try
                        {
                            Marshal.ReleaseComObject(obj);
                        }
                        catch { }
                    }
                }
                _tracked.Clear();
            }
        }

        public NativeProbeRawResult ProbeTaskbarElements(
            IntPtr hTaskbar,
            int trayLeft,
            Stopwatch probeStopwatch,
            out string currentStage)
        {
            return ProbeTaskbarElements(hTaskbar, IntPtr.Zero, trayLeft, probeStopwatch, out currentStage);
        }

        public NativeProbeRawResult ProbeTaskbarElements(
            IntPtr hTaskbar,
            IntPtr hTray,
            int trayLeft,
            Stopwatch probeStopwatch,
            out string currentStage)
        {
            if (_disposed || _uia == null)
            {
                throw new ObjectDisposedException("NativeTaskbarAutomation");
            }

            currentStage = "FindChevron";
            Rectangle observedChevronRect = Rectangle.Empty;
            int validButtonsCount = 0;
            int maxAppRight = 0;

            CheckBudget(probeStopwatch, "PreProbe");

            using (RcwTracker tracker = new RcwTracker())
            {
                IUIAutomationElement taskbarElem = tracker.Track(_uia.ElementFromHandle(hTaskbar));
                if (taskbarElem == null)
                {
                    throw new InvalidOperationException("AutomationElementNull from Shell_TrayWnd handle");
                }

                CheckBudget(probeStopwatch, "ElementFromHandle");
                IUIAutomationTreeWalker walker = tracker.Track(_uia.ControlViewWalker);

                // Stage 1: Find Chevron (优先从 hTray shallow 查找，未命中再从 taskbarElem 查找，两者同预算)
                currentStage = "FindChevron";
                // 1.1 针对 Windows 10: TrayNotifyWnd 内部直接含有 Win32 原生 "Button" 窗口
                if (hTray != IntPtr.Zero)
                {
                    IntPtr hWin10Btn = NativeMethods.FindWindowEx(hTray, IntPtr.Zero, "Button", null);
                    if (hWin10Btn != IntPtr.Zero && NativeMethods.IsWindowVisible(hWin10Btn))
                    {
                        NativeMethods.RECT rcBtn;
                        if (NativeMethods.GetWindowRect(hWin10Btn, out rcBtn))
                        {
                            int bw = rcBtn.Right - rcBtn.Left;
                            int bh = rcBtn.Bottom - rcBtn.Top;
                            if (bw > 0 && bh > 0)
                            {
                                observedChevronRect = new Rectangle(rcBtn.Left, rcBtn.Top, bw, bh);
                            }
                        }
                    }
                }

                if (observedChevronRect.IsEmpty && hTray != IntPtr.Zero)
                {
                    IUIAutomationElement trayElem = tracker.Track(_uia.ElementFromHandle(hTray));
                    if (trayElem != null)
                    {
                        observedChevronRect = FindChevronButtonInternal(trayElem, walker, tracker, probeStopwatch, ref currentStage, maxDepth: 4);
                    }
                }

                if (observedChevronRect.IsEmpty)
                {
                    CheckBudget(probeStopwatch, "FindChevronShellFallback");
                    observedChevronRect = FindChevronButtonInternal(taskbarElem, walker, tracker, probeStopwatch, ref currentStage, maxDepth: 6);
                }
                CheckBudget(probeStopwatch, "FindChevronCompleted");

                // Stage 2: Find TaskbarFrame / TaskbarFrameRepeater (Win11) 或 MSTaskListWClass (Win10)
                currentStage = "FindRepeater";
                IUIAutomationElement targetContainer = null;
                IntPtr hWin10TaskList = IntPtr.Zero;

                // 2.1 探测 Windows 10 原生任务栏应用列表窗口 (MSTaskListWClass)
                IntPtr hReBar = NativeMethods.FindWindowEx(hTaskbar, IntPtr.Zero, "ReBarWindow32", null);
                if (hReBar != IntPtr.Zero)
                {
                    IntPtr hTaskSw = NativeMethods.FindWindowEx(hReBar, IntPtr.Zero, "MSTaskSwWClass", null);
                    if (hTaskSw != IntPtr.Zero)
                    {
                        hWin10TaskList = NativeMethods.FindWindowEx(hTaskSw, IntPtr.Zero, "MSTaskListWClass", null);
                    }
                }
                if (hWin10TaskList == IntPtr.Zero)
                {
                    hWin10TaskList = NativeMethods.FindWindowEx(hTaskbar, IntPtr.Zero, "MSTaskListWClass", null);
                }

                if (hWin10TaskList != IntPtr.Zero)
                {
                    targetContainer = tracker.Track(_uia.ElementFromHandle(hWin10TaskList));
                }

                // 2.2 若非 Win10，则走 Windows 11 的 XAML TaskbarFrameRepeater / TaskbarFrame
                if (targetContainer == null)
                {
                    targetContainer = FindTaskbarFrameRepeaterInternal(taskbarElem, walker, tracker, probeStopwatch);
                }
                CheckBudget(probeStopwatch, "FindRepeaterCompleted");

                // Stage 3 & 4: Enumerate buttons with FindAllBuildCache
                currentStage = "EnumerateButtons";
                if (targetContainer != null)
                {
                    IUIAutomationCondition btnCond = tracker.Track(_uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ButtonControlTypeId));
                    IUIAutomationElementArray buttons = tracker.Track(targetContainer.FindAllBuildCache(
                        TreeScope.TreeScope_Children,
                        btnCond,
                        _buttonCacheRequest));

                    CheckBudget(probeStopwatch, "FindAllBuildCacheCompleted");

                    if (buttons != null)
                    {
                        int count = buttons.Length;
                        for (int i = 0; i < count; i++)
                        {
                            CheckBudget(probeStopwatch, "ProcessAppButton[" + i + "]");
                            currentStage = "ProcessAppButton";

                            IUIAutomationElement btn = tracker.Track(buttons.GetElement(i));
                            if (ProcessAppButtonCached(btn, trayLeft, ref maxAppRight))
                            {
                                validButtonsCount++;
                            }
                            currentStage = "EnumerateButtons";
                        }
                    }
                }

                // 4.1 针对 Windows 10 的安全回退保障：若未单独枚举到子按钮但 Win10 任务栏存在，使用其物理右边界
                if (validButtonsCount == 0 && hWin10TaskList != IntPtr.Zero)
                {
                    NativeMethods.RECT rcTaskList;
                    if (NativeMethods.GetWindowRect(hWin10TaskList, out rcTaskList))
                    {
                        if (rcTaskList.Right > 0 && rcTaskList.Right <= trayLeft)
                        {
                            maxAppRight = rcTaskList.Right;
                            validButtonsCount = 1;
                        }
                    }
                }

                return new NativeProbeRawResult(observedChevronRect, validButtonsCount, maxAppRight);
            }
        }

        private Rectangle FindChevronButtonInternal(
            IUIAutomationElement root,
            IUIAutomationTreeWalker walker,
            RcwTracker tracker,
            Stopwatch sw,
            ref string currentStage,
            int maxDepth = 6)
        {
            if (root == null || walker == null) return Rectangle.Empty;

            CheckBudget(sw, "FindChevronBatch");
            var cls = tracker.Track(_uia.CreatePropertyCondition(UIA_ClassNamePropertyId, "SystemTray.ChevronIcon"));
            var zh = tracker.Track(_uia.CreatePropertyCondition(UIA_NamePropertyId, "显示隐藏的图标"));
            var en = tracker.Track(_uia.CreatePropertyCondition(UIA_NamePropertyId, "Show hidden icons"));
            var names = tracker.Track(_uia.CreateOrCondition(zh, en));
            var identity = tracker.Track(_uia.CreateOrCondition(cls, names));
            var button = tracker.Track(_uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ButtonControlTypeId));
            var condition = tracker.Track(_uia.CreateAndCondition(button, identity));
            var elem = tracker.Track(root.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condition, _buttonCacheRequest));
            CheckBudget(sw, "FindChevronBatchCompleted");
            if (elem == null) return Rectangle.Empty;
            currentStage = "readChevron";
            tagRECT rect = elem.CachedBoundingRectangle;
            if (rect.right <= rect.left || rect.bottom <= rect.top)
                throw new InvalidOperationException("App button geometry unavailable");
            return Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
        }

        private IUIAutomationElement FindTaskbarFrameRepeaterInternal(
            IUIAutomationElement root,
            IUIAutomationTreeWalker walker,
            RcwTracker tracker,
            Stopwatch sw)
        {
            if (root == null || walker == null) return null;

            CheckBudget(sw, "FindRepeaterQuery");
            var repeaterCondition = tracker.Track(_uia.CreatePropertyCondition(UIA_AutomationIdPropertyId, "TaskbarFrameRepeater"));
            var repeater = tracker.Track(root.FindFirst(TreeScope.TreeScope_Descendants, repeaterCondition));
            CheckBudget(sw, "FindRepeaterQueryCompleted");
            if (repeater != null) return repeater;
            var frameCondition = tracker.Track(_uia.CreatePropertyCondition(UIA_AutomationIdPropertyId, "TaskbarFrame"));
            var frame = tracker.Track(root.FindFirst(TreeScope.TreeScope_Descendants, frameCondition));
            CheckBudget(sw, "FindFrameQueryCompleted");
            return frame;
        }

        public static bool ProcessAppButtonCached(IUIAutomationElement elem, int trayLeft, ref int maxAppRight)
        {
            if (elem == null) return false;

            if (elem.CachedControlType != UIA_ButtonControlTypeId)
            {
                return false;
            }

            string autoId = elem.CachedAutomationId ?? "";
            string className = elem.CachedClassName ?? "";
            string name = elem.CachedName ?? "";

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

            tagRECT rect = elem.CachedBoundingRectangle;
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("App button geometry unavailable");
            }

            int r = rect.right;
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

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_buttonCacheRequest != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(_buttonCacheRequest);
                    }
                    catch { }
                    _buttonCacheRequest = null;
                }
                if (_uia != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(_uia);
                    }
                    catch { }
                    _uia = null;
                }
            }
        }
    }
}
