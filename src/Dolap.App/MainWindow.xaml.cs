using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Dolap.Core;
using Forms = System.Windows.Forms;

namespace Dolap.App;

public partial class MainWindow : Window
{
    const int HotkeyId = 1;
    readonly CollectionStore _store;
    readonly MainVm _vm = new();
    readonly DispatcherTimer _toastTimer = new(), _dragOpenTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly Dictionary<Guid, DateTime> _lastLaunch = [];
    List<ShortcutVm> _all = [];
    CategoryVm? _dragTarget;
    Shortcut? _undo;
    IntPtr _hwnd;
    bool _dragging, _hiddenStrip;
    DateTime _insideSince = DateTime.UtcNow, _edgeSince = DateTime.MaxValue;

    public AppSettings Settings { get; private set; }
    public bool ModalOpen => ModalHost.Content != null;
    public CollectionStore Store => _store;

    public MainWindow(CollectionStore store)
    {
        _store = store;
        Settings = store.LoadSettings();
        InitializeComponent();
        DataContext = _vm;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _vm.ToastVisible = false; _undo = null; _vm.HasUndo = false; };
        _dragOpenTimer.Tick += (_, _) => { _dragOpenTimer.Stop(); if (_dragTarget != null) OpenOnly(_dragTarget); };
        _hideTimer.Tick += (_, _) => AutoHideTick();
        Deactivated += (_, _) => { if (!ModalOpen && !_dragging) CloseAll(); };
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) =>
        {
            if (!Settings.FirstRunDone) await FirstRun();
            // gelistirme yardimcisi: --open <sira> cekmeceyi acik baslatir (ekran goruntusu icin)
            var args = Environment.GetCommandLineArgs();
            var k = Array.IndexOf(args, "--open");
            if (k >= 0 && int.TryParse(args.ElementAtOrDefault(k + 1), out var n) && n < _vm.Categories.Count) _vm.Categories[n].IsOpen = true;
        };
        LoadAll();
    }

    void OnSourceInitialized(object? s, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        ApplySettings(Settings, save: false);
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && w.ToInt32() == HotkeyId) { ToggleVisible(); handled = true; }
        return IntPtr.Zero;
    }

    // ---------- veri ----------
    public void Reload() => LoadAll();

    void LoadAll()
    {
        var openId = _vm.Categories.FirstOrDefault(c => c.IsOpen)?.Model.Id;
        var cats = _store.GetCategories();
        var byCat = _store.GetShortcuts().GroupBy(s => s.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        _vm.Categories.Clear();
        _all = [];
        foreach (var c in cats)
        {
            var cv = new CategoryVm(c) { IsOpen = c.Id == openId };
            cv.Refresh();
            foreach (var s in byCat.GetValueOrDefault(c.Id) ?? [])
            {
                var sv = new ShortcutVm(s, c.Name);
                cv.Items.Add(sv); _all.Add(sv);
            }
            cv.Refresh();
            _vm.Categories.Add(cv);
        }
    }

    void Guard(Action a)
    {
        try { a(); }
        catch (DolapException e) { ShowToast(e.Message); }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        { ShowToast("Kaydedilemedi; değişiklik kalıcı değil. " + e.Message); }
    }

    public void ShowToast(string msg, bool undo = false)
    {
        _vm.Toast = msg; _vm.HasUndo = undo; _vm.ToastVisible = true;
        _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromSeconds(undo ? 10 : 4); _toastTimer.Start();
    }

    void Undo_Click(object s, RoutedEventArgs e)
    {
        if (_undo == null) return;
        Guard(() => { _store.Restore(_undo); LoadAll(); });
        _undo = null; _vm.ToastVisible = false; _vm.HasUndo = false;
    }

    // ---------- cekmece ----------
    static CategoryVm Cat(object s) => (CategoryVm)((FrameworkElement)s).DataContext;
    static ShortcutVm Sc(object s) => (ShortcutVm)((FrameworkElement)s).DataContext;

    void OpenOnly(CategoryVm c) { foreach (var o in _vm.Categories) o.IsOpen = o == c; }
    void CloseAll() { foreach (var o in _vm.Categories) o.IsOpen = false; }
    void Front_Click(object s, RoutedEventArgs e) { var c = Cat(s); if (c.IsOpen) c.IsOpen = false; else OpenOnly(c); }

    void Item_Click(object s, RoutedEventArgs e) => LaunchItem(Sc(s));

    public void LaunchItem(ShortcutVm sv)
    {
        if (_lastLaunch.TryGetValue(sv.Model.Id, out var t) && DateTime.UtcNow - t < TimeSpan.FromSeconds(1)) return;
        _lastLaunch[sv.Model.Id] = DateTime.UtcNow;
        var r = ShellLauncher.Launch(sv.Model);
        ShowToast(r.Message);
        if (r.Status == LaunchStatus.Sent && Settings.CloseAfterLaunch) CloseAll();
    }

    void Item_ContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        var b = (Button)s; var sv = Sc(b); var cm = b.ContextMenu!; cm.Items.Clear();
        cm.Items.Add(Mi("Aç", () => LaunchItem(sv)));
        cm.Items.Add(Mi("Düzenle", async () => await EditShortcut(sv)));
        var move = new MenuItem { Header = "Başka kategoriye taşı" };
        foreach (var c in _vm.Categories.Where(c => c.Model.Id != sv.Model.CategoryId))
        {
            var target = c;
            move.Items.Add(Mi(c.Name, () => Guard(() => { _store.MoveShortcut(sv.Model.Id, target.Model.Id); LoadAll(); })));
        }
        move.IsEnabled = move.Items.Count > 0;
        cm.Items.Add(move);
        cm.Items.Add(Mi("Sola taşı", () => MoveItem(sv, -1)));
        cm.Items.Add(Mi("Sağa taşı", () => MoveItem(sv, +1)));
        if (sv.Model.Kind != ShortcutKind.Url) cm.Items.Add(Mi("Dosya konumunu aç", () => ShellLauncher.RevealInExplorer(sv.Model)));
        cm.Items.Add(Mi("Kaldır", () => Guard(() =>
        {
            _undo = _store.RemoveShortcut(sv.Model.Id); LoadAll();
            ShowToast($"\"{sv.Name}\" çekmeceden kaldırıldı (dosya silinmedi).", undo: true);
        })));
    }

    static MenuItem Mi(string header, Action a) { var m = new MenuItem { Header = header }; m.Click += (_, _) => a(); return m; }

    void MoveItem(ShortcutVm sv, int d)
    {
        var cat = _vm.Categories.First(c => c.Model.Id == sv.Model.CategoryId);
        var ids = cat.Items.Select(i => i.Model.Id).ToList();
        var i = ids.IndexOf(sv.Model.Id); var j = i + d;
        if (j < 0 || j >= ids.Count) return;
        (ids[i], ids[j]) = (ids[j], ids[i]);
        Guard(() => { _store.ReorderShortcuts(ids); LoadAll(); });
    }

    void Front_ContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        var b = (Button)s; var c = Cat(b); var cm = b.ContextMenu!; cm.Items.Clear();
        cm.Items.Add(Mi("Çekmeceyi aç/kapat", () => Front_Click(b, null!)));
        cm.Items.Add(Mi("Düzenle", async () => await EditCategory(c)));
        cm.Items.Add(Mi("Yukarı taşı", () => MoveCategory(c, -1)));
        cm.Items.Add(Mi("Aşağı taşı", () => MoveCategory(c, +1)));
        cm.Items.Add(Mi("Sil", async () => await DeleteCategory(c)));
    }

    void MoveCategory(CategoryVm c, int d)
    {
        var ids = _vm.Categories.Select(x => x.Model.Id).ToList();
        var i = ids.IndexOf(c.Model.Id); var j = i + d;
        if (j < 0 || j >= ids.Count) return;
        (ids[i], ids[j]) = (ids[j], ids[i]);
        Guard(() => { _store.ReorderCategories(ids); LoadAll(); });
    }

    // ---------- surukle-birak (Gezgin -> cekmece) ----------
    void Drawer_DragEnter(object s, DragEventArgs e)
    {
        _dragging = true; _dragTarget = Cat(s);
        e.Effects = DragDropEffects.Link; e.Handled = true;
        if (!_dragTarget.IsOpen) _dragOpenTimer.Start(); // ~600 ms bekleyince acilir
    }
    void Drawer_DragLeave(object s, DragEventArgs e) { _dragOpenTimer.Stop(); _dragging = false; }

    void Drawer_Drop(object s, DragEventArgs e)
    {
        _dragOpenTimer.Stop(); _dragging = false;
        var cat = Cat(s);
        var inputs = new List<string>();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) inputs.AddRange(files);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string txt && TargetClassifier.IsHttpUrl(txt)) inputs.Add(txt.Trim());
        if (inputs.Count > 0) AddMany(cat, inputs);
        e.Handled = true;
    }

    /// <summary>Kaynaklara dokunmaz; basarili/atlanan/basarisiz sayilarini ayri bildirir.</summary>
    void AddMany(CategoryVm cat, IEnumerable<string> inputs)
    {
        int ok = 0, dup = 0, fail = 0;
        foreach (var i in inputs)
        {
            try
            {
                if (_store.AddShortcut(cat.Model.Id, ShortcutFactory.FromPath(i)) == null) dup++; else ok++;
            }
            catch (DolapException) { fail++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { fail++; }
        }
        LoadAll();
        ShowToast($"{ok} eklendi, {dup} zaten vardı (atlandı), {fail} eklenemedi.");
    }

    // ---------- konum / ayarlar ----------
    public void ApplySettings(AppSettings s, bool save = true)
    {
        var prev = Settings;
        Settings = s;
        if (save) Guard(() => _store.SaveSettings(s));
        App.ApplyTheme(s.ThemeId, s.ReduceMotion ?? !SystemParameters.ClientAreaAnimation, s.QualityProfile == "low");
        ScaleTf.ScaleX = ScaleTf.ScaleY = Math.Clamp(s.Scale, 0.75, 1.5);
        _vm.Side = s.Edge == DockEdge.Left ? -1 : 1;
        Topmost = s.AlwaysOnTop;
        if (prev.StartWithWindows != s.StartWithWindows || !save) Guard(() => StartupService.Set(s.StartWithWindows));
        RegisterHotkey();
        _hideTimer.IsEnabled = s.AutoHide && s.Edge != DockEdge.Free;
        _hiddenStrip = false;
        PlaceWindow();
    }

    void RegisterHotkey()
    {
        Native.UnregisterHotKey(_hwnd, HotkeyId);
        if (!Hotkeys.TryParse(Settings.Hotkey, out var mods, out var vk))
        { ShowToast("Kısayol tuşu anlaşılamadı: " + Settings.Hotkey); return; }
        if (!Native.RegisterHotKey(_hwnd, HotkeyId, mods | 0x4000, vk))
            ShowToast($"{Settings.Hotkey} başka bir uygulamada kullanılıyor. Ayarlar > Davranış'tan farklı bir kısayol seçin.");
    }

    (Forms.Screen Screen, double Dpi) CurrentScreen()
    {
        var sc = Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == Settings.MonitorDevice) ?? Forms.Screen.PrimaryScreen!;
        var pt = new Native.POINT { X = sc.Bounds.Left + sc.Bounds.Width / 2, Y = sc.Bounds.Top + sc.Bounds.Height / 2 };
        Native.GetDpiForMonitor(Native.MonitorFromPoint(pt, 2), 0, out var dx, out _);
        return (sc, dx == 0 ? 1 : dx / 96.0);
    }

    void PlaceWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        var (sc, dpi) = CurrentScreen();
        var wa = sc.WorkingArea;
        var scale = Math.Clamp(Settings.Scale, 0.75, 1.5);
        int w = (int)Math.Round(420 * scale * dpi), h = (int)Math.Round(wa.Height * 0.88);
        int x = Settings.Edge switch
        {
            DockEdge.Left => wa.Left,
            DockEdge.Right => wa.Right - w,
            _ => wa.Left + (int)(Math.Clamp(Settings.FreeX, 0, 1) * Math.Max(0, wa.Width - w)),
        };
        int y = wa.Top + (Settings.Edge == DockEdge.Free
            ? (int)(Math.Clamp(Settings.FreeY, 0, 1) * Math.Max(0, wa.Height - h)) : (int)(wa.Height * 0.03));
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
        // Calisma alaninin %80'i asilinca icerik kaydirilir.
        Scroller.MaxHeight = Math.Max(120, Math.Min(h, wa.Height * 0.8) / dpi / scale - 50);
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    protected override void OnDpiChanged(DpiScale o, DpiScale n) { base.OnDpiChanged(o, n); Dispatcher.BeginInvoke(PlaceWindow); }

    void Header_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1) return;
        _dragging = true;
        Native.GetWindowRect(_hwnd, out var r0);
        try { DragMove(); } catch (InvalidOperationException) { }
        _dragging = false;
        Native.GetWindowRect(_hwnd, out var r);
        if (r.Left == r0.Left && r.Top == r0.Top) return; // tiklama surukleme degildir
        var sc = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
        var wa = sc.WorkingArea; int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var edge = r.Left - wa.Left < 24 ? DockEdge.Left : wa.Right - r.Right < 24 ? DockEdge.Right : DockEdge.Free;
        var fx = wa.Width - w <= 0 ? 0 : (double)(r.Left - wa.Left) / (wa.Width - w);
        var fy = wa.Height - h <= 0 ? 0 : (double)(r.Top - wa.Top) / (wa.Height - h);
        ApplySettings(Settings with { Edge = edge, FreeX = Math.Clamp(fx, 0, 1), FreeY = Math.Clamp(fy, 0, 1), MonitorDevice = sc.DeviceName });
    }

    // ---------- gorunurluk (tepsi / kisayol) ----------
    public void ToggleVisible() { if (IsVisible && !_hiddenStrip) Hide(); else ShowCabinet(); }

    public void ShowCabinet()
    {
        _hiddenStrip = false;
        if (!IsVisible) Show();
        PlaceWindow();
        Topmost = true; Topmost = Settings.AlwaysOnTop; // one cikar; surekli ustte kalmaz
        Activate();
    }

    public void ResetPosition() => ApplySettings(Settings with { Edge = DockEdge.Right, MonitorDevice = null, FreeX = 0.8, FreeY = 0.1 });

    void AutoHideTick()
    {
        if (!IsVisible || !Settings.AutoHide || Settings.Edge == DockEdge.Free) return;
        Native.GetCursorPos(out var p); Native.GetWindowRect(_hwnd, out var r);
        var (sc, dpi) = CurrentScreen(); var wa = sc.WorkingArea; int strip = (int)(10 * dpi);
        var left = Settings.Edge == DockEdge.Left;
        if (_hiddenStrip)
        {
            var atEdge = (left ? p.X <= wa.Left + strip : p.X >= wa.Right - strip) && p.Y >= r.Top && p.Y <= r.Bottom;
            if (!atEdge) { _edgeSince = DateTime.MaxValue; return; }
            if (_edgeSince == DateTime.MaxValue) _edgeSince = DateTime.UtcNow;
            if (DateTime.UtcNow - _edgeSince > TimeSpan.FromMilliseconds(250)) { _hiddenStrip = false; PlaceWindow(); _insideSince = DateTime.UtcNow; }
            return;
        }
        var inside = p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;
        if (inside || ModalOpen || _dragging || IsKeyboardFocusWithin) { _insideSince = DateTime.UtcNow; return; }
        if (DateTime.UtcNow - _insideSince < TimeSpan.FromMilliseconds(800)) return;
        _hiddenStrip = true; CloseAll();
        var w = r.Right - r.Left;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, left ? wa.Left - w + strip : wa.Right - strip, r.Top, w, r.Bottom - r.Top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    // ---------- klavye ----------
    void Window_PreviewKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ShowSearch(); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            if (ModalOpen) CancelModal();
            else if (_vm.Categories.Any(c => c.IsOpen)) CloseAll();
            else Hide();
            e.Handled = true;
        }
    }

    void Search_Click(object s, RoutedEventArgs e) => ShowSearch();
    void Settings_Click(object s, RoutedEventArgs e) => OpenSettings();
    async void AddCategory_Click(object s, RoutedEventArgs e) => await EditCategory(null);
    async void AddShortcut_Click(object s, RoutedEventArgs e) => await EditShortcut(null, Cat(s));

    SettingsWindow? _settingsWin;
    public void OpenSettings()
    {
        if (_settingsWin is { IsLoaded: true }) { _settingsWin.Activate(); return; }
        _settingsWin = new SettingsWindow(this);
        _settingsWin.Show();
    }
}
