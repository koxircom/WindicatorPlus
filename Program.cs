using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("1.3.13.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.13.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.3.13")]

namespace WindicatorPlus
{
    static class Program
    {
        private const string MUTEX_NAME = @"Global\WindicatorPlus_SingleInstance_Mutex_987213";
        private static Mutex _appMutex;

        [STAThread]
        static void Main(string[] args)
        {
            // Enable Per-Monitor V2 DPI awareness to eliminate any text blurriness
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
            }
            catch
            {
                try { NativeMethods.SetProcessDpiAwareness(NativeMethods.PROCESS_PER_MONITOR_DPI_AWARE); } catch { }
            }

            // Single instance check
            bool createdNew;
            _appMutex = new Mutex(true, MUTEX_NAME, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Windicator+ 已经在后台运行中！\n请查看屏幕右下角任务栏托盘图标。",
                    "Windicator+ 提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            RuntimeTrace.LogAppStart(Process.GetCurrentProcess().Id, "1.3.13");

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig config = ConfigManager.Load();

            // Create invisible watermark window
            WatermarkWindow watermark = new WatermarkWindow();
            watermark.ApplyConfig(config);

            // Desktop watcher
            DesktopWatcher watcher = new DesktopWatcher();

            // Desktop loop switcher (wrap-around virtual desktop)
            DesktopLoopSwitcher loopSwitcher = new DesktopLoopSwitcher(watcher, config);
            loopSwitcher.Start();

            // Tray manager
            TrayIconManager trayManager = new TrayIconManager(config, watermark, watcher);

            // Wire desktop change event (dispatched to UI thread)
            watcher.DesktopChanged += (s, e) =>
            {
                if (watermark.IsHandleCreated && !watermark.IsDisposed)
                {
                    try
                    {
                        watermark.BeginInvoke(new Action(() =>
                        {
                            trayManager.UpdateDesktop(e.CurrentDesktop, e.TotalDesktops);
                        }));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Dispatch error: " + ex.Message);
                    }
                }
            };

            // Initial query & display
            int initCur, initTotal;
            watcher.QueryDesktops(out initCur, out initTotal);
            trayManager.UpdateDesktop(initCur, initTotal);

            // Start background registry monitoring
            watcher.Start();

            // Handle clean exit
            trayManager.ExitRequested += () =>
            {
                // Ensure current menu close animation and focus return complete first
                // to avoid ObjectDisposedException when menu closes
                if (SynchronizationContext.Current != null)
                {
                    SynchronizationContext.Current.Post(_ =>
                    {
                        try { loopSwitcher.Dispose(); } catch { }
                        try { watcher.Stop(); } catch { }
                        try { watermark.Dispose(); } catch { }
                        try { trayManager.Dispose(); } catch { }
                        RuntimeTrace.Shutdown("TrayExit");
                        Application.Exit();
                    }, null);
                }
                else
                {
                    RuntimeTrace.Shutdown("TrayExit");
                    Application.Exit();
                }
            };

            // Run message loop
            Application.Run();

            RuntimeTrace.Shutdown("AppRunExit");

            // Cleanup
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Close();
            }
        }
    }
}
