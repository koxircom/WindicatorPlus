using System.Text.Json;

namespace IndepenDesk;

/// <summary>Geçiş bilgisi: OSD ve animasyon için.</summary>
internal sealed record SwitchInfo(string Device, int Ordinal, int LocalIndex, int LocalCount, int GlobalNumber);

internal sealed record WindowEntry(IntPtr Handle, string Title);
internal sealed record DesktopEntry(int LocalIndex, int GlobalNumber, bool IsCurrent, IReadOnlyList<WindowEntry> Windows);
internal sealed record MonitorEntry(string Device, int Ordinal, IReadOnlyList<DesktopEntry> Desktops);

/// <summary>
/// Monitör başına bağımsız sanal masaüstü yöneticisi.
/// Windows'un global sanal masaüstü sistemini kullanmaz; bunun yerine her monitör için
/// pencere setleri tutar ve geçişlerde yalnızca o monitördeki pencereleri gizler/gösterir.
/// Masaüstleri dinamiktir ve her monitörde bağımsız sayıdadır; numaralandırma monitörler
/// arasında globaldir (Monitör 1: 1-2-3, Monitör 2: 4-5-6 ...).
/// </summary>
internal sealed class DesktopManager
{
    public const int MaxDesktopsPerMonitor = 9;

    private sealed class MonitorState
    {
        public required string Device;
        public List<HashSet<IntPtr>> Desktops = new() { new HashSet<IntPtr>() };
        public List<IntPtr> LastActive = new() { IntPtr.Zero };
        public int Current;
    }

    private readonly Dictionary<string, MonitorState> _monitors = new();
    private readonly HashSet<IntPtr> _hidden = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly string _stateFile;

    /// <summary>Geçiş kesinleşti, pencereler henüz gizlenmedi: (cihaz, eski index, yeni index).
    /// Animasyon katmanının ekran görüntüsünü bu anda alması gerekir.</summary>
    public event Action<string, int, int>? SwitchStarting;

    /// <summary>Geçiş tamamlandı (veya uçta OSD tazelemesi).</summary>
    public event Action<SwitchInfo>? DesktopSwitched;

