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
    readonly MainWindow _main;
    bool _loading = true;

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        var s = main.Settings;
        (s.ThemeId == "blocks" ? ThemeBlocks : ThemeMetal).IsChecked = true;
        ScaleSlider.Value = s.Scale;
        ReduceMotion.IsChecked = s.ReduceMotion ?? !SystemParameters.ClientAreaAnimation;
        LowQuality.IsChecked = s.QualityProfile == "low";
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

    void Apply(Func<AppSettings, AppSettings> f) { if (!_loading) _main.ApplySettings(f(_main.Settings)); }

    void Theme_Checked(object s, RoutedEventArgs e) { var id = (string)((FrameworkElement)s).Tag; Apply(x => x with { ThemeId = id }); }
    void Scale_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = Math.Round(e.NewValue * 20) / 20;
        if (ScaleText != null) ScaleText.Text = $"%{v * 100:0}";
        if (!ScaleSlider.IsMouseCaptureWithin && !ScaleSlider.IsKeyboardFocusWithin) return; // yalniz kullanici degisimi
        Apply(x => x with { Scale = v });
    }
    void Behavior_Click(object s, RoutedEventArgs e) => Apply(x => x with
    {
        ReduceMotion = ReduceMotion.IsChecked, QualityProfile = LowQuality.IsChecked == true ? "low" : "standard",
        AutoHide = AutoHide.IsChecked == true, AlwaysOnTop = AlwaysOnTop.IsChecked == true,
        CloseAfterLaunch = CloseAfter.IsChecked == true, StartWithWindows = StartWin.IsChecked == true,
    });
    void Monitor_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e) =>
        Apply(x => x with { MonitorDevice = Monitors.SelectedIndex <= 0 ? null : (string)Monitors.SelectedItem });
    void EdgeLeft_Click(object s, RoutedEventArgs e) => Apply(x => x with { Edge = DockEdge.Left });
    void EdgeRight_Click(object s, RoutedEventArgs e) => Apply(x => x with { Edge = DockEdge.Right });
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
            if (await _main.Choose("Yedekten geri yükle", $"Yedek {c} kategori ve {n} kısayol içeriyor. Mevcut koleksiyonun yerine geçecek (önce otomatik yedeklenir).", "İptal", "Geri yükle") != "Geri yükle") return;
            _main.Store.ImportJson(d.FileName, Paths.Backups);
            _main.ApplySettings(_main.Store.LoadSettings(), save: false);
            _main.Reload();
            DataMsg.Text = "Geri yüklendi.";
        }
        catch (Exception ex) when (ex is DolapException or IOException) { DataMsg.Text = ex.Message; }
    }

    void Link_Navigate(object s, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }
}
