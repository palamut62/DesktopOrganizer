using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dolap.Core;

namespace Dolap.App;

public sealed record Field(string Label, string Initial, string[]? Choices = null, bool Browse = false);

public partial class MainWindow
{
    TaskCompletionSource<object?>? _modalTcs;
    bool _searchOpen;

    Border Card(UIElement content, string? title = null)
    {
        var sp = new StackPanel();
        if (title != null) sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), Margin = new(0, 0, 0, 4) });
        sp.Children.Add(content);
        return new Border
        {
            Background = (Brush)FindResource("CardBrush"), BorderBrush = (Brush)FindResource("CardBorderBrush"), BorderThickness = new(1),
            CornerRadius = new(8), Padding = new(14), Child = sp, MaxWidth = 380,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, Opacity = 0.5, ShadowDepth = 2 },
        };
    }

    Task<object?> RunModal(UIElement card, IInputElement? focus = null)
    {
        _modalTcs?.TrySetResult(null);
        _modalTcs = new();
        ModalHost.Content = card;
        Activate();
        if (focus != null) Dispatcher.BeginInvoke(() => { Keyboard.Focus(focus); if (focus is TextBox t) t.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        return _modalTcs.Task;
    }

    void CloseModal(object? result)
    {
        ModalHost.Content = null;
        _searchOpen = false;
        var t = _modalTcs; _modalTcs = null;
        t?.TrySetResult(result);
    }
    void CancelModal() => CloseModal(null);

    static Button Btn(string text, Action a, bool isDefault = false)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("ModalButtonStyle"), IsDefault = isDefault };
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Uygulama ici form (alert/prompt yerine). Iptal = null.</summary>
    public async Task<string[]?> Form(string title, string okText, params Field[] fields)
    {
        var sp = new StackPanel();
        var getters = new List<Func<string>>();
        IInputElement? first = null;
        foreach (var f in fields)
        {
            sp.Children.Add(new TextBlock { Text = f.Label, Style = (Style)FindResource("LabelStyle") });
            if (f.Choices != null)
            {
                var cb = new ComboBox { ItemsSource = f.Choices, SelectedItem = f.Choices.Contains(f.Initial) ? f.Initial : f.Choices[0] };
                AutomationProperties.SetName(cb, f.Label);
                sp.Children.Add(cb); getters.Add(() => (string)cb.SelectedItem);
                first ??= cb;
            }
            else
            {
                var tb = new TextBox { Text = f.Initial };
                AutomationProperties.SetName(tb, f.Label);
                if (f.Browse)
                {
                    var dp = new DockPanel();
                    var br = Btn("Gözat", () =>
                    {
                        var d = new Microsoft.Win32.OpenFileDialog { Title = "Hedef seçin" };
                        if (d.ShowDialog() == true) tb.Text = d.FileName;
                    });
                    br.Margin = new(4, 0, 0, 0);
                    DockPanel.SetDock(br, Dock.Right);
                    dp.Children.Add(br); dp.Children.Add(tb); sp.Children.Add(dp);
                }
                else sp.Children.Add(tb);
                getters.Add(() => tb.Text);
                first ??= tb;
            }
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        buttons.Children.Add(Btn("İptal", CancelModal));
        buttons.Children.Add(Btn(okText, () => CloseModal(getters.Select(g => g()).ToArray()), isDefault: true));
        sp.Children.Add(buttons);
        return await RunModal(Card(sp, title), first) as string[];
    }

    /// <summary>Dugme adlarindan secileni doner (iptal/Esc = null).</summary>
    public async Task<string?> Choose(string title, string message, params string[] buttons)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextBrush"), Margin = new(0, 6, 0, 0) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        foreach (var b in buttons) { var name = b; row.Children.Add(Btn(name, () => CloseModal(name))); }
        sp.Children.Add(row);
        return await RunModal(Card(sp, title), row.Children[0]) as string;
    }

    // ---------- kategori ----------
    async Task EditCategory(CategoryVm? c)
    {
        var icons = Icons.All.Select(i => i.Label).ToArray();
        var colors = Icons.Colors.Select(i => i.Name).ToArray();
        var cur = c?.Model;
        var r = await Form(c == null ? "Yeni kategori" : "Kategoriyi düzenle", "Kaydet",
            new("Ad", cur?.Name ?? ""),
            new("Simge", Icons.All.FirstOrDefault(i => i.Key == cur?.IconKey).Label ?? icons[0], icons),
            new("Renk", Icons.Colors.FirstOrDefault(i => i.Hex == cur?.Color).Name ?? colors[0], colors));
        if (r == null) return;
        var key = Icons.All.First(i => i.Label == r[1]).Key; var hex = Icons.Colors.First(i => i.Name == r[2]).Hex;
        Guard(() =>
        {
            if (c == null) _store.AddCategory(r[0], key, hex, _cabinetId); else _store.UpdateCategory(c.Model.Id, r[0], key, hex);
            LoadAll();
        });
    }

    async Task DeleteCategory(CategoryVm c)
    {
        var others = _vm.Categories.Where(x => x != c).ToList();
        if (c.Count == 0)
        {
            if (await Choose("Kategoriyi sil", $"\"{c.Name}\" kategorisi silinsin mi?", "İptal", "Sil") == "Sil")
                Guard(() => { _store.DeleteCategory(c.Model.Id, null); LoadAll(); });
            return;
        }
        var opts = others.Count > 0 ? new[] { "İptal", "Referansları kaldır", "Başka kategoriye taşı" } : ["İptal", "Referansları kaldır"];
        var pick = await Choose("Kategoriyi sil",
            $"\"{c.Name}\" içinde {c.Count} öğe var. Dosya ve uygulamalar silinmez; yalnızca dolaptaki referanslar etkilenir.", opts);
        if (pick == "Referansları kaldır") Guard(() => { _store.DeleteCategory(c.Model.Id, null); LoadAll(); });
        else if (pick == "Başka kategoriye taşı")
        {
            var names = others.Select(o => o.Name).ToArray();
            var r = await Form("Öğeleri taşı", "Taşı ve sil", new Field("Hedef kategori", names[0], names));
            if (r == null) return;
            var target = others.First(o => o.Name == r[0]);
            Guard(() => { _store.DeleteCategory(c.Model.Id, target.Model.Id); LoadAll(); });
        }
    }

    // ---------- kisayol ----------
    async Task EditShortcut(ShortcutVm? sv, CategoryVm? into = null)
    {
        var m = sv?.Model;
        var r = await Form(sv == null ? "Kısayol ekle" : "Kısayolu düzenle", "Kaydet",
            new("Hedef (dosya, klasör veya http/https adresi)", m == null ? "" : m.SourceLinkPath ?? m.Target, Browse: true),
            new("Görünen ad (boşsa hedef adı)", m?.DisplayName ?? ""),
            new("Parametreler", m?.Arguments ?? ""),
            new("Çalışma dizini", m?.WorkingDirectory ?? ""));
        if (r == null) return;
        Guard(() =>
        {
            var d = ShortcutFactory.FromPath(r[0]);
            if (d.Kind != ShortcutKind.Lnk)
                d = d with { Arguments = string.IsNullOrWhiteSpace(r[2]) ? null : r[2], WorkingDirectory = string.IsNullOrWhiteSpace(r[3]) ? null : r[3] };
            if (!string.IsNullOrWhiteSpace(r[1])) d = d with { DisplayName = r[1] };
            if (sv != null) _store.UpdateShortcut(sv.Model.Id, d);
            else if (_store.AddShortcut(into!.Model.Id, d) == null) ShowToast("Bu hedef bu kategoride zaten var.");
            LoadAll();
        });
    }

    // ---------- arama ----------
    void ShowSearch()
    {
        var tb = new TextBox { FontSize = 14, Padding = new Thickness(8, 6, 8, 6) };
        AutomationProperties.SetName(tb, "Ara");
        var lb = new ListBox { MaxHeight = 280, ItemsSource = _vm.Results, Background = Brushes.Transparent, BorderThickness = new(0), Margin = new(0, 8, 0, 0) };
        lb.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="2,3">
              <TextBlock Text="{Binding Name}" Foreground="#EEF1F4" FontWeight="SemiBold"/>
              <TextBlock Foreground="#9AA3AD" FontSize="11" TextTrimming="CharacterEllipsis"><Run Text="{Binding CategoryName, Mode=OneWay}"/><Run Text="  -  "/><Run Text="{Binding Subtitle, Mode=OneWay}"/></TextBlock>
            </StackPanel></DataTemplate>
            """);
        void Run(ShortcutVm? sv) { if (sv == null) return; CloseModal(null); LaunchItem(sv); }
        void Refresh()
        {
            _vm.Results.Clear();
            var q = TurkishText.Fold(tb.Text.Trim());
            if (q.Length == 0) return;
            foreach (var s in _all.Where(s => s.SearchKey.Contains(q)).Take(50)) _vm.Results.Add(s);
            if (_vm.Results.Count > 0) lb.SelectedIndex = 0;
        }
        tb.TextChanged += (_, _) => Refresh();
        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Run(lb.SelectedItem as ShortcutVm ?? _vm.Results.FirstOrDefault()); e.Handled = true; }
            else if (e.Key == Key.Down && lb.Items.Count > 0) { lb.SelectedIndex = Math.Min(lb.SelectedIndex + 1, lb.Items.Count - 1); e.Handled = true; }
            else if (e.Key == Key.Up && lb.Items.Count > 0) { lb.SelectedIndex = Math.Max(lb.SelectedIndex - 1, 0); e.Handled = true; }
            else if (e.Key == Key.Escape && tb.Text.Length > 0) { tb.Clear(); e.Handled = true; } // once temizle, sonra kapat
        };
        lb.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d)
                for (; d != null; d = VisualTreeHelper.GetParent(d))
                    if (d is ListBoxItem li) { Run(li.DataContext as ShortcutVm); break; }
        };
        var close = Btn("Kapat", CancelModal);
        close.Margin = new(6, 0, 0, 0); close.MinWidth = 0; close.Padding = new(10, 4, 10, 4);
        AutomationProperties.SetName(close, "Aramayı kapat");
        var top = new DockPanel(); DockPanel.SetDock(close, Dock.Right); top.Children.Add(close); top.Children.Add(tb);
        var sp = new StackPanel(); sp.Children.Add(top); sp.Children.Add(lb);
        _vm.Results.Clear();
        _ = RunModal(Card(sp), tb);
        _searchOpen = true;
    }

    // ---------- ilk acilis / masaustu iceri aktarma ----------
    async Task FirstRun()
    {
        var r = await Form("Masaüstü Dolabı'na hoş geldiniz", "Başla",
            new("Tema", "Metal Dolap", ["Metal Dolap", "Blok Dolap"]),
            new("Kenar", "Sağ", ["Sağ", "Sol"]));
        var s = Settings with
        {
            ThemeId = r?[0] == "Blok Dolap" ? "blocks" : "metal",
            Edge = r?[1] == "Sol" ? DockEdge.Left : DockEdge.Right,
            FirstRunDone = true,
        };
        Guard(() =>
        {
            if (_store.GetCategories().Count == 0)
                foreach (var (n, i, c) in new[] { ("İş", "briefcase", "#D32F2F"), ("Geliştirme", "code", "#1E5BD8"), ("Medya", "play", "#F2B01E"), ("Arşiv", "archive", "#2E9E4F") })
                    _store.AddCategory(n, i, c, _cabinetId);
            LoadAll();
        });
        ApplySettings(s);
        ShowCabinet();
        await ShowImport();
    }

    public async Task ShowImport()
    {
        var dirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
        var found = new List<string>();
        foreach (var d in dirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct())
            try
            {
                found.AddRange(Directory.EnumerateFileSystemEntries(d).Where(p =>
                    !Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (found.Count == 0 || _vm.Categories.Count == 0) { ShowToast("İçe aktarılacak masaüstü öğesi bulunamadı."); return; }

        var checks = found.Select(p => new CheckBox
        {
            Content = Path.GetFileNameWithoutExtension(p) is { Length: > 0 } n ? n : Path.GetFileName(p),
            ToolTip = p, Tag = p, Foreground = (Brush)FindResource("TextBrush"), Margin = new(0, 2, 0, 2),
        }).ToList();
        var list = new StackPanel(); checks.ForEach(c => list.Children.Add(c));
        var names = _vm.Categories.Select(c => c.Name).ToArray();
        var cb = new ComboBox { ItemsSource = names, SelectedIndex = 0 };
        AutomationProperties.SetName(cb, "Hedef kategori");
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Seçtikleriniz dolaba referans olarak eklenir; masaüstündeki dosyalar yerinde kalır.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush"), Margin = new(0, 4, 0, 6) });
        sp.Children.Add(new ScrollViewer { MaxHeight = 260, Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        sp.Children.Add(new TextBlock { Text = "Hedef kategori", Style = (Style)FindResource("LabelStyle") });
        sp.Children.Add(cb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        row.Children.Add(Btn("Atla", CancelModal));
        row.Children.Add(Btn("İçe aktar", () => CloseModal("ok"), isDefault: true));
        sp.Children.Add(row);
        if (await RunModal(Card(sp, "Masaüstünden içe aktar"), row.Children[1]) as string != "ok") return;
        var cat = _vm.Categories[Math.Max(0, cb.SelectedIndex)];
        AddMany(cat, checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag));
    }
}
