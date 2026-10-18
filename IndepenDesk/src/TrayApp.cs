namespace IndepenDesk;

/// <summary>Tray ikonu, global kısayollar, geçiş animasyonu ve periyodik senkronizasyon.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int HkPrev = 1;
    private const int HkNext = 2;
    private const int HkMovePrev = 3;
    private const int HkMoveNext = 4;
    private const int HkOverview = 5;
    private const int HkDesktopBase = 10; // 10..18 => global masaüstü 1..9

    private readonly DesktopManager _manager = new();
    private readonly NotifyIcon _tray;
    private readonly OsdForm? _osd;
    private readonly SlideAnimator _animator = new();
    private readonly HotkeyWindow _hotkeys;
    private readonly System.Windows.Forms.Timer _syncTimer = new() { Interval = 600 };
    private readonly bool _isBridgeMode;
    private readonly IndicatorStateServer? _stateServer;

    public bool IsBridgeMode => _isBridgeMode;

    public TrayApp(bool isBridgeMode = false)
    {
        _isBridgeMode = isBridgeMode;
        _hotkeys = new HotkeyWindow(OnHotkey);

        if (!_isBridgeMode)
        {
            StartupManager.ApplyOnLaunch();
            _osd = new OsdForm();
        }
        else
        {
            _stateServer = new IndicatorStateServer();
        }

        _tray = new NotifyIcon
        {
            Icon = CreateIcon(),
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OverviewForm.Toggle(_manager);
        BuildMenu();

        _manager.SwitchStarting += (device, from, to) =>
        {
            if (!OverviewForm.IsOpen) // genel bakış açıkken animasyon oynatma
                _animator.Begin(device, to > from ? +1 : -1);
        };

        _manager.DesktopSwitched += info =>
        {
            _animator.Commit(info.Device);
            _osd?.ShowSwitch(info);
            _stateServer?.UpdateSnapshot(_manager.GetCurrentInfos());
        };

        RegisterHotkeys();

        _manager.Sync();
        _stateServer?.UpdateSnapshot(_manager.GetCurrentInfos());

        _syncTimer.Tick += (_, _) =>
        {
            if (!OverviewForm.IsOpen)
                _manager.Sync();
            _stateServer?.UpdateSnapshot(_manager.GetCurrentInfos());
        };
        _syncTimer.Start();
    }

    /// <summary>Menüyü (yeniden) kurar; dil değişince tekrar çağrılır.</summary>
    private void BuildMenu()
    {
        _tray.Text = Truncate(L.T("tray.tooltip"), 63);

        var menu = new ContextMenuStrip();

        var versionItem = new ToolStripMenuItem($"IndepenDesk  v{UpdateChecker.CurrentVersion}") { Enabled = false };
        menu.Items.Add(versionItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(L.T("menu.overview"), null, (_, _) => OverviewForm.Toggle(_manager));
        menu.Items.Add(L.T("menu.restore"), null, (_, _) => _manager.RestoreAll());
        menu.Items.Add(L.T("menu.help"), null, (_, _) => HelpForm.ShowHelp());
        menu.Items.Add(new ToolStripSeparator());

        var langMenu = new ToolStripMenuItem(L.T("menu.language"));
        var auto = new ToolStripMenuItem(L.T("menu.lang.auto")) { Checked = L.Override == "auto" };
        auto.Click += (_, _) => { L.SetOverride("auto"); BuildMenu(); };
        langMenu.DropDownItems.Add(auto);
        langMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (code, native) in L.Supported)
        {
            var item = new ToolStripMenuItem(native) { Checked = L.Override == code };
            string c = code;
            item.Click += (_, _) => { L.SetOverride(c); BuildMenu(); };
            langMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(langMenu);

        // MSIX'te başlangıç Windows Ayarları'ndan yönetilir; menü öğesi o sayfayı açar.
        var startupItem = new ToolStripMenuItem(L.T("menu.startup"));
        if (StartupManager.IsPackaged)
        {
            startupItem.Click += (_, _) => StartupManager.OpenWindowsStartupSettings();
        }
        else
        {
            startupItem.Checked = StartupManager.Enabled;
            startupItem.CheckOnClick = true;
            startupItem.CheckedChanged += (_, _) => StartupManager.SetEnabled(startupItem.Checked);
        }
        menu.Items.Add(startupItem);

        menu.Items.Add(L.T("menu.update"), null, async (_, _) =>
            await UpdateChecker.CheckAndNotifyAsync(new WindowWrapper(_hotkeys.Handle)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("menu.exit"), null, (_, _) => ExitThread());

        _tray.ContextMenuStrip = menu;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HkPrev: _manager.SwitchRelative(-1); break;
            case HkNext: _manager.SwitchRelative(+1); break;
            case HkMovePrev: _manager.MoveActiveWindow(-1); break;
            case HkMoveNext: _manager.MoveActiveWindow(+1); break;
            case HkOverview: OverviewForm.Toggle(_manager); break;
            default:
                if (id >= HkDesktopBase && id < HkDesktopBase + 9)
                    _manager.SwitchToGlobal(id - HkDesktopBase + 1);
                break;
        }
    }

    private void RegisterHotkeys()
    {
        var failed = new List<string>();
        void Reg(int id, uint mods, uint vk, string label)
        {
            if (!Native.RegisterHotKey(_hotkeys.Handle, id, mods | Native.MOD_NOREPEAT, vk))
                failed.Add(label);
        }

        const uint ca = Native.MOD_CONTROL | Native.MOD_ALT;
        Reg(HkPrev, ca, Native.VK_LEFT, "Ctrl+Alt+←");
        Reg(HkNext, ca, Native.VK_RIGHT, "Ctrl+Alt+→");
        Reg(HkOverview, ca, Native.VK_UP, "Ctrl+Alt+↑");
        Reg(HkMovePrev, ca | Native.MOD_SHIFT, Native.VK_LEFT, "Ctrl+Alt+Shift+←");
        Reg(HkMoveNext, ca | Native.MOD_SHIFT, Native.VK_RIGHT, "Ctrl+Alt+Shift+→");
        for (int i = 0; i < 9; i++)
            Reg(HkDesktopBase + i, ca, (uint)('1' + i), $"Ctrl+Alt+{i + 1}");

        if (failed.Count > 0)
            _tray?.ShowBalloonTip(4000, "IndepenDesk",
                L.T("msg.hotkeyFail") + string.Join(", ", failed), ToolTipIcon.Warning);
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b1 = new SolidBrush(Color.FromArgb(0, 120, 215));
            using var b2 = new SolidBrush(Color.FromArgb(90, 200, 250));
            g.FillRectangle(b1, 2, 6, 13, 20);
            g.FillRectangle(b2, 17, 6, 13, 20);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _stateServer?.Dispose();
        _syncTimer.Stop();
        for (int id = 1; id < HkDesktopBase + 9; id++)
            Native.UnregisterHotKey(_hotkeys.Handle, id);
        _manager.RestoreAll();
        _animator.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _osd?.Dispose();
        base.ExitThreadCore();
    }

    private sealed class WindowWrapper : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowWrapper(IntPtr handle) => Handle = handle;
    }

    /// <summary>WM_HOTKEY mesajlarını almak için görünmez pencere.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public HotkeyWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
                _onHotkey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }
}
