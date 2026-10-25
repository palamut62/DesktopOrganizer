using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Dolap.Core;
using Forms = System.Windows.Forms;

namespace Dolap.App;

public partial class SettingsWindow : Window
{
    MainWindow _main;
    bool _loading = true;
    static App? Host => Application.Current as App;

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        Cabs.ItemsSource = Host?.CabinetWindows.Select(w => w.CabinetName).ToList() ?? [main.CabinetName];
        Cabs.SelectedIndex = Math.Max(0, Host?.CabinetWindows.ToList().IndexOf(main) ?? 0);
        LoadFrom();
        _loading = false;
    }

    /// <summary>Secili dolabin ayarlarini kontrollere yukler.</summary>
    void LoadFrom()
    {
        _loading = true;
        var s = _main.Settings;
        CabName.Text = _main.CabinetName;
        var d = s.Design;
        VisibleBox.IsChecked = d.Visible;
        BodyColorBox.ItemsSource = BodyColors.Select(c => c.Name).ToList();
        BodyColorBox.SelectedIndex = Math.Max(0, Array.FindIndex(BodyColors, c => c.Hex == d.BodyColor));
        SelectTag(HandleBox, d.Handle); SelectTag(LabelBox, d.Label); SelectTag(ModuleBox, d.Module);
        VolumeSlider.Value = s.SoundVolume; MotionSlider.Value = s.MotionScale; ShelfBox.IsChecked = s.ShowShelf;
        RefreshLayouts();
        OpenDir.SelectedIndex = (int)s.OpenDirection;
        (s.ThemeId == "blocks" ? ThemeBlocks : ThemeMetal).IsChecked = true;
        ScaleSlider.Value = s.Scale;
        ReduceMotion.IsChecked = s.ReduceMotion ?? !SystemParameters.ClientAreaAnimation;
        LowQuality.IsChecked = s.QualityProfile == "low";
        ThemedWp.IsChecked = s.ThemedWallpaper;
        SoundsBox.IsChecked = s.Sounds;
        LockLayout.IsChecked = s.LayoutLocked; SnapLayout.IsChecked = s.MagneticSnap;
        HideIcons.IsChecked = s.HideDesktopIcons;
        MonitorBox.IsChecked = s.ShowSystemMonitor;
        MonitorSize.Value = s.MonitorScale; MonitorSizeText.Text = $"%{s.MonitorScale * 100:0}";
        MonitorTheme.SelectedIndex = Math.Max(0, new[] { "dark", "wood", "silver", "neon" }.ToList().IndexOf(s.MonitorTheme));
        DepthBox.IsChecked = s.DepthScale;
        AutoHide.IsChecked = s.AutoHide; AlwaysOnTop.IsChecked = s.AlwaysOnTop;
        CloseAfter.IsChecked = s.CloseAfterLaunch; StartWin.IsChecked = s.StartWithWindows;
        HotkeyBox.Text = s.Hotkey;
        var names = new[] { "Birincil monitör" }.Concat(Forms.Screen.AllScreens.Select(x => x.DeviceName)).ToList();
        Monitors.ItemsSource = names;
        Monitors.SelectedItem = s.MonitorDevice != null && names.Contains(s.MonitorDevice) ? s.MonitorDevice : names[0];
        VersionText.Text = "Sürüm " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
        var last = Directory.Exists(Paths.Backups) ? Directory.GetFiles(Paths.Backups, "dolap-*.db").OrderDescending().FirstOrDefault() : null;
        BackupInfo.Text = last == null ? "Henüz günlük yedek yok." : "Son günlük yedek: " + File.GetLastWriteTime(last).ToString("dd.MM.yyyy HH:mm") + "  (" + Paths.Backups + ")";
        _loading = false;
    }

    void Cab_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || Host == null || Cabs.SelectedIndex < 0 || Cabs.SelectedIndex >= Host!.CabinetWindows.Count) return;
        _main = Host!.CabinetWindows[Cabs.SelectedIndex];
        LoadFrom();
    }

    void RefreshCabList(int select)
    {
        _loading = true;
        Cabs.ItemsSource = Host!.CabinetWindows.Select(w => w.CabinetName).ToList();
        Cabs.SelectedIndex = select;
        _main = Host!.CabinetWindows[select];
        _loading = false;
        LoadFrom();
    }

    void AddCabinet_Click(object s, RoutedEventArgs e)
    {
        try { var w = Host!.AddCabinet(); RefreshCabList(Host!.CabinetWindows.ToList().IndexOf(w)); DataMsg.Text = ""; }
        catch (DolapException ex) { _main.ShowToast(ex.Message); }
    }

    async void DeleteCabinet_Click(object s, RoutedEventArgs e)
    {
        if (Host!.CabinetWindows.Count < 2) { _main.ShowToast("Son dolap silinemez."); return; }
        var target = _main;
        target.ShowCabinet();
        if (await target.Choose("Dolabı sil", $"\"{target.CabinetName}\" dolabı kaldırılsın mı? Kategorileri ve kısayolları diğer dolaba taşınır; hiçbir dosya silinmez.", "İptal", "Dolabı sil") != "Dolabı sil") return;
        try { Host.RemoveCabinet(target); RefreshCabList(0); }
        catch (DolapException ex) { Host!.CabinetWindows[0].ShowToast(ex.Message); }
    }

    void CabName_Commit(object s, RoutedEventArgs e)
    {
        if (_loading || string.IsNullOrWhiteSpace(CabName.Text) || CabName.Text.Trim() == _main.CabinetName) return;
        _main.Rename(CabName.Text);
        _loading = true;
        var i = Cabs.SelectedIndex;
        Cabs.ItemsSource = Host!.CabinetWindows.Select(w => w.CabinetName).ToList();
        Cabs.SelectedIndex = i;
        _loading = false;
    }
    void CabName_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) CabName_Commit(s, e); }

    void OpenDir_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e) =>
        Apply(x => x with { OpenDirection = (OpenDirection)Math.Max(0, OpenDir.SelectedIndex) });

    // Monitor ayarlari geneldir: her zaman ilk dolap uzerinden kaydedilir.
    void ApplyGlobal(Func<AppSettings, AppSettings> f)
    {
        if (_loading || Host?.CabinetWindows.FirstOrDefault() is not { } first) return;
        first.ApplySettings(f(first.Settings));
    }
    static string SelectedTag(System.Windows.Controls.ComboBox c) => (string)((System.Windows.Controls.ComboBoxItem)c.SelectedItem).Tag;

    void MonitorSize_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = Math.Round(e.NewValue * 20) / 20;
        if (MonitorSizeText != null) MonitorSizeText.Text = $"%{v * 100:0}";
        if (!MonitorSize.IsMouseCaptureWithin && !MonitorSize.IsKeyboardFocusWithin) return;
        ApplyGlobal(x => x with { MonitorScale = v });
    }
    void MonitorTheme_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e) =>
        ApplyGlobal(x => x with { MonitorTheme = (string)((System.Windows.Controls.ComboBoxItem)MonitorTheme.SelectedItem).Tag });

    void Apply(Func<AppSettings, AppSettings> f) { if (!_loading) _main.ApplySettings(f(_main.Settings)); }

    void Theme_Checked(object s, RoutedEventArgs e) { var id = (string)((FrameworkElement)s).Tag; Apply(x => x with { ThemeId = id }); }
    void Scale_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = Math.Round(e.NewValue * 20) / 20;
        if (ScaleText != null) ScaleText.Text = $"%{v * 100:0}";
        if (!ScaleSlider.IsMouseCaptureWithin && !ScaleSlider.IsKeyboardFocusWithin) return; // yalniz kullanici degisimi
        Apply(x => x with { Scale = v });
    }
    static readonly (string Name, string? Hex)[] BodyColors =
    [
        ("Temaya göre", null), ("Grafit", "#3C4148"), ("Gümüş", "#9AA3AD"), ("Lacivert", "#1A2F6B"), ("Kırmızı", "#A3262A"),
        ("Orman yeşili", "#2F5D3A"), ("Kum", "#B9A27A"), ("Beyaz", "#E6E6E2"), ("Siyah", "#16181C"),
    ];

    static void SelectTag(System.Windows.Controls.ComboBox c, string tag) =>
        c.SelectedItem = c.Items.Cast<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? c.Items[0];

    void Design_Changed(object s, RoutedEventArgs e) => Apply(x => x with
    {
        Design = new CabinetDesign(BodyColors[Math.Max(0, BodyColorBox.SelectedIndex)].Hex, SelectedTag(HandleBox), SelectedTag(LabelBox), SelectedTag(ModuleBox), VisibleBox.IsChecked == true),
    });

    void Volume_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!VolumeSlider.IsMouseCaptureWithin && !VolumeSlider.IsKeyboardFocusWithin) return;
        Apply(x => x with { SoundVolume = (int)Math.Round(e.NewValue) });
    }

    void Motion_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!MotionSlider.IsMouseCaptureWithin && !MotionSlider.IsKeyboardFocusWithin) return;
        Apply(x => x with { MotionScale = Math.Round(e.NewValue, 2) });
    }

    // ---- calisma setleri ve zaman makinesi ----
    List<LayoutSnapshot> _sets = [], _history = [];
    void RefreshLayouts()
    {
        _sets = _main.Store.GetSnapshots(true); _history = _main.Store.GetSnapshots(false);
        SetsList.ItemsSource = _sets.Select(x => x.Name).ToList();
        HistoryList.ItemsSource = _history.Select(x => x.CreatedAt.ToString("dd.MM HH:mm") + $"  ({x.Cabinets.Count} dolap)").ToList();
    }
    void SaveSet_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SetName.Text)) { LayoutMsg.Text = "Set için bir ad yazın (ör. İş, Yazılım, Oyun)."; return; }
        _main.Store.SaveSnapshot(SetName.Text.Trim()); LayoutMsg.Text = $"\"{SetName.Text.Trim()}\" kaydedildi."; SetName.Clear(); RefreshLayouts();
    }
    void ApplySet_Click(object s, RoutedEventArgs e)
    {
        if (SetsList.SelectedIndex < 0) { LayoutMsg.Text = "Önce bir set seçin."; return; }
        var n = Host!.ApplySnapshot(_sets[SetsList.SelectedIndex].Id); LayoutMsg.Text = $"Set uygulandı ({n} dolap)."; LoadFrom();
    }
    void DeleteSet_Click(object s, RoutedEventArgs e)
    {
        if (SetsList.SelectedIndex < 0) return;
        _main.Store.DeleteSnapshot(_sets[SetsList.SelectedIndex].Id); RefreshLayouts();
    }
    void RestoreHistory_Click(object s, RoutedEventArgs e)
    {
        if (HistoryList.SelectedIndex < 0) { LayoutMsg.Text = "Önce geçmişten bir an seçin."; return; }
        // Geri donusten once mevcut hal de gecmise yazilir: geri donus geri alinabilir.
        _main.Store.SaveSnapshot(null);
        var n = Host!.ApplySnapshot(_history[HistoryList.SelectedIndex].Id); LayoutMsg.Text = $"Düzen o ana döndü ({n} dolap). Önceki hâl geçmişe kaydedildi."; LoadFrom();
    }
    async void Broken_Click(object s, RoutedEventArgs e) => await _main.RepairBroken();

    void Behavior_Click(object s, RoutedEventArgs e) => Apply(x => x with
    {
        ShowShelf = ShelfBox.IsChecked == true,
        LayoutLocked = LockLayout.IsChecked == true, MagneticSnap = SnapLayout.IsChecked == true,
        ReduceMotion = ReduceMotion.IsChecked, ThemedWallpaper = ThemedWp.IsChecked == true, Sounds = SoundsBox.IsChecked == true, HideDesktopIcons = HideIcons.IsChecked == true, ShowSystemMonitor = MonitorBox.IsChecked == true, DepthScale = DepthBox.IsChecked == true, QualityProfile = LowQuality.IsChecked == true ? "low" : "standard",
        AutoHide = AutoHide.IsChecked == true, AlwaysOnTop = AlwaysOnTop.IsChecked == true,
        CloseAfterLaunch = CloseAfter.IsChecked == true, StartWithWindows = StartWin.IsChecked == true,
    });
    void Monitor_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e) =>
        Apply(x => x with { MonitorDevice = Monitors.SelectedIndex <= 0 ? null : (string)Monitors.SelectedItem });
    void EdgeLeft_Click(object s, RoutedEventArgs e) => Apply(x => x with { Edge = DockEdge.Left });
    void EdgeRight_Click(object s, RoutedEventArgs e) => Apply(x => x with { Edge = DockEdge.Right });
    void ResetView_Click(object s, RoutedEventArgs e) => _main.ResetView();
    void Reset_Click(object s, RoutedEventArgs e) { _main.ResetPosition(); Monitors.SelectedIndex = 0; }

    void Hotkey_KeyDown(object s, KeyEventArgs e)
    {
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var m = Keyboard.Modifiers;
        if (m == ModifierKeys.None) { DataMsg.Text = "Kısayol en az bir değiştirici tuş (Ctrl, Alt, Shift, Win) içermeli."; return; }
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(k.ToString());
        var combo = string.Join("+", parts);
        HotkeyBox.Text = combo; DataMsg.Text = "";
        Apply(x => x with { Hotkey = combo }); // cakisma varsa ana pencere uyari verir
    }

    async void Import_Click(object s, RoutedEventArgs e) { _main.ShowCabinet(); await _main.ShowImport(); }

    void Export_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = "Dolap yedeği (*.json)|*.json", FileName = $"dolap-{DateTime.Now:yyyyMMdd}.json" };
        if (d.ShowDialog() != true) return;
        try { _main.Store.ExportJson(d.FileName); DataMsg.Text = "Dışa aktarıldı: " + d.FileName; }
        catch (IOException ex) { DataMsg.Text = "Yazılamadı: " + ex.Message; }
    }

    async void Restore_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Dolap yedeği (*.json)|*.json" };
        if (d.ShowDialog() != true) return;
        try
        {
            var (c, n) = CollectionStore.PreviewJson(d.FileName);
            _main.ShowCabinet();
            if (await _main.Choose("Yedekten geri yükle", $"Yedek {c} kategori ve {n} kısayol içeriyor. Mevcut koleksiyonun yerine geçecek (önce otomatik yedeklenir). Çalışma setleri yedekten alınır; otomatik yerleşim geçmişi sıfırlanır.", "İptal", "Geri yükle") != "Geri yükle") return;
            _main = Host!.ImportCollection(d.FileName, Paths.Backups);
            _main.OpenSettings(); // New selector binds to the imported cabinet IDs.
            _main.ShowToast("Koleksiyon ve çalışma setleri geri yüklendi.");
            // Baska bilgisayardan gelen yedek: eksik yollari eslemeyi oner.
            if (_main.Store.FindBroken().Count > 0) await _main.RepairBroken();
        }
        catch (Exception ex) when (ex is DolapException or IOException) { DataMsg.Text = ex.Message; }
    }

    void Link_Navigate(object s, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }
}