    private static readonly string[] ClassBlacklist =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"
    };

    public DesktopManager() : this(null) { }

    public DesktopManager(string? stateFile = null)
    {
        if (string.IsNullOrEmpty(stateFile))
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IndepenDesk");
            Directory.CreateDirectory(dir);
            _stateFile = Path.Combine(dir, "hidden.json");
        }
        else
        {
            _stateFile = stateFile;
        }
        RecoverPreviousSession();
    }

    // ---------- kalıcılık ve kurtarma ----------

    private void RecoverPreviousSession()
    {
        try
        {
            if (!File.Exists(_stateFile)) return;
            var handles = JsonSerializer.Deserialize<List<long>>(File.ReadAllText(_stateFile));
            if (handles != null)
                foreach (long h in handles)
                {
                    var hwnd = new IntPtr(h);
                    if (Native.IsWindow(hwnd) && !Native.IsWindowVisible(hwnd))
                        Native.ShowWindow(hwnd, Native.SW_SHOWNA);
                }
            File.Delete(_stateFile);
        }
        catch { /* kurtarma en iyi çabadır; başlangıcı engellemesin */ }
    }

    private void PersistHidden()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_hidden.Select(h => h.ToInt64()).ToList()));
        }
        catch { }
    }

    // ---------- pencere uygunluğu ----------

    private bool IsEligible(IntPtr h)
    {
        if (!Native.IsWindowVisible(h)) return false;
        if (Native.GetAncestor(h, Native.GA_ROOT) != h) return false;
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == _ownPid) return false;
        long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if (Native.GetWindowTextLength(h) == 0) return false;
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        string cls = Native.GetWindowClass(h);
        return !ClassBlacklist.Contains(cls);
    }

    private static List<IntPtr> EnumerateTopLevelWindows()
    {
        var list = new List<IntPtr>();
        Native.EnumWindows((h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    // ---------- numaralandırma ----------

    /// <summary>Monitörleri ekran düzenine göre (soldan sağa) sıralar; numaralandırma bu sıraya dayanır.</summary>
    private List<MonitorState> OrderedMonitors()
    {
        var bounds = Screen.AllScreens.ToDictionary(s => s.DeviceName, s => s.Bounds);
        return _monitors.Values
            .OrderBy(m => bounds.TryGetValue(m.Device, out var b) ? b.X : int.MaxValue)
            .ThenBy(m => bounds.TryGetValue(m.Device, out var b) ? b.Y : 0)
            .ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Tüm monitörlerin mevcut masaüstü durumunu salt okunur olarak döndürür.
    /// Kesinlikle Sync() çağırmaz, pencereleri listelemez ve durumu değiştirmez.
    /// </summary>
    public IReadOnlyList<SwitchInfo> GetCurrentInfos()
    {
        var ordered = OrderedMonitors();
        var list = new List<SwitchInfo>(ordered.Count);
        int globalBase = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var st = ordered[i];
            int ordinal = i + 1;
            list.Add(new SwitchInfo(st.Device, ordinal, st.Current, st.Desktops.Count, globalBase + st.Current + 1));
            globalBase += st.Desktops.Count;
        }
        return list;
    }

    internal void SetupTestMonitor(string device, int desktopCount, int currentIndex = 0)
    {
        var st = new MonitorState
        {
            Device = device,
            Desktops = Enumerable.Range(0, Math.Clamp(desktopCount, 1, MaxDesktopsPerMonitor))
                                 .Select(_ => new HashSet<IntPtr>())
                                 .ToList(),
            LastActive = Enumerable.Range(0, Math.Clamp(desktopCount, 1, MaxDesktopsPerMonitor))
                                   .Select(_ => IntPtr.Zero)
                                   .ToList(),
            Current = Math.Clamp(currentIndex, 0, desktopCount - 1)
        };
        _monitors[device] = st;
    }

    private SwitchInfo BuildInfo(MonitorState st)
    {
        var ordered = OrderedMonitors();
        int ordinal = ordered.IndexOf(st) + 1;
        int globalBase = ordered.TakeWhile(m => m != st).Sum(m => m.Desktops.Count);
        return new SwitchInfo(st.Device, ordinal, st.Current, st.Desktops.Count, globalBase + st.Current + 1);
    }

    /// <summary>Global masaüstü numarasını (1 tabanlı) sahibi monitöre ve yerel index'e çözer.</summary>
    private (MonitorState st, int local)? ResolveGlobal(int number)
    {
        int n = number;
        foreach (var m in OrderedMonitors())
        {
            if (n <= m.Desktops.Count) return (m, n - 1);
            n -= m.Desktops.Count;
        }
        return null;
    }

    // ---------- yapı yönetimi ----------

    private static void AddDesktop(MonitorState st)
    {
        st.Desktops.Add(new HashSet<IntPtr>());
        st.LastActive.Add(IntPtr.Zero);
    }

    /// <summary>Aktif masaüstünün gerisindeki boş son masaüstlerini kaldırır.</summary>
    private static void PruneTrailingEmpty(MonitorState st)
    {
        while (st.Desktops.Count - 1 > st.Current && st.Desktops[^1].Count == 0)
        {
            st.Desktops.RemoveAt(st.Desktops.Count - 1);
            st.LastActive.RemoveAt(st.LastActive.Count - 1);
        }
    }

    /// <summary>
    /// Durumu gerçekle senkronlar: yeni pencereleri sahiplen, kapananları temizle,
    /// monitör değiştirenleri taşı, kaybolan monitörlerin gizli pencerelerini kurtar.
    /// Invariant: görünür bir pencere her zaman bulunduğu monitörün aktif masaüstü setindedir.
    /// </summary>
    public void Sync()
    {
        var currentDevices = Screen.AllScreens.Select(s => s.DeviceName).ToHashSet();

        foreach (string dev in currentDevices)
            if (!_monitors.ContainsKey(dev))
                _monitors[dev] = new MonitorState { Device = dev };

        foreach (string dev in _monitors.Keys.Where(d => !currentDevices.Contains(d)).ToList())
        {
            foreach (var set in _monitors[dev].Desktops)
                foreach (var h in set)
                    if (Native.IsWindow(h) && !Native.IsWindowVisible(h))
                    {
                        Native.ShowWindow(h, Native.SW_SHOWNA);
                        _hidden.Remove(h);
                    }
            _monitors.Remove(dev);
        }

        foreach (var st in _monitors.Values)
            foreach (var set in st.Desktops)
                set.RemoveWhere(h =>
                {
                    if (Native.IsWindow(h)) return false;
                    _hidden.Remove(h);
                    return true;
                });

        foreach (var h in EnumerateTopLevelWindows())
        {
            if (!IsEligible(h)) continue;
            string? dev = Native.GetMonitorDeviceOfWindow(h);
            if (dev == null || !_monitors.TryGetValue(dev, out var st)) continue;

            if (!st.Desktops[st.Current].Contains(h))
            {
                // Başka bir set'te kayıtlıysa oradan çıkar (monitör değiştirmiş
                // veya gizliyken uygulama tarafından tekrar gösterilmiş olabilir)
                foreach (var other in _monitors.Values)
                    foreach (var set in other.Desktops)
                        set.Remove(h);
                st.Desktops[st.Current].Add(h);
                _hidden.Remove(h);
            }
        }

        foreach (var st in _monitors.Values)
            PruneTrailingEmpty(st);

        PersistHidden();
    }

    /// <summary>Genel bakış arayüzü için tam düzen (global numaralarla).</summary>
    public IReadOnlyList<MonitorEntry> GetLayout()
    {
        Sync();
        var result = new List<MonitorEntry>();
        int global = 0, ordinal = 0;
        foreach (var st in OrderedMonitors())
        {
            ordinal++;
            var desktops = new List<DesktopEntry>();
            for (int i = 0; i < st.Desktops.Count; i++)
            {
                global++;
                var windows = st.Desktops[i]
                    .Where(Native.IsWindow)
                    .Select(h =>
                    {
                        string t = Native.GetWindowTitle(h);
                        return new WindowEntry(h, t.Length > 0 ? t : Native.GetWindowClass(h));
                    })
                    .ToList();
                desktops.Add(new DesktopEntry(i, global, i == st.Current, windows));
            }
            result.Add(new MonitorEntry(st.Device, ordinal, desktops));
        }
        return result;
    }

    // ---------- geçişler ----------

    /// <summary>Fare imlecinin bulunduğu monitörde bir sonraki/önceki masaüstüne geçer.
    /// Son masaüstünde ileri geçiş, masaüstünde pencere varsa yeni masaüstü oluşturur.</summary>
    public void SwitchRelative(int delta)
    {
        string? dev = Native.GetMonitorDeviceUnderCursor();
        if (dev == null) return;
        Sync();
        if (!_monitors.TryGetValue(dev, out var st)) return;

        int target = st.Current + delta;
        if (target < 0)
        {
            DesktopSwitched?.Invoke(BuildInfo(st)); // uçta: sadece OSD göster
            return;
        }
        if (target >= st.Desktops.Count)
        {
            bool canGrow = delta > 0
                && st.Desktops.Count < MaxDesktopsPerMonitor
                && st.Desktops[st.Current].Count > 0; // boş masaüstünden yenisi açılmaz
            if (!canGrow)
            {
                DesktopSwitched?.Invoke(BuildInfo(st));
                return;
            }
            AddDesktop(st);
            target = st.Desktops.Count - 1;
        }
        SwitchToCore(st, target);
    }

    /// <summary>Global masaüstü numarasına geçer (hangi monitörde olduğunu kendisi bulur).</summary>
    public void SwitchToGlobal(int number)
    {
        Sync();
        var resolved = ResolveGlobal(number);
        if (resolved != null)
            SwitchToCore(resolved.Value.st, resolved.Value.local);
    }

    /// <summary>Belirli monitörde belirli yerel masaüstüne geçer.</summary>
    public void SwitchTo(string device, int localIndex)
    {
        Sync();
        if (_monitors.TryGetValue(device, out var st) && localIndex >= 0 && localIndex < st.Desktops.Count)
            SwitchToCore(st, localIndex);
    }

    /// <summary>Verilen monitörde yeni boş masaüstü oluşturur ve ona geçer.</summary>
    public void CreateDesktopAndSwitch(string device)
    {
        Sync();
        if (!_monitors.TryGetValue(device, out var st)) return;
        if (st.Desktops.Count >= MaxDesktopsPerMonitor) return;
        AddDesktop(st);
        SwitchToCore(st, st.Desktops.Count - 1);
    }

    private void SwitchToCore(MonitorState st, int target)
    {
        if (st.Current == target)
        {
            DesktopSwitched?.Invoke(BuildInfo(st));
            return;
        }

        SwitchStarting?.Invoke(st.Device, st.Current, target);

        st.LastActive[st.Current] = Native.GetForegroundWindow();

        foreach (var h in st.Desktops[st.Current].ToList())
        {
            if (!Native.IsWindow(h)) continue;
            if (Native.IsWindowVisible(h) && Native.ShowWindow(h, Native.SW_HIDE))
                _hidden.Add(h);
        }

        st.Current = target;

        foreach (var h in st.Desktops[target].ToList())
        {
            if (!Native.IsWindow(h)) { st.Desktops[target].Remove(h); continue; }
            Native.ShowWindow(h, Native.SW_SHOWNA);
            _hidden.Remove(h);
        }

        // Odağı hedef masaüstünde en son aktif olan pencereye ver
        IntPtr focus = st.LastActive[target];
        if (!Native.IsWindow(focus) || !st.Desktops[target].Contains(focus))
            focus = st.Desktops[target].FirstOrDefault(h => Native.IsWindow(h) && !Native.IsIconic(h));
        if (focus != IntPtr.Zero)
            Native.SetForegroundWindow(focus);

        PruneTrailingEmpty(st);
        PersistHidden();
        DesktopSwitched?.Invoke(BuildInfo(st));
    }

    // ---------- pencere ve masaüstü taşıma ----------

    /// <summary>Aktif pencereyi kendi monitöründe bitişik masaüstüne taşır ve oraya geçer.
    /// Son masaüstünden ileri taşıma yeni masaüstü oluşturur.</summary>
    public void MoveActiveWindow(int delta)
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || !IsEligible(fg)) return;
        Sync();
        string? dev = Native.GetMonitorDeviceOfWindow(fg);
        if (dev == null || !_monitors.TryGetValue(dev, out var st)) return;

        int target = st.Current + delta;
        if (target < 0) return;
        if (target >= st.Desktops.Count)
        {
            if (delta <= 0 || st.Desktops.Count >= MaxDesktopsPerMonitor) return;
            AddDesktop(st);
            target = st.Desktops.Count - 1;
        }

        st.Desktops[st.Current].Remove(fg);
        st.Desktops[target].Add(fg);
        st.LastActive[target] = fg;
        SwitchToCore(st, target);
    }

    /// <summary>Bir pencereyi herhangi bir monitörün herhangi bir masaüstüne taşır
    /// (genel bakıştaki sürükle-bırak ve sağ tık menüsü bunu kullanır).</summary>
    public void MoveWindowToDesktop(IntPtr h, string dstDevice, int dstLocal)
    {
        Sync();
        if (!Native.IsWindow(h)) return;
        if (!_monitors.TryGetValue(dstDevice, out var dst)) return;
        if (dstLocal < 0 || dstLocal >= dst.Desktops.Count) return;

        string? srcDevice = null;
        foreach (var st in _monitors.Values)
            foreach (var set in st.Desktops)
                if (set.Remove(h))
                    srcDevice = st.Device;

        if (srcDevice != null && srcDevice != dstDevice)
            RepositionWindow(h, srcDevice, dstDevice);

        dst.Desktops[dstLocal].Add(h);

        if (dst.Current == dstLocal)
        {
            if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOWNA);
            _hidden.Remove(h);
        }
        else if (Native.IsWindowVisible(h) && Native.ShowWindow(h, Native.SW_HIDE))
        {
            _hidden.Add(h);
        }

        foreach (var st in _monitors.Values) PruneTrailingEmpty(st);
        PersistHidden();
    }

    /// <summary>Bir masaüstünü tüm pencereleriyle başka monitöre taşır (sonuna eklenir).</summary>
    public void MoveDesktopToMonitor(string srcDevice, int srcLocal, string dstDevice)
    {
        Sync();
        if (srcDevice == dstDevice) return;
        if (!_monitors.TryGetValue(srcDevice, out var src)) return;
        if (!_monitors.TryGetValue(dstDevice, out var dst)) return;
        if (srcLocal < 0 || srcLocal >= src.Desktops.Count) return;
        if (dst.Desktops.Count >= MaxDesktopsPerMonitor) return;

        var set = src.Desktops[srcLocal];
        var last = src.LastActive[srcLocal];
        bool wasCurrent = src.Current == srcLocal;

        src.Desktops.RemoveAt(srcLocal);
        src.LastActive.RemoveAt(srcLocal);
        if (src.Desktops.Count == 0) AddDesktop(src);
        if (src.Current > srcLocal) src.Current--;
        if (src.Current >= src.Desktops.Count) src.Current = src.Desktops.Count - 1;

        // Taşınan pencereleri hedef monitöre konumlandır ve gizle (eklenen masaüstü aktif değil)
        foreach (var h in set.ToList())
        {
            if (!Native.IsWindow(h)) { set.Remove(h); continue; }
            RepositionWindow(h, srcDevice, dstDevice);
            if (Native.IsWindowVisible(h) && Native.ShowWindow(h, Native.SW_HIDE))
                _hidden.Add(h);
        }

        dst.Desktops.Add(set);
        dst.LastActive.Add(last);

        // Kaynak monitörde aktif masaüstü taşındıysa kalan aktif masaüstünü görünür yap
        if (wasCurrent)
            foreach (var h in src.Desktops[src.Current])
                if (Native.IsWindow(h) && !Native.IsWindowVisible(h))
                {
                    Native.ShowWindow(h, Native.SW_SHOWNA);
                    _hidden.Remove(h);
                }

        PruneTrailingEmpty(src);
        PersistHidden();
    }

    /// <summary>Pencereyi kaynak monitördeki göreli konumunu koruyarak hedef monitöre taşır.</summary>
    private static void RepositionWindow(IntPtr h, string srcDevice, string dstDevice)
    {
        var srcScreen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == srcDevice);
        var dstScreen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == dstDevice);
        if (srcScreen == null || dstScreen == null) return;
        if (!Native.GetWindowRect(h, out var r)) return;

        bool zoomed = Native.IsZoomed(h);
        if (zoomed) Native.ShowWindow(h, Native.SW_RESTORE);

        var sa = srcScreen.WorkingArea;
        var da = dstScreen.WorkingArea;
        int w = Math.Min(r.Right - r.Left, da.Width);
        int hgt = Math.Min(r.Bottom - r.Top, da.Height);
        double relX = sa.Width > 0 ? (r.Left - sa.Left) / (double)sa.Width : 0;
        double relY = sa.Height > 0 ? (r.Top - sa.Top) / (double)sa.Height : 0;
        int x = da.Left + (int)(relX * da.Width);
        int y = da.Top + (int)(relY * da.Height);
        x = Math.Clamp(x, da.Left, Math.Max(da.Left, da.Right - w));
        y = Math.Clamp(y, da.Top, Math.Max(da.Top, da.Bottom - hgt));

        Native.SetWindowPos(h, IntPtr.Zero, x, y, w, hgt, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        if (zoomed) Native.ShowWindow(h, Native.SW_MAXIMIZE);
    }

    /// <summary>Tüm gizli pencereleri geri getirir (çıkışta ve tray menüsünden çağrılır).</summary>
    public void RestoreAll()
    {
        foreach (var st in _monitors.Values)
        {
            foreach (var set in st.Desktops)
                foreach (var h in set)
                    if (Native.IsWindow(h) && !Native.IsWindowVisible(h))
                        Native.ShowWindow(h, Native.SW_SHOWNA);
            st.Current = 0;
        }
        foreach (var h in _hidden)
            if (Native.IsWindow(h) && !Native.IsWindowVisible(h))
                Native.ShowWindow(h, Native.SW_SHOWNA);
        _hidden.Clear();
        try { File.Delete(_stateFile); } catch { }
    }
}
