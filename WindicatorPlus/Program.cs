using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("1.4.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.4.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.4.0-preview.1")]

namespace WindicatorPlus
{
    static class Program
    {
        private static bool HasArgument(string[] args, string value)
        {
            foreach (string arg in args) if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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

            bool independent = HasArgument(args, "--independesk");
            // Independent companion has its own mutex: it never terminates the installed native mode.
            bool createdNew;
            _appMutex = new Mutex(true, independent ? @"Local\WindicatorPlus_IndepenDesk_v1" : MUTEX_NAME, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Windicator+ 已经在后台运行中！\n请查看屏幕右下角任务栏托盘图标。",
                    "Windicator+ 提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (independent)
            {
                try { using (var context = new IndependentDesktopContext()) Application.Run(context); }
                finally { _appMutex.ReleaseMutex(); _appMutex.Close(); }
                return;
            }

            AppConfig config = ConfigManager.Load();

            // Create invisible watermark window
            WatermarkWindow watermark = new WatermarkWindow();
            watermark.ApplyConfig(config);

            // Desktop watcher
            DesktopWatcher watcher = new DesktopWatcher();

            // Taskbar Tint Overlay (100% mouse-penetrating ambient color per desktop)
            TaskbarTintOverlay tintOverlay = new TaskbarTintOverlay(config, 1);
            if (config.TaskbarTintEnabled)
            {
                tintOverlay.Show();
            }

            // Desktop loop switcher (wrap-around virtual desktop)
            DesktopLoopSwitcher loopSwitcher = new DesktopLoopSwitcher(watcher, config);
            loopSwitcher.Start();

            // Tray manager
            TrayIconManager trayManager = new TrayIconManager(config, watermark, watcher, tintOverlay);

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
                            if (tintOverlay.IsHandleCreated && !tintOverlay.IsDisposed)
                            {
                                tintOverlay.UpdateDesktop(e.CurrentDesktop);
                            }
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
            tintOverlay.UpdateDesktop(initCur);

            // Start background registry monitoring
            watcher.Start();

            // Prewarm settings form in background (Instant 0ms open, zero flicker)
            trayManager.PrewarmSettingsForm();

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
                        try { if (tintOverlay != null && !tintOverlay.IsDisposed) tintOverlay.Dispose(); } catch { }
                        try { trayManager.Dispose(); } catch { }
                        Application.Exit();
                    }, null);
                }
                else
                {
                    Application.Exit();
                }
            };

            // Run message loop
            Application.Run();

            // Cleanup
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Close();
            }
        }
    }
}
