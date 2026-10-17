using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindicatorPlus
{
    public class DesktopLoopSwitcher : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_CONTROL = 0x11;

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct INPUT64
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT64[] pInputs, int cbSize);

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private DesktopWatcher _watcher;
        private AppConfig _config;

        private volatile bool _isInjecting = false;
        private long _lastWrapTime = 0;

        public DesktopLoopSwitcher(DesktopWatcher watcher, AppConfig config)
        {
            _watcher = watcher;
            _config = config;
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_hookId == IntPtr.Zero)
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                }
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isInjecting && _config.DesktopLoopEnabled)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    KBDLLHOOKSTRUCT kbd = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    uint vk = kbd.vkCode;

                    if (vk == VK_LEFT || vk == VK_RIGHT)
                    {
                        bool isWin = (NativeMethods.GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
                                     (NativeMethods.GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                        bool isCtrl = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                        if (isWin && isCtrl)
                        {
                            long now = DateTime.UtcNow.Ticks / 10000; // ms
                            if (now - _lastWrapTime > 400) // 400ms debounce
                            {
                                int cur, total;
                                _watcher.QueryDesktops(out cur, out total);

                                if (vk == VK_LEFT && cur == 1 && total > 1)
                                {
                                    // Wrap Left: from 1 to last desktop (total)
                                    _lastWrapTime = now;
                                    int jumpSteps = total - 1;
                                    ThreadPool.QueueUserWorkItem(s => ExecuteWrap(jumpSteps, VK_RIGHT));
                                    return (IntPtr)1; // Block native boundary hit
                                }
                                else if (vk == VK_RIGHT && cur == total && total > 1)
                                {
                                    // Wrap Right: from last desktop to desktop 1
                                    _lastWrapTime = now;
                                    int jumpSteps = total - 1;
                                    ThreadPool.QueueUserWorkItem(s => ExecuteWrap(jumpSteps, VK_LEFT));
                                    return (IntPtr)1; // Block native boundary hit
                                }
                            }
                        }
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void ExecuteWrap(int steps, uint arrowVk)
        {
            _isInjecting = true;
            try
            {
                Thread.Sleep(30);
                for (int i = 0; i < steps; i++)
                {
                    SendWinCtrlArrow(arrowVk);
                    Thread.Sleep(40); // 40ms interval for smooth Windows 11 Explorer desktop transition
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Wrap execution error: " + ex.Message);
            }
            finally
            {
                _isInjecting = false;
            }
        }

        private void SendWinCtrlArrow(uint arrowVk)
        {
            // Win64 40-byte compliant SendInput
            INPUT64[] inputs = new INPUT64[6];

            // 1. Win Down
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = (ushort)VK_LWIN;

            // 2. Ctrl Down
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki.wVk = (ushort)VK_CONTROL;

            // 3. Arrow Down
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].ki.wVk = (ushort)arrowVk;
            inputs[2].ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

            // 4. Arrow Up
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].ki.wVk = (ushort)arrowVk;
            inputs[3].ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

            // 5. Ctrl Up
            inputs[4].type = INPUT_KEYBOARD;
            inputs[4].ki.wVk = (ushort)VK_CONTROL;
            inputs[4].ki.dwFlags = KEYEVENTF_KEYUP;

            // 6. Win Up
            inputs[5].type = INPUT_KEYBOARD;
            inputs[5].ki.wVk = (ushort)VK_LWIN;
            inputs[5].ki.dwFlags = KEYEVENTF_KEYUP;

            uint sent = SendInput((uint)inputs.Length, inputs, 40);
            Debug.WriteLine("SendWinCtrlArrow sent: " + sent);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
