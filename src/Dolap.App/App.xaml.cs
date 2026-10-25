using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Dolap.Core;
using Forms = System.Windows.Forms;

namespace Dolap.App;

static class Hotkeys
{
    /// <summary>"Ctrl+Alt+D" -> (MOD_*, VK). Degiskenler: Ctrl, Alt, Shift, Win.</summary>
    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        var parts = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var p in parts[..^1])
            switch (p.ToLowerInvariant())
            {
                case "alt": mods |= 1; break;
                case "ctrl": mods |= 2; break;
                case "shift": mods |= 4; break;
                case "win": mods |= 8; break;
                default: return false;
            }
        if (!Enum.TryParse<Key>(parts[^1], true, out var k)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        return vk != 0;
    }
}

public partial class App : Application
{
    const string MutexName = "MasaustuDolabi.Single", ShowEventName = "MasaustuDolabi.Show";
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    CollectionStore? _store;
    readonly List<MainWindow> _wins = [];
    MainWindow? _win => _wins.FirstOrDefault();
    public IReadOnlyList<MainWindow> CabinetWindows => _wins;
    Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--app-smoke"))
        {
            VisualSmoke.Start(e.Args);
            return;
        }
        if (e.Args.Contains("--visual-check") || e.Args.Contains("--design-preview"))
        {
            DesignPreview.Start(e.Args);
            return;
        }
        _mutex = new Mutex(true, MutexName, out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first) { _showEvent.Set(); Shutdown(); return; } // tek ornek: mevcut olani one getir

