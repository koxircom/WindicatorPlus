using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindicatorPlus
{
    /// <summary>
    /// 独立多显示器桌面角标与托盘运行上下文。
    /// 纯新运行模式，可直接由 Application.Run(new IndependentDesktopContext()) 启动。
    /// 严格遵循只读架构：
    /// 1. 不执行桌面切换命令。
    /// 2. 只读获取系统配置 ConfigManager.Load()，独立命名与颜色写入 independent-desktops.xml。
    /// 3. 数据断开或3秒过期时坚决撤下角标，无 native 伪回退。
    /// 4. 动态响应屏幕插拔，断开屏幕立即销毁 Form。
    /// 5. UI Dispatch Control 承载全部跨线程通知，调度至 UI 线程执行。
    /// </summary>
    public class IndependentDesktopContext : ApplicationContext
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly IndepenDeskClient _client;
        private readonly IndependentDesktopSettings _settings;
        private readonly AppConfig _cachedConfig;
        private readonly Func<Screen[]> _screenProvider;

        private readonly Control _dispatcherControl;
        private int _reconcileScheduled = 0;

        private readonly Dictionary<string, IndependentBadgeForm> _badgeForms =
            new Dictionary<string, IndependentBadgeForm>(StringComparer.OrdinalIgnoreCase);

        private readonly System.Windows.Forms.Timer _reconciliationTimer;
        private bool _isDisposed;

        public IndependentDesktopContext()
            : this(null, null, null, null)
        {
        }

        public IndependentDesktopContext(IndepenDeskClient clientOverride, IndependentDesktopSettings settingsOverride)
            : this(clientOverride, settingsOverride, null, null)
        {
        }

        public IndependentDesktopContext(IndepenDeskClient clientOverride, IndependentDesktopSettings settingsOverride, AppConfig configOverride, Func<Screen[]> screenProviderOverride)
        {
            // 1. 初始化 UI Dispatch Control 句柄，承接跨线程通知
            _dispatcherControl = new Control();
            _dispatcherControl.CreateControl();
            IntPtr forceHandle = _dispatcherControl.Handle;

            // 2. 加载独立设置与系统配置
            _settings = settingsOverride ?? IndependentDesktopSettings.Load();
            _cachedConfig = configOverride ?? LoadSafeConfig();
            _screenProvider = screenProviderOverride ?? (() => Screen.AllScreens);

            // 3. 初始化托盘组件
            _contextMenu = new ContextMenuStrip();
            _trayIcon = new NotifyIcon
            {
                Text = "Windicator+ [IndepenDesk 独立模式]",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _trayIcon.Icon = CreateDefaultTrayIcon();

            // 4. 初始化屏幕协调定时器
            _reconciliationTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000,
                Enabled = true
            };
            _reconciliationTimer.Tick += OnReconciliationTick;

            // 5. 监听操作系统屏幕配置变更
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // 6. 初始化客户端与事件绑定
            _client = clientOverride ?? new IndepenDeskClient();
            _client.SnapshotReceived += OnClientSnapshotReceived;
            _client.StateChanged += OnClientStateChanged;

            // 7. 首次构建菜单并启动客户端
            ReconcileBadges();
            RebuildContextMenu();
            _client.Start();
        }

        private static AppConfig LoadSafeConfig()
        {
            try
            {
                return ConfigManager.Load() ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public IndepenDeskClient Client
        {
            get { return _client; }
        }

        public IndependentDesktopSettings Settings
        {
            get { return _settings; }
        }

        public ICollection<IndependentBadgeForm> BadgeForms
        {
            get { return _badgeForms.Values; }
        }

        private Icon CreateDefaultTrayIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(16, 16))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(37, 99, 235)))
                        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(16, 185, 129)))
                        {
                            g.FillRectangle(b1, 1, 3, 6, 10);
                            g.FillRectangle(b2, 9, 3, 6, 10);
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        using (Icon tempIcon = Icon.FromHandle(hIcon))
                        {
                            return (Icon)tempIcon.Clone();
                        }
                    }
                    finally
                    {
                        if (hIcon != IntPtr.Zero)
                        {
                            DestroyIcon(hIcon);
                        }
                    }
                }
            }
            catch
            {
                return (Icon)SystemIcons.Application.Clone();
            }
        }

        private void ScheduleReconciliation()
        {
            if (_isDisposed) return;
            if (Interlocked.CompareExchange(ref _reconcileScheduled, 1, 0) == 0)
            {
                try
                {
                    if (_dispatcherControl != null && !_dispatcherControl.IsDisposed && _dispatcherControl.IsHandleCreated)
                    {
                        _dispatcherControl.BeginInvoke(new Action(() =>
                        {
                            Interlocked.Exchange(ref _reconcileScheduled, 0);
                            if (_isDisposed) return;
                            ReconcileBadges();
                            RebuildContextMenu();
                        }));
                    }
                    else
                    {
                        Interlocked.Exchange(ref _reconcileScheduled, 0);
                    }
                }
                catch
                {
                    Interlocked.Exchange(ref _reconcileScheduled, 0);
                }
            }
        }

        private void OnClientSnapshotReceived(object sender, SnapshotEventArgs e)
        {
            ScheduleReconciliation();
        }

        private void OnClientStateChanged(object sender, EventArgs e)
        {
            ScheduleReconciliation();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            ScheduleReconciliation();
        }

        private void OnReconciliationTick(object sender, EventArgs e)
        {
            ScheduleReconciliation();
        }

        /// <summary>
        /// 核心屏幕与角标协调逻辑：
        /// 1. 优先清理物理断开的屏幕（即使处于 stale 状态）。
        /// 2. 若不可用或快照为空，将全部已有角标 Hide。
        /// 3. 若物理屏幕存在但从当前快照中被移除，将旧角标 Dispose。
        /// 4. count <= 1 的角标 Hide，>= 2 显示。
        /// 5. 严格匹配屏幕，绝不 fallback 到主屏。
        /// </summary>
        public void ReconcileBadges()
        {
            if (_isDisposed) return;

            Screen[] allScreens = _screenProvider != null ? _screenProvider() : Screen.AllScreens;
            if (allScreens == null) allScreens = new Screen[0];

            HashSet<string> activeScreenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Screen scr in allScreens)
            {
                if (scr != null && !string.IsNullOrEmpty(scr.DeviceName))
                {
                    activeScreenDevices.Add(scr.DeviceName);
                }
            }

            // 1. 清理已断开屏幕的窗体
            List<string> devicesToDispose = new List<string>();
            foreach (KeyValuePair<string, IndependentBadgeForm> kvp in _badgeForms)
            {
                if (!activeScreenDevices.Contains(kvp.Key))
                {
                    if (kvp.Value != null && !kvp.Value.IsDisposed)
                    {
                        kvp.Value.Dispose();
                    }
                    devicesToDispose.Add(kvp.Key);
                }
            }
            foreach (string dev in devicesToDispose)
            {
                _badgeForms.Remove(dev);
            }

            bool isAvailable = _settings.BadgeEnabled && _client.IsConnected && !_client.IsStale;
            IndepenDeskSnapshot snapshot = _client.CurrentSnapshot;

            if (snapshot != null && snapshot.Monitors.Count == 0 && !_client.IsStale)
            {
                foreach (IndependentBadgeForm form in _badgeForms.Values) form.Dispose();
                _badgeForms.Clear();
                return;
            }

            if (!isAvailable || snapshot == null || snapshot.Monitors == null || snapshot.Monitors.Count == 0)
            {
                foreach (IndependentBadgeForm form in _badgeForms.Values)
                {
                    if (form != null && !form.IsDisposed && form.Visible)
                    {
                        form.Hide();
                    }
                }
                return;
            }

            // 2. 屏幕存在但从快照中移除的旧 badge：Hide 并 Dispose
            HashSet<string> snapshotDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IndepenDeskMonitorInfo mon in snapshot.Monitors)
            {
                if (mon != null && !string.IsNullOrEmpty(mon.Device))
                {
                    snapshotDevices.Add(mon.Device);
                }
            }

            List<string> removedDevices = new List<string>();
            foreach (KeyValuePair<string, IndependentBadgeForm> kvp in _badgeForms)
            {
                if (!snapshotDevices.Contains(kvp.Key))
                {
                    if (kvp.Value != null && !kvp.Value.IsDisposed)
                    {
                        kvp.Value.Hide();
                        kvp.Value.Dispose();
                    }
                    removedDevices.Add(kvp.Key);
                }
            }
            foreach (string dev in removedDevices)
            {
                _badgeForms.Remove(dev);
            }

            // 3. 更新或创建每个屏幕的角标
            foreach (IndepenDeskMonitorInfo mon in snapshot.Monitors)
            {
                IndependentBadgeForm form;
                _badgeForms.TryGetValue(mon.Device, out form);

                // count <= 1 时隐藏
                if (mon.Count <= 1)
                {
                    if (form != null && !form.IsDisposed && form.Visible)
                    {
                        form.Hide();
                    }
                    continue;
                }

                // 严格匹配当前物理屏幕
                Screen matchedScreen = null;
                foreach (Screen scr in allScreens)
                {
                    if (scr != null && string.Equals(scr.DeviceName, mon.Device, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedScreen = scr;
                        break;
                    }
                }

                if (matchedScreen == null)
                {
                    // 未匹配物理屏幕，忽略，绝不 fallback 到主屏
                    continue;
                }

                if (form == null || form.IsDisposed)
                {
                    form = new IndependentBadgeForm();
                    _badgeForms[mon.Device] = form;
                }

                string customName = _settings.GetDesktopName(mon.Device, mon.Index);
                string accentColor = _settings.GetAccentColor(mon.Device, mon.Index);

                form.UpdateBadge(mon, customName, accentColor, snapshot.Monitors.Count, _cachedConfig, matchedScreen);
                if (!form.Visible)
                {
                    form.Show();
                }
            }
        }

        private static void DisposeContextMenuItems(ToolStripItemCollection items)
        {
            if (items == null) return;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                ToolStripItem item = items[i];
                ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
                if (dropDown != null && dropDown.HasDropDownItems)
                {
                    DisposeContextMenuItems(dropDown.DropDownItems);
                }
                item.Dispose();
            }
            items.Clear();
        }

        private void RebuildContextMenu()
        {
            // Snapshot refreshes must not dispose items while the user navigates the menu.
            if (_contextMenu.Visible) return;
            if (_isDisposed) return;

            // 保持当前菜单展开状态不被破坏
            if (_contextMenu.Visible) return;

            DisposeContextMenuItems(_contextMenu.Items);

            // 1. 管道连接状态展示
            string statusText;
            if (_client.IsStale)
            {
                statusText = "IndepenDesk: 数据已过期 (>3s)";
            }
            else if (_client.IsConnected)
            {
                statusText = string.Format("IndepenDesk: 已连接 (Session {0})", _client.ExpectedSessionId);
            }
            else
            {
                statusText = "IndepenDesk: 等待连接...";
            }

            ToolStripMenuItem statusItem = new ToolStripMenuItem(statusText)
            {
                Enabled = false
            };
            _contextMenu.Items.Add(statusItem);
            _contextMenu.Items.Add(new ToolStripSeparator());

            // 2. 每屏当前状态与重命名/改色子菜单
            IndepenDeskSnapshot snapshot = _client.CurrentSnapshot;
            if (snapshot != null && snapshot.Monitors != null && snapshot.Monitors.Count > 0 && !_client.IsStale)
            {
                foreach (IndepenDeskMonitorInfo mon in snapshot.Monitors)
                {
                    string currentName = _settings.GetDesktopName(mon.Device, mon.Index);
                    string displayName = !string.IsNullOrEmpty(currentName)
                        ? string.Format("{0} {1}", mon.Index + 1, currentName)
                        : string.Format("桌面 {0}", mon.Index + 1);

                    string menuHeader = string.Format("显示器 {0} ({1}) - {2}", mon.Ordinal, mon.Device, displayName);
                    ToolStripMenuItem screenItem = new ToolStripMenuItem(menuHeader);

                    // 子菜单：重命名
                    ToolStripMenuItem renameItem = new ToolStripMenuItem("重命名当前桌面别名...", null, (s, e) =>
                    {
                        using (RenameDialog dlg = new RenameDialog(mon.Index + 1, currentName))
                        {
                            if (dlg.ShowDialog() == DialogResult.OK)
                            {
                                _settings.SetDesktopName(mon.Device, mon.Index, dlg.DesktopName);
                                try
                                {
                                    _settings.Save();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("保存桌面别名失败: " + ex.Message, "Windicator+", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                ReconcileBadges();
                                RebuildContextMenu();
                            }
                        }
                    });
                    screenItem.DropDownItems.Add(renameItem);

                    // 子菜单：修改强调色
                    ToolStripMenuItem colorItem = new ToolStripMenuItem("设置当前桌面强调色...", null, (s, e) =>
                    {
                        using (ColorDialog dlg = new ColorDialog())
                        {
                            dlg.FullOpen = true;
                            dlg.AnyColor = true;

                            string hex = _settings.GetAccentColor(mon.Device, mon.Index);
                            try
                            {
                                dlg.Color = ColorTranslator.FromHtml(hex);
                            }
                            catch { }

                            if (dlg.ShowDialog() == DialogResult.OK)
                            {
                                string newHex = string.Format("#{0:X2}{1:X2}{2:X2}", dlg.Color.R, dlg.Color.G, dlg.Color.B);
                                _settings.SetAccentColor(mon.Device, mon.Index, newHex);
                                try
                                {
                                    _settings.Save();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("保存强调色失败: " + ex.Message, "Windicator+", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                ReconcileBadges();
                                RebuildContextMenu();
                            }
                        }
                    });
                    screenItem.DropDownItems.Add(colorItem);

                    _contextMenu.Items.Add(screenItem);
                }
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            // 3. 角标总开关
            ToolStripMenuItem toggleBadgeItem = new ToolStripMenuItem("显示按屏桌面角标", null, (s, e) =>
            {
                _settings.BadgeEnabled = !_settings.BadgeEnabled;
                try
                {
                    _settings.Save();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存角标开关设置失败: " + ex.Message, "Windicator+", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                ReconcileBadges();
                RebuildContextMenu();
            })
            {
                Checked = _settings.BadgeEnabled
            };
            _contextMenu.Items.Add(toggleBadgeItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 4. 退出菜单项
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出", null, (s, e) =>
            {
                ExitThread();
            });
            _contextMenu.Items.Add(exitItem);
        }

        protected override void ExitThreadCore()
        {
            Cleanup();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
            base.Dispose(disposing);
        }

        private void Cleanup()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

                if (_reconciliationTimer != null)
                {
                    _reconciliationTimer.Stop();
                    _reconciliationTimer.Dispose();
                }

                if (_client != null)
                {
                    _client.SnapshotReceived -= OnClientSnapshotReceived;
                    _client.StateChanged -= OnClientStateChanged;
                    _client.Dispose();
                }

                foreach (IndependentBadgeForm form in _badgeForms.Values)
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Dispose();
                    }
                }
                _badgeForms.Clear();

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    if (_trayIcon.Icon != null)
                    {
                        try { _trayIcon.Icon.Dispose(); } catch { }
                    }
                    _trayIcon.Dispose();
                }

                if (_contextMenu != null)
                {
                    DisposeContextMenuItems(_contextMenu.Items);
                    _contextMenu.Dispose();
                }

                if (_dispatcherControl != null)
                {
                    try { _dispatcherControl.Dispose(); } catch { }
                }
            }

        }
    }
}
