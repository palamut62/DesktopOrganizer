using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dolap.Core;

namespace Dolap.App;

/// <summary>One searchable list for launch, drawer navigation and explicit multi-selection commands.</summary>
public sealed class QuickAccessWindow : Window
{
    readonly MainWindow _host;
    readonly CollectionStore _store;
    readonly Guid? _category;
    internal readonly TextBox Query = new() { Margin = new(0, 8, 0, 8), FontSize = 18 };
    internal readonly ListBox Results = new() { SelectionMode = SelectionMode.Extended, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new(0) };
    readonly TextBlock _message = new() { Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    readonly ComboBox _destination = new() { MinWidth = 180, MaxWidth = 300, DisplayMemberPath = "Label" };
    readonly TextBox _section = new() { Width = 125, ToolTip = "Bölüm adı (boş: bölümü kaldır)" };
    readonly Button _remove;
    bool _confirmRemove;
    sealed record Destination(Guid Id, string Label);

    public QuickAccessWindow(MainWindow host, Guid? category = null)
    {
        _host = host; _store = host.Store; _category = category;
        Title = category == null ? "Hızlı erişim - Tüm dolaplar" : "Çekmeceyi toplu düzenle";
        Width = 760; Height = 570; MinWidth = 640; MinHeight = 420;
        Background = (Brush)Application.Current.FindResource("CardBrush"); Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new(18) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        top.Children.Add(Query);
        top.Children.Add(new TextBlock { Text = "Yaz ve Enter ile aç · Ctrl / Shift ile çoklu seç · Esc ile kapat", Foreground = Brushes.LightGray });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel();
        var actions = new WrapPanel { Margin = new(0, 8, 0, 4) };
        actions.Children.Add(ActionButton("Aç", OpenSelected));
        actions.Children.Add(ActionButton("Çekmecede göster", RevealSelected));
        actions.Children.Add(ActionButton("Favori ekle / kaldır", FavoriteSelected));
        actions.Children.Add(ActionButton("Yer öner", () =>
        {
            if (Results.SelectedItems.Count != 1 || Results.SelectedItem is not SearchHit { Shortcut: { } item })
            { _message.Text = "Öneri için tek bir kısayol seçin."; return; }
            var suggested = _store.SuggestCategory(new(item.DisplayName, item.Kind, item.Target));
            if (suggested == null) { _message.Text = "Uygun öneri yok; hedef çekmeceyi kendiniz seçebilirsiniz."; return; }
            _destination.SelectedItem = _destination.Items.OfType<Destination>().FirstOrDefault(d => d.Id == suggested.Id);
            _message.Text = $"Önerilen: {suggested.Name}. Taşı düğmesine basarsanız uygulanır.";
        }));
        actions.Children.Add(ActionButton("Geri al (Ctrl+Z)", Undo));
        bottom.Children.Add(actions);
        var batch = new WrapPanel();
        var names = _store.GetCabinets().ToDictionary(c => c.Id, c => c.Name);
        _destination.ItemsSource = _store.GetCategories().Where(c => string.IsNullOrEmpty(c.LinkedFolder)).Select(c => new Destination(c.Id, names.GetValueOrDefault(c.CabinetId) + " / " + c.Name)).ToList();
        _destination.SelectedIndex = 0;
        AutomationProperties.SetName(_destination, "Hedef çekmece"); AutomationProperties.SetName(_section, "Bölüm adı");
        batch.Children.Add(_destination); batch.Children.Add(ActionButton("Taşı", () => Batch("move")));
        batch.Children.Add(_section); batch.Children.Add(ActionButton("Bölüm ata", () => Batch("section")));
        _remove = ActionButton("Kaldır", () =>
        {
            if (!_confirmRemove) { _confirmRemove = true; _remove!.Content = "Kaldırmayı onayla"; _message.Text = "Yalnızca seçili kısayollar kaldırılır. Dosyalar silinmez. İptal için seçimi değiştirin."; }
            else Batch("remove");
        });
        batch.Children.Add(_remove); bottom.Children.Add(batch); bottom.Children.Add(_message);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        Results.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="6,5">
            <TextBlock Text="{Binding Name}" FontSize="15" FontWeight="SemiBold"/>
            <TextBlock Text="{Binding Detail}" FontSize="12" Opacity="0.7"/></StackPanel></DataTemplate>
            """);
        root.Children.Add(Results); Content = root;
        AutomationProperties.SetName(Query, "Tüm dolaplarda ara"); AutomationProperties.SetName(Results, "Arama sonuçları ve çoklu seçim");
        Query.TextChanged += (_, _) => Refresh();
        Query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Down or Key.Up && Results.Items.Count > 0)
            { Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1); Results.ScrollIntoView(Results.SelectedItem); e.Handled = true; }
        };
        Results.SelectionChanged += (_, _) => { _confirmRemove = false; _remove.Content = "Kaldır"; _message.Text = $"{SelectedIds().Length} kısayol seçili"; };
        Results.MouseDoubleClick += (_, e) => { if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) != null) OpenSelected(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && !_section.IsKeyboardFocusWithin) { OpenSelected(); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
            { Undo(); e.Handled = true; }
        };
        Loaded += (_, _) => Query.Focus(); Refresh();
    }

    static Button ActionButton(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new(2), Padding = new(9, 5, 9, 5), Style = (Style)Application.Current.FindResource("ModalButtonStyle") };
        b.Click += (_, _) => action(); return b;
    }
    internal Guid[] SelectedIds() => Results.SelectedItems.OfType<SearchHit>().Where(h => h.Shortcut != null).Select(h => h.Shortcut!.Id).ToArray();
    internal void Refresh()
    {
        var all = QuickSearch.Find(_store, Query.Text, _category);
        Results.ItemsSource = all; if (all.Count > 0) Results.SelectedIndex = 0;
        _message.Text = all.Count == 0 ? "Sonuç bulunamadı." : $"{all.Count} sonuç · Ctrl / Shift ile toplu seçebilirsiniz.";
    }
    void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is DolapException or Microsoft.Data.Sqlite.SqliteException or System.IO.IOException) { _message.Text = ex.Message; }
    }
    void OpenSelected()
    {
        if (Results.SelectedItems.Count != 1 || Results.SelectedItem is not SearchHit hit) { _message.Text = "Açmak için tek bir sonuç seçin."; return; }
        if (hit.Shortcut == null) { RevealSelected(); return; }
        var result = ShellLauncher.Launch(hit.Shortcut);
        if (result.Status == LaunchStatus.Sent) Close(); else _message.Text = result.Message;
    }
    void RevealSelected()
    {
        if (Results.SelectedItem is not SearchHit hit) return;
        var window = (Application.Current as App)?.CabinetWindows.FirstOrDefault(w => w.CabinetId == hit.CabinetId) ?? _host;
        Close(); window.RevealCategory(hit.CategoryId);
    }
    void FavoriteSelected() => Run(() =>
    {
        var ids = SelectedIds(); if (ids.Length != 1) throw new DolapException("Favori için tek bir kısayol seçin.");
        _store.ToggleFavorite(ids[0]); (Application.Current as App)?.RefreshUsability(); _message.Text = "Favoriler güncellendi.";
    });
    internal void Batch(string action) => Run(() =>
    {
        _store.BatchShortcuts(SelectedIds(), action, (_destination.SelectedItem as Destination)?.Id, _section.Text);
        _host.RefreshCollection(); Refresh(); _message.Text = "İşlem tamamlandı. Geri al ile geri dönebilirsiniz.";
    });
    internal void Undo() => Run(() =>
    {
        var undone = _store.UndoShortcutEdit(); _host.RefreshCollection(); Refresh();
        _message.Text = undone ? "Son kısayol işlemi geri alındı." : "Bu oturumda geri alınacak işlem yok.";
    });
}