        Resources["BrushedTexture"] = Textures.Brushed();
        try
        {
            Directory.CreateDirectory(Paths.Backups);
            _store = OpenStore();
            _store.DailyBackup(Paths.Backups);
        }
        catch (Exception ex) when (ex is DolapException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageFatal("Veri dosyası açılamadı: " + ex.Message);
            Shutdown(); return;
        }
        foreach (var cab in _store.GetCabinets()) OpenCabinetWindow(cab.Id);
        BuildTray();
        new Thread(() => { while (_showEvent.WaitOne()) Dispatcher.Invoke(ShowAll); }) { IsBackground = true }.Start();
    }

    /// <summary>Bozuk veritabani yeniden adlandirilip korunur; yeni bos koleksiyon acilir.</summary>
    static CollectionStore OpenStore()
    {
        try { return new CollectionStore(Paths.Db); }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Move(Paths.Db, Path.Combine(Paths.Data, $"bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
            return new CollectionStore(Paths.Db);
        }
    }

    static void MessageFatal(string m) => Forms.MessageBox.Show(m, "Masaüstü Dolabı", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);

    public static void ApplyTheme(string id, bool reduceMotion, bool lowQuality)
    {
        var res = Current.Resources;
        var uri = new Uri(id == "blocks" ? "Themes/Blocks.xaml" : "Themes/Metal.xaml", UriKind.Relative);
        var merged = res.MergedDictionaries;
        if (merged.Count > 2) merged.RemoveAt(2);
        merged.Add(new ResourceDictionary { Source = uri });
        res["ReduceMotion"] = reduceMotion;
        if (lowQuality) res["CabinetShadow"] = null; else res.Remove("CabinetShadow");
    }

    void BuildTray()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(Color.FromArgb(70, 76, 84)), 4, 3, 24, 26);
            for (var i = 0; i < 3; i++)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(170, 178, 186)), 6, 5 + i * 8, 20, 6);
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 44, 50)), 12, 7 + i * 8, 8, 2);
            }
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Göster / Gizle", null, (_, _) => ToggleAll());
        menu.Items.Add("Metal Dolap", null, (_, _) => SetTheme("metal"));
        menu.Items.Add("Blok Dolap", null, (_, _) => SetTheme("blocks"));
        menu.Items.Add("Sistem monitörü", null, (_, _) => { if (_win is { } w) w.ApplySettings(w.Settings with { ShowSystemMonitor = !w.Settings.ShowSystemMonitor }); });
        menu.Items.Add("Ayarlar", null, (_, _) => _win?.OpenSettings());
        menu.Items.Add("Yeni dolap ekle", null, (_, _) => { try { AddCabinet(); } catch (DolapException ex) { _win?.ShowToast(ex.Message); } });
        menu.Items.Add("Konumu sıfırla", null, (_, _) => _wins.ForEach(w => w.ResetPosition()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => Quit());
        _tray = new Forms.NotifyIcon { Icon = Icon.FromHandle(bmp.GetHicon()), Text = "Masaüstü Dolabı", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) _win?.ToggleVisible(); };
    }

    void SetTheme(string id) => _wins.ToList().ForEach(w => w.ApplySettings(w.Settings with { ThemeId = id }));

    MainWindow OpenCabinetWindow(Guid id)
    {
        var w = new MainWindow(_store!, cabinetId: id);
        _wins.Add(w);
        w.Show();
        return w;
    }

    public void ToggleAll()
    {
        if (_wins.Any(w => w.IsVisible)) { _wins.ForEach(w => w.Hide()); _monitor?.Hide(); } else ShowAll();
    }
    public void ShowAll() { _wins.ForEach(w => w.ShowCabinet()); _monitor?.Show(); }

    // ---- oda: ofis duvar kagidi (hava + saat) ----
    WeatherNow? _weather; DateTime _weatherAt = DateTime.MinValue; string _weatherFor = "";
    System.Windows.Threading.DispatcherTimer? _roomTimer;
    bool _roomBusy;
    public string WeatherStatus { get; private set; } = "Hava durumu kapalı: gün doğumu/batımı mevsime göre tahmin ediliyor.";

    /// <summary>Duvar kagidini ayara gore gunceller: ofis odasi (hava/saat) ya da tema duvar kagidi. 10 dk'da bir de kendiliginden.</summary>
    public async void RefreshRoom(AppSettings s, bool forceWeather = false)
    {
        if (_roomTimer == null)
        {
            _roomTimer = new() { Interval = TimeSpan.FromMinutes(10) };
            _roomTimer.Tick += (_, _) => { if (_win is { } w && w.Settings.ThemedWallpaper) RefreshRoom(w.Settings); };
            _roomTimer.Start();
        }
        if (s.RoomStyle != "office") { _ = WallpaperService.Apply(s.ThemeId, Dolap.App.MainWindow.SideOf(s) < 0); return; }
        if (_roomBusy) return;
        _roomBusy = true;
        try
        {
            var key = (s.WeatherUseDeviceLocation ? "konum|" : "") + s.WeatherCity;
            if (!s.WeatherEnabled || (!s.WeatherUseDeviceLocation && string.IsNullOrWhiteSpace(s.WeatherCity))) { _weather = null; WeatherStatus = "Hava durumu kapalı: gün doğumu/batımı mevsime göre tahmin ediliyor."; }
            else if (forceWeather || _weatherFor != key || DateTime.Now - _weatherAt > TimeSpan.FromMinutes(30))
            {
                try
                {
                    // Once bilgisayarin konumu (Windows izni), olmazsa yazilan sehir.
                    var loc = s.WeatherUseDeviceLocation ? await WeatherService.DeviceLocation() : null;
                    if (loc is { } l) _weather = await WeatherService.FetchAt(l.Lat, l.Lon, $"Bilgisayarın konumu ({l.Lat:0.00}, {l.Lon:0.00})");
                    else if (!string.IsNullOrWhiteSpace(s.WeatherCity)) _weather = await WeatherService.Fetch(s.WeatherCity);
                    else throw new DolapException("Konum alınamadı (Windows Ayarlar > Gizlilik ve güvenlik > Konum) ve şehir yazılmamış.");
                    _weatherAt = DateTime.Now; _weatherFor = key;
                    var note = s.WeatherUseDeviceLocation && loc == null ? " (konum izni yok, şehir kullanıldı)" : "";
                    WeatherStatus = $"{_weather.Place}: {SkyName(_weather.Sky)}, {(_weather.IsDay ? "gündüz" : "gece")}. Güncellendi {_weatherAt:HH:mm}{note}.";
                }
                catch (DolapException ex) { WeatherStatus = ex.Message + " Son bilinen durum kullanılıyor."; }
            }
            File.WriteAllText(Path.Combine(Paths.Data, "hava-durumu.txt"), WeatherStatus); // tani icin son durum
            var path = OfficeRoom.Render(OfficeLogic.StateFor(s, _weather, DateTime.Now));
            await Task.Run(() => WallpaperService.SetFile(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            WeatherStatus = "Oda duvar kağıdı oluşturulamadı: " + ex.Message;
        }
        finally { _roomBusy = false; }
    }

    static string SkyName(SkyKind k) => k switch { SkyKind.Clear => "açık", SkyKind.Cloudy => "bulutlu", SkyKind.Rain => "yağışlı", _ => "karlı" };

    MonitorWindow? _monitor;
    /// <summary>Ilk dolabin ayarina gore sistem monitorunu acar/kapatir ve konumlar.</summary>
    public void SyncMonitor(AppSettings s)
    {
        if (!s.ShowSystemMonitor) { _monitor?.Close(); _monitor = null; return; }
        if (_monitor == null)
        {
            _monitor = new MonitorWindow();
            _monitor.Moved += (x, y, dev) => { if (_win is { } w) w.ApplySettings(w.Settings with { MonitorX = x, MonitorY = y, MonitorScreen = dev }); };
            _monitor.Show();
        }
        _monitor.ApplyStyle(s.MonitorTheme, s.MonitorScale);
        _monitor.Place(s.MonitorX, s.MonitorY, s.MonitorScreen);
    }

    /// <summary>Genel ayar degisti (ve istenirse kategoriler tasindi): diger dolaplar yenilenir.</summary>
    public void RefreshOthers(MainWindow except, bool reload = false)
    {
        foreach (var w in _wins.Where(w => w != except)) { w.ReloadSettings(); if (reload) w.Reload(); }
    }

    public MainWindow AddCabinet()
    {
        var c = _store!.AddCabinet();
        var w = OpenCabinetWindow(c.Id);
        w.ShowCabinet();
        return w;
    }

    /// <summary>Dolabi kapatir; kategorileri ilk kalan dolaba tasinir (veri silinmez).</summary>
    public void RemoveCabinet(MainWindow w)
    {
        _store!.DeleteCabinet(w.CabinetId);
        _wins.Remove(w); w.Close();
        _wins.ForEach(x => { x.Reload(); x.ReloadSettings(); });
    }

    void Quit()
    {
        DesktopIcons.SetVisible(true); // uygulama kapaninca masaustu eski haline doner
        _tray!.Visible = false; _tray.Dispose();
        _store?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }
}
