using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace WindicatorPlus
{
    public class DesktopChangedEventArgs : EventArgs
    {
        public int CurrentDesktop { get; private set; }
        public int TotalDesktops { get; private set; }

        public DesktopChangedEventArgs(int current, int total)
        {
            CurrentDesktop = current;
            TotalDesktops = total;
        }
    }

    public class DesktopWatcher : IDisposable
    {
        private const string VIRTUAL_DESKTOPS_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

        public event EventHandler<DesktopChangedEventArgs> DesktopChanged;

        private Thread _watchThread;
        private IntPtr _hStopEvent = IntPtr.Zero;
        private IntPtr _hNotifyEvent = IntPtr.Zero;
        private IntPtr _hRegKey = IntPtr.Zero;
        private volatile bool _isRunning = false;

        public int CurrentDesktop { get; private set; }
        public int TotalDesktops { get; private set; }

        public DesktopWatcher()
        {
            CurrentDesktop = 1;
            TotalDesktops = 1;
        }

        public void Start()
        {
            if (_isRunning) return;

            // Read initial state
            int cur, total;
            QueryDesktops(out cur, out total);
            CurrentDesktop = cur;
            TotalDesktops = total;

            _hStopEvent = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null);
            _hNotifyEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);

            int openRes = NativeMethods.RegOpenKeyEx(
                NativeMethods.HKEY_CURRENT_USER,
                VIRTUAL_DESKTOPS_KEY,
                0,
                NativeMethods.KEY_READ | NativeMethods.KEY_NOTIFY,
                out _hRegKey);

            if (openRes != 0)
            {
                Debug.WriteLine("Failed to open VirtualDesktops registry key: " + openRes);
            }

            _isRunning = true;
            _watchThread = new Thread(WatchThreadProc)
            {
                IsBackground = true,
                Name = "VirtualDesktopWatcherThread"
            };
            _watchThread.Start();
        }

        private void WatchThreadProc()
        {
            IntPtr[] waitHandles = new IntPtr[] { _hStopEvent, _hNotifyEvent };

            while (_isRunning)
            {
                if (_hRegKey != IntPtr.Zero && _hNotifyEvent != IntPtr.Zero)
                {
                    int notifyRes = NativeMethods.RegNotifyChangeKeyValue(
                        _hRegKey,
                        true,
                        NativeMethods.REG_NOTIFY_CHANGE_NAME | NativeMethods.REG_NOTIFY_CHANGE_LAST_SET,
                        _hNotifyEvent,
                        true);

                    if (notifyRes != 0)
                    {
                        Debug.WriteLine("RegNotifyChangeKeyValue returned: " + notifyRes);
                        Thread.Sleep(500);
                        continue;
                    }
                }

                uint waitResult = NativeMethods.WaitForMultipleObjects(2, waitHandles, false, NativeMethods.INFINITE);

                if (waitResult == NativeMethods.WAIT_OBJECT_0)
                {
                    // Stop event signaled
                    break;
                }
                else if (waitResult == NativeMethods.WAIT_OBJECT_0 + 1)
                {
                    // Registry changed!
                    // Slight debounce to ensure registry writes have finished
                    Thread.Sleep(30);

                    int newCur, newTotal;
                    QueryDesktops(out newCur, out newTotal);
                    if (newCur != CurrentDesktop || newTotal != TotalDesktops)
                    {
                        CurrentDesktop = newCur;
                        TotalDesktops = newTotal;

                        var handler = DesktopChanged;
                        if (handler != null)
                        {
                            handler(this, new DesktopChangedEventArgs(newCur, newTotal));
                        }
                    }
                }
                else
                {
                    // Error or unexpected
                    Thread.Sleep(200);
                }
            }
        }

        public void QueryDesktops(out int currentIdx, out int totalCount)
        {
            currentIdx = 1;
            totalCount = 1;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(VIRTUAL_DESKTOPS_KEY, false))
                {
                    if (key == null) return;

                    byte[] ids = key.GetValue("VirtualDesktopIDs") as byte[] ?? key.GetValue("VirtualDesktopIds") as byte[];
                    byte[] cur = key.GetValue("CurrentVirtualDesktop") as byte[];

                    if (ids != null && ids.Length >= 16)
                    {
                        totalCount = ids.Length / 16;
                    }

                    if (ids != null && cur != null && cur.Length == 16)
                    {
                        Guid curGuid = new Guid(cur);
                        for (int i = 0; i < totalCount; i++)
                        {
                            byte[] slice = new byte[16];
                            Array.Copy(ids, i * 16, slice, 0, 16);
                            if (new Guid(slice) == curGuid)
                            {
                                currentIdx = i + 1;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error reading desktops from registry: " + ex.Message);
            }
        }

        public void Stop()
        {
            _isRunning = false;
            if (_hStopEvent != IntPtr.Zero)
            {
                NativeMethods.SetEvent(_hStopEvent);
            }

            if (_watchThread != null && _watchThread.IsAlive)
            {
                _watchThread.Join(500);
            }

            if (_hRegKey != IntPtr.Zero)
            {
                NativeMethods.RegCloseKey(_hRegKey);
                _hRegKey = IntPtr.Zero;
            }

            if (_hStopEvent != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_hStopEvent);
                _hStopEvent = IntPtr.Zero;
            }

            if (_hNotifyEvent != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_hNotifyEvent);
                _hNotifyEvent = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
