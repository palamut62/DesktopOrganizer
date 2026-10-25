using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolap.Core;

namespace Dolap.App;

/// <summary>Gecici birakma tepsisi: dosya, baglanti ve ekran goruntusunu simdilik birak, sonra cekmecelere dagit.
/// Ogeler gizli bir "shelf:" kategorisinde tutulur (dolaplarda gorunmez). Dosyalara dokunulmaz; yapistirilan
/// goruntu %LOCALAPPDATA%/MasaustuDolabi/tepsi altina kaydedilir.</summary>
public sealed class ShelfWindow : Window
{
    public const string Marker = "shelf:";
    readonly CollectionStore _store;
    readonly WrapPanel _items = new() { Margin = new(6, 2, 6, 6) };
    readonly TextBlock _empty = new() { Text = "Dosya, bağlantı ya da ekran görüntüsü bırak (Ctrl+V)", Foreground = Brushes.LightGray, FontSize = 12, Margin = new(12, 8, 12, 8), TextWrapping = TextWrapping.Wrap };
    public event Action? Changed;

    public ShelfWindow(CollectionStore store)
    {
        _store = store;
        Title = "Masaüstü Dolabı - Bırakma tepsisi";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Width = 380; SizeToContent = SizeToContent.Height; AllowDrop = true;
        AutomationProperties.SetName(this, "Bırakma tepsisi");
        var title = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new(10, 6, 6, 0) };
        var close = new Button { Content = "", Style = (Style)Application.Current.FindResource("HeaderButtonStyle"), ToolTip = "Tepsiyi gizle" };
        AutomationProperties.SetName(close, "Tepsiyi gizle");
        close.Click += (_, _) => (Application.Current as App)?.HideShelf();
        var paste = new Button { Content = "", Style = (Style)Application.Current.FindResource("HeaderButtonStyle"), ToolTip = "Panodan yapıştır (Ctrl+V)" };
        AutomationProperties.SetName(paste, "Panodan yapıştır");
        paste.Click += (_, _) => Paste();
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(paste, Dock.Right);
        title.Children.Add(close); title.Children.Add(paste);
        title.Children.Add(new TextBlock { Text = "Bırakma tepsisi", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) try { DragMove(); } catch (InvalidOperationException) { } };
        var body = new StackPanel(); body.Children.Add(title); body.Children.Add(_empty);
        body.Children.Add(new ScrollViewer { Content = _items, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = new Border
        {
            CornerRadius = new(12), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 200, 215, 235)),
            Background = new SolidColorBrush(Color.FromArgb(225, 22, 26, 33)), Child = body, Margin = new(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .5 },
        };
        DragOver += (_, e) => { e.Effects = DragDropEffects.Link; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddInputs(files);
            else if (e.Data.GetData(DataFormats.UnicodeText) is string t && TargetClassifier.IsHttpUrl(t)) AddInputs([t.Trim()]);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { Paste(); e.Handled = true; } };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        };
        // shortcut: konum kaydedilmez, her acilista calisma alaninin alt ortasi; gerekirse ayarlara eklenir.
        Loaded += (_, _) => { var wa = SystemParameters.WorkArea; Left = wa.Left + (wa.Width - ActualWidth) / 2; Top = wa.Bottom - ActualHeight - 12; };
        Refresh();
    }

    public static Guid CategoryId(CollectionStore store)
    {
        var c = store.GetCategories().FirstOrDefault(c => c.LinkedFolder == Marker);
        if (c != null) return c.Id;
        var created = store.AddCategory("Tepsi", "download", "#6B7480");
        store.SetLinkedFolder(created.Id, Marker);
        return created.Id;
    }

    void AddInputs(IEnumerable<string> inputs)
    {
        var cat = CategoryId(_store);
        foreach (var i in inputs)
            try { _store.AddShortcut(cat, ShortcutFactory.FromPath(i)); }
            catch (Exception e) when (e is DolapException or IOException or UnauthorizedAccessException) { }
        Refresh(); Changed?.Invoke();
    }

    void Paste()
    {
        if (Clipboard.ContainsFileDropList()) { AddInputs(Clipboard.GetFileDropList().Cast<string>()); return; }
        if (Clipboard.ContainsText() && TargetClassifier.IsHttpUrl(Clipboard.GetText())) { AddInputs([Clipboard.GetText().Trim()]); return; }
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
        {
            var dir = Path.Combine(Paths.Data, "tepsi"); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"ekran-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img));
            using (var f = File.Create(file)) enc.Save(f);
            AddInputs([file]);
        }
    }

    public void Refresh()
    {
        _items.Children.Clear();
        var cat = _store.GetCategories().FirstOrDefault(c => c.LinkedFolder == Marker);
        var list = cat == null ? [] : _store.GetShortcuts(cat.Id);
        _empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var s in list)
        {
            var vm = new ShortcutVm(s, "Tepsi");
            var b = new Button { DataContext = vm, Style = (Style)Application.Current.FindResource("ItemButtonStyle"), ToolTip = vm.Subtitle, ContextMenu = new ContextMenu() };
            AutomationProperties.SetName(b, vm.Name + ", tepside");
            b.Click += (_, _) => (Application.Current as App)?.FirstWindow?.ShowToast(ShellLauncher.Launch(s).Message);
            b.ContextMenuOpening += (_, _) => BuildMenu(b.ContextMenu, s);
            _items.Children.Add(b);
        }
    }

    void BuildMenu(ContextMenu cm, Shortcut s)
    {
        cm.Items.Clear();
        var open = new MenuItem { Header = "Aç" }; open.Click += (_, _) => ShellLauncher.Launch(s); cm.Items.Add(open);
        var move = new MenuItem { Header = "Çekmeceye taşı" };
        var cabs = _store.GetCabinets().ToDictionary(c => c.Id, c => c.Name);
        foreach (var c in _store.GetCategories().Where(c => c.LinkedFolder == null))
        {
            var target = c;
            var mi = new MenuItem { Header = $"{cabs.GetValueOrDefault(c.CabinetId)} / {c.Name}" };
            mi.Click += (_, _) => { _store.MoveShortcut(s.Id, target.Id); Refresh(); Changed?.Invoke(); };
            move.Items.Add(mi);
        }
        cm.Items.Add(move);
        var remove = new MenuItem { Header = "Tepsiden kaldır (dosya silinmez)" };
        remove.Click += (_, _) => { _store.RemoveShortcut(s.Id); Refresh(); };
        cm.Items.Add(remove);
    }
}
