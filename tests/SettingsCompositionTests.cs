using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace WindicatorPlus.Tests
{
    public static class SettingsCompositionTests
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

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private const int UOI_NAME = 2;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_COMPOSITED = 0x02000000L;
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
                if (disposing) _file.Dispose();
                base.Dispose(disposing);
            }
        }

        private static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, nIndex).ToInt64();
            }
            return (long)GetWindowLong32(hWnd, nIndex);
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

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            string targetAssembly = null;
            bool isIsolatedChild = false;
            string logFile = null;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--isolated-child")
                    {
                        isIsolatedChild = true;
                    }
                    else if (args[i] == "--target" && i + 1 < args.Length)
                    {
                        targetAssembly = args[++i];
                    }
                    else if (args[i] == "--log-file" && i + 1 < args.Length)
                    {
                        logFile = args[++i];
                    }
                    else if (!args[i].StartsWith("--") && targetAssembly == null)
                    {
                        targetAssembly = args[i];
                    }
                }
            }

            MultiWriter multiWriter = null;
            if (!string.IsNullOrEmpty(logFile))
            {
                try
                {
                    multiWriter = new MultiWriter(Console.Out, logFile);
                    Console.SetOut(multiWriter);
                    Console.SetError(multiWriter);
                }
                catch { }
            }

            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Settings Composition & Isolation Regression Tests");
            Console.WriteLine("==================================================");

            if (string.IsNullOrEmpty(targetAssembly))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cand1 = Path.Combine(baseDir, @"out\v1.3.14\WindicatorPlus.exe");
                string cand2 = Path.Combine(baseDir, @"WindicatorPlus.exe");
                targetAssembly = File.Exists(cand1) ? cand1 : cand2;
            }

            targetAssembly = Path.GetFullPath(targetAssembly);
            Console.WriteLine("[TEST CONFIG] Target Assembly: " + targetAssembly);

            // Win32 隔离桌面守卫
            string desktopName = GetCurrentDesktopName();
            bool isIsolated = (!string.IsNullOrEmpty(desktopName) && desktopName.StartsWith("WindicatorTest", StringComparison.OrdinalIgnoreCase));

            if (isIsolatedChild)
            {
                if (!isIsolated)
                {
                    Console.WriteLine("[SAFETY GUARD ERROR] Isolated child must run inside WindicatorTest desktop, but got: '" + desktopName + "'");
                    return 2;
                }
            }
            else if (!isIsolated)
            {
                Console.WriteLine("[SAFETY GUARD] Current desktop is '" + desktopName + "'. Spawning child in isolated desktop 'WindicatorTest-*'...");
                string testDeskName = "WindicatorTest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                IntPtr hNewDesk = CreateDesktop(testDeskName, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
                if (hNewDesk == IntPtr.Zero)
                {
                    Console.WriteLine("[SAFETY GUARD ERROR] CreateDesktop failed: " + Marshal.GetLastWin32Error());
                    return 2;
                }

                string childLogPath = Path.Combine(Path.GetTempPath(), "SettingsCompTest-" + Guid.NewGuid().ToString("N") + ".log");

                try
                {
                    STARTUPINFO si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf(si);
                    si.lpDesktop = testDeskName;

                    PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    string cmdLine = string.Format("\"{0}\" --isolated-child --target \"{1}\" --log-file \"{2}\"", exePath, targetAssembly, childLogPath);

                    bool spawned = CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW, IntPtr.Zero, null, ref si, out pi);
                    if (!spawned)
                    {
                        Console.WriteLine("[SAFETY GUARD ERROR] CreateProcess in isolated desktop failed: " + Marshal.GetLastWin32Error());
                        return 2;
                    }

                    WaitForSingleObject(pi.hProcess, 45000);
                    uint childExitCode = 0;
                    GetExitCodeProcess(pi.hProcess, out childExitCode);
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);

                    if (File.Exists(childLogPath))
                    {
                        string childLogs = File.ReadAllText(childLogPath, Encoding.UTF8);
                        Console.WriteLine("--- [ISOLATED CHILD LOGS BEGIN] ---");
                        Console.Write(childLogs);
                        Console.WriteLine("--- [ISOLATED CHILD LOGS END] ---");
                        try { File.Delete(childLogPath); } catch { }
                    }

                    Console.WriteLine("[SAFETY GUARD] Isolated child exited with code: " + childExitCode);
                    return (int)childExitCode;
                }
                finally
                {
                    CloseDesktop(hNewDesk);
                }
            }

            Console.WriteLine("[SAFETY GUARD VERIFIED] Running inside isolated desktop context: " + desktopName);

            try
            {
                RunAllTests(targetAssembly);
                Console.WriteLine("==================================================");
                Console.WriteLine("ALL SETTINGS COMPOSITION & ISOLATION REGRESSION TESTS PASSED");
                Console.WriteLine("==================================================");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[TEST FAILURE] " + ex.Message);
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return 1;
            }
            finally
            {
                if (multiWriter != null)
                {
                    multiWriter.Dispose();
                }
            }
        }

        private static Form CreateSettingsFormInstance(Type tSettings, object configObj)
        {
            ConstructorInfo[] ctors = tSettings.GetConstructors();
            Assert(ctors.Length > 0, "SettingsForm must have public constructors");

            // 优先选择参数匹配度最高的构造函数
            ConstructorInfo bestCtor = null;
            int maxParams = -1;
            foreach (var ctor in ctors)
            {
                var ps = ctor.GetParameters();
                if (ps.Length > maxParams)
                {
                    maxParams = ps.Length;
                    bestCtor = ctor;
                }
            }

            Assert(bestCtor != null, "Could not find a valid SettingsForm constructor");
            var paramList = bestCtor.GetParameters();
            object[] args = new object[paramList.Length];
            for (int i = 0; i < paramList.Length; i++)
            {
                if (i == 0) args[i] = configObj;
                else args[i] = null;
            }

            return (Form)bestCtor.Invoke(args);
        }

        private static void RunAllTests(string targetAssembly)
        {
            Assert(File.Exists(targetAssembly), "Target assembly does not exist: " + targetAssembly);

            Assembly asm = Assembly.LoadFrom(targetAssembly);
            Type tSettings = asm.GetType("WindicatorPlus.SettingsForm");
            Type tConfig = asm.GetType("WindicatorPlus.AppConfig");
            Type tConfigManager = asm.GetType("WindicatorPlus.ConfigManager");

            Assert(tSettings != null, "SettingsForm type must exist in " + targetAssembly);
            Assert(tConfig != null, "AppConfig type must exist in " + targetAssembly);

            // 1. 验证配置文件零副作用守卫前置信息
            string configPath = null;
            if (tConfigManager != null)
            {
                FieldInfo fiConfigPath = tConfigManager.GetField("ConfigFilePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (fiConfigPath != null)
                {
                    configPath = fiConfigPath.GetValue(null) as string;
                }
                else
                {
                    PropertyInfo piConfigPath = tConfigManager.GetProperty("ConfigFilePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (piConfigPath != null)
                    {
                        configPath = piConfigPath.GetValue(null, null) as string;
                    }
                }
            }

            if (string.IsNullOrEmpty(configPath))
            {
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WindicatorPlus",
                    "config.ini");
            }

            bool configExistedBefore = !string.IsNullOrEmpty(configPath) && File.Exists(configPath);
            DateTime configMtimeBefore = configExistedBefore ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            string configHashBefore = configExistedBefore ? ComputeFileSha256(configPath) : "";

            // 2. 创建第一个 Form 测试：样式无 COMPOSITED、6页可见性独占、卡片无重叠
            object configObj = Activator.CreateInstance(tConfig);
            Form form1 = CreateSettingsFormInstance(tSettings, configObj);
            try
            {
                PropertyInfo piSuppress = tSettings.GetProperty("SuppressPersistenceForTesting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (piSuppress != null)
                {
                    piSuppress.SetValue(form1, true, null);
                }

                form1.StartPosition = FormStartPosition.Manual;
                form1.Location = new Point(-20000, -20000);
                form1.ShowInTaskbar = false;
                form1.Show();
                Application.DoEvents();

                // 2.1 检查实际 GWL_EXSTYLE 无 COMPOSITED (0x02000000)
                long exStyle = GetWindowLongPtr(form1.Handle, GWL_EXSTYLE);
                Console.WriteLine(string.Format("[TEST] SettingsForm HWND=0x{0:X}, GWL_EXSTYLE=0x{1:X8}", form1.Handle.ToInt64(), exStyle));
                bool hasComposited = (exStyle & WS_EX_COMPOSITED) != 0;
                Assert(!hasComposited, string.Format("SettingsForm GWL_EXSTYLE (0x{0:X8}) must NOT contain WS_EX_COMPOSITED (0x02000000)!", exStyle));
                Console.WriteLine("PASS: GWL_EXSTYLE has no WS_EX_COMPOSITED");

                // 2.2 6 页切换仅一页可见与卡片无重叠
                string[] tabs = new string[] { "General", "Watermark", "Position", "Names", "System", "About" };
                MethodInfo miSwitchTab = tSettings.GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert(miSwitchTab != null, "SwitchTab method must exist on SettingsForm");

                FieldInfo fiPageScrollers = tSettings.GetField("_pageScrollers", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(fiPageScrollers != null, "_pageScrollers field must exist on SettingsForm");
                IDictionary pageScrollers = fiPageScrollers.GetValue(form1) as IDictionary;
                Assert(pageScrollers != null && pageScrollers.Count == 6, "_pageScrollers must contain all 6 page scroller panels");

                FieldInfo fiRightContentPanel = tSettings.GetField("_rightContentPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(fiRightContentPanel != null, "_rightContentPanel field must exist");

                foreach (string tabKey in tabs)
                {
                    miSwitchTab.Invoke(form1, new object[] { tabKey });
                    Application.DoEvents();

                    Panel activePanel = fiRightContentPanel.GetValue(form1) as Panel;
                    Assert(activePanel != null, "Active right content panel must not be null for " + tabKey);
                    Assert(activePanel.Visible, "Active panel must be visible for tab: " + tabKey);

                    int visibleCount = 0;
                    foreach (DictionaryEntry de in pageScrollers)
                    {
                        Panel p = de.Value as Panel;
                        if (p != null && p.Visible)
                        {
                            visibleCount++;
                        }
                    }
                    Assert(visibleCount == 1, string.Format("Exactly 1 page scroller panel must be visible, but got {0} on tab {1}", visibleCount, tabKey));

                    // 检查同层级可见卡片/子控件无空间重叠
                    CheckNoOverlapsRecursive(activePanel, tabKey);
                }
                Console.WriteLine("PASS: Six pages switching isolation & no card overlaps");
            }
            finally
            {
                form1.Close();
                form1.Dispose();
                Application.DoEvents();
            }

            // 3. Form 关闭重开生命周期与处置状态验证 (验证同一 Form 实例 Close() -> Hide/Show 且切页，处置后 HWND 销毁)
            object configObj2 = Activator.CreateInstance(tConfig);
            Form form2 = CreateSettingsFormInstance(tSettings, configObj2);
            try
            {
                PropertyInfo piSuppress2 = tSettings.GetProperty("SuppressPersistenceForTesting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (piSuppress2 != null)
                {
                    piSuppress2.SetValue(form2, true, null);
                }

                form2.StartPosition = FormStartPosition.Manual;
                form2.Location = new Point(-20000, -20000);
                form2.ShowInTaskbar = false;
                form2.Show();
                Application.DoEvents();

                Assert(form2.Visible, "SettingsForm must be visible after initial Show()");
                Assert(form2.IsHandleCreated && form2.Handle != IntPtr.Zero, "SettingsForm HWND must be created after Show()");
                Assert(!form2.IsDisposed, "SettingsForm must not be disposed after initial Show()");

                // 同一实例调用 Close() -> 由于 UserClosing 被 Cancel，窗口隐藏而不是 Disposed
                form2.Close();
                Application.DoEvents();

                Assert(!form2.Visible, "SettingsForm must not be visible after Close()");
                Assert(!form2.IsDisposed, "SettingsForm must not be disposed after Close() due to UserClosing cancellation");

                // 再次 Show() 并执行切页验证
                form2.Show();
                Application.DoEvents();

                Assert(form2.Visible, "SettingsForm must be visible after subsequent Show()");
                Assert(!form2.IsDisposed, "SettingsForm must remain undisposed after subsequent Show()");

                MethodInfo miSwitchTab2 = tSettings.GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert(miSwitchTab2 != null, "SwitchTab method must exist on SettingsForm");
                miSwitchTab2.Invoke(form2, new object[] { "Watermark" });
                Application.DoEvents();
                miSwitchTab2.Invoke(form2, new object[] { "General" });
                Application.DoEvents();

                FieldInfo fiRightContentPanel2 = tSettings.GetField("_rightContentPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fiRightContentPanel2 != null)
                {
                    Panel activePanel2 = fiRightContentPanel2.GetValue(form2) as Panel;
                    Assert(activePanel2 != null && activePanel2.Visible, "Active content panel must be visible after reshow tab switch");
                }
            }
            finally
            {
                if (!form2.IsDisposed)
                {
                    form2.Dispose();
                    Application.DoEvents();
                }
            }

            // 处置后断言：IsDisposed 且 !IsHandleCreated
            Assert(form2.IsDisposed, "SettingsForm must be disposed after Dispose()");
            Assert(!form2.IsHandleCreated, "SettingsForm HWND must not be created after Dispose()");
            Console.WriteLine("PASS: SettingsForm hide, reshow lifecycle and clean disposal verified");

            // 4. 验证 SuppressPersistenceForTesting 且无真实配置写入
            if (!string.IsNullOrEmpty(configPath))
            {
                bool configExistedAfter = File.Exists(configPath);
                Assert(configExistedBefore == configExistedAfter, "Config file existence changed during SuppressPersistenceForTesting test!");
                if (configExistedBefore)
                {
                    DateTime configMtimeAfter = File.GetLastWriteTimeUtc(configPath);
                    string configHashAfter = ComputeFileSha256(configPath);
                    Assert(configMtimeBefore == configMtimeAfter, "Config file modification time changed during test!");
                    Assert(configHashBefore == configHashAfter, "Config file content changed during test!");
                }
                Console.WriteLine("PASS: Zero persistence side effects verified");
            }
        }

        private static void CheckNoOverlapsRecursive(Control parent, string tabContext)
        {
            if (parent.Controls.Count > 1)
            {
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c1 = parent.Controls[i];
                    if (!c1.Visible) continue;

                    for (int j = i + 1; j < parent.Controls.Count; j++)
                    {
                        Control c2 = parent.Controls[j];
                        if (!c2.Visible) continue;

                        if (c1.Bounds.IntersectsWith(c2.Bounds))
                        {
                            throw new Exception(string.Format(
                                "Overlap collision detected on tab [{0}] in container [{1}]: {2} (Bounds={3}, Text='{4}') collides with {5} (Bounds={6}, Text='{7}')",
                                tabContext,
                                parent.GetType().Name,
                                c1.GetType().Name,
                                c1.Bounds,
                                c1.Text,
                                c2.GetType().Name,
                                c2.Bounds,
                                c2.Text));
                        }
                    }
                }
            }

            foreach (Control child in parent.Controls)
            {
                if (child.Visible)
                {
                    CheckNoOverlapsRecursive(child, tabContext);
                }
            }
        }

        private static string ComputeFileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
