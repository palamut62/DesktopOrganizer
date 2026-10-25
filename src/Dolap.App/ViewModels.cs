using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Dolap.Core;

namespace Dolap.App;

public abstract class Obs : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return;
        f = v; PropertyChanged?.Invoke(this, new(n));
    }
    protected void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

public sealed class ShortcutVm(Shortcut model, string categoryName) : Obs
{
    ImageSource? _icon; bool _iconRequested;
    public Shortcut Model { get; set; } = model;
    public string CategoryName { get; set; } = categoryName;
    public string Name => Model.DisplayName;
    /// <summary>Hedefi bulunamayan yerel kisayol (kirik); kartta soluk gosterilir.</summary>
    public bool Broken { get; } = model.Kind != ShortcutKind.Url && !System.IO.File.Exists(model.SourceLinkPath ?? model.Target) && !System.IO.Directory.Exists(model.SourceLinkPath ?? model.Target);
    string? _key;
    public string SearchKey => _key ??= TurkishText.Fold($"{Name} {CategoryName} {Model.Target}");
    public string Subtitle => Model.Kind == ShortcutKind.Url ? Model.Target : Model.SourceLinkPath ?? Model.Target;
    public string Automation => $"{Name}, {CategoryName} kategorisi, {Model.Kind switch { ShortcutKind.Url => "web baglantisi", ShortcutKind.Folder => "klasor", _ => "ac" }}";

    /// <summary>Ikon ilk okunusta arka planda yuklenir.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                var path = Model.SourceLinkPath ?? Model.Target; // URL icin kabuk varsayilan tarayici ikonunu verir
                if (path != null)
                    Task.Run(() => Native.ShellIcon(path, 256)).ContinueWith(t =>
                    {
                        if (t.Result is { } img) Application.Current?.Dispatcher.Invoke(() => { _icon = img; Raise(nameof(Icon)); });
                    });
            }
            return _icon;
        }
    }
    public string Fallback => Model.Kind switch { ShortcutKind.Url => "\uE774", ShortcutKind.Folder => "\uE8B7", ShortcutKind.File => "\uE8A5", _ => "\uE71D" };
    /// <summary>Kart kosesindeki kucuk tur rozeti (uygulama icin yok).</summary>
    public string KindGlyph => Model.Kind switch { ShortcutKind.Url => "\uE774", ShortcutKind.Folder => "\uE8B7", ShortcutKind.File => "\uE8A5", ShortcutKind.Lnk => "\uE71B", _ => "" };
}

public sealed class CategoryVm(Category model) : Obs
{
    bool _open;
    public Category Model { get; set; } = model;
    public ObservableCollection<ShortcutVm> Items { get; } = [];
    /// <summary>Kapali cekmece onundeki kucuk simge seridi (ilk 5 oge).</summary>
    public List<ShortcutVm> PreviewItems => Items.Take(5).ToList();
    public Visibility EmptyVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Name => Model.Name;
    public int Count => Items.Count;
    public bool IsOpen { get => _open; set => Set(ref _open, value); }
    public string Glyph => Icons.Glyph(Model.IconKey);
    public Brush Fill { get; private set; } = Brushes.Gray;
    public Brush LabelBrush { get; private set; } = Brushes.White;
    public string Automation => $"{Name} cekmecesi, {Count} oge" + (Model.LinkedFolder is { } f ? $", {f} klasorune bagli" : "");

    public void Refresh()
    {
        Fill = Icons.Brush(Model.Color);
        var color = (Color)ColorConverter.ConvertFromString(Model.Color);
        LabelBrush = .2126 * color.R + .7152 * color.G + .0722 * color.B > 155 ? Brushes.Black : Brushes.White;
        foreach (var n in new[] { nameof(Name), nameof(Glyph), nameof(Fill), nameof(LabelBrush), nameof(Count), nameof(PreviewItems), nameof(Automation), nameof(EmptyVisibility) }) Raise(n);
    }
}

public sealed class MainVm : Obs
{
    string _toast = ""; bool _toastVisible, _hasUndo; int _side = 1;
    public ObservableCollection<CategoryVm> Categories { get; } = [];
    public ObservableCollection<ShortcutVm> Results { get; } = [];
    public string Toast { get => _toast; set => Set(ref _toast, value); }
    public bool ToastVisible { get => _toastVisible; set { Set(ref _toastVisible, value); Raise(nameof(ToastVisibility)); } }
    public bool HasUndo { get => _hasUndo; set { Set(ref _hasUndo, value); Raise(nameof(UndoVisibility)); } }
    public Visibility ToastVisibility => _toastVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UndoVisibility => _hasUndo ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>+1 sag kenar (cekmece sola acilir), -1 sol kenar.</summary>
    public int Side { get => _side; set { Set(ref _side, value); Raise(nameof(Align)); Raise(nameof(SlideDir)); } }
    public HorizontalAlignment Align => _side > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public double SlideDir => -_side;
}

static class Icons
{
    public static readonly (string Key, string Glyph, string Label)[] All =
    [
        ("folder", "", "Klasor"), ("briefcase", "", "Is"), ("play", "", "Medya"), ("code", "", "Gelistirme"),
        ("archive", "", "Arsiv"), ("globe", "", "Web"), ("home", "", "Ev"), ("photo", "", "Foto"),
        ("music", "", "Muzik"), ("game", "", "Oyun"), ("mail", "", "Posta"), ("star", "", "Favori"),
        ("download", "", "Indirilenler"), ("doc", "", "Belge"), ("tag", "", "Etiket"),
    ];
    public static readonly (string Name, string Hex)[] Colors =
    [
        ("Kirmizi", "#D32F2F"), ("Sari", "#F2B01E"), ("Mavi", "#1E5BD8"), ("Yesil", "#2E9E4F"),
        ("Turuncu", "#EF6C00"), ("Mor", "#7B3FC4"), ("Turkuaz", "#0097A7"), ("Gri", "#6B7480"),
    ];
    public static string Glyph(string key) => All.FirstOrDefault(i => i.Key == key).Glyph ?? "";
    public static Brush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new LinearGradientBrush(Lighten(c, 0.18), Darken(c, 0.12), 90);
        b.Freeze();
        return b;
    }
    static Color Lighten(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    static Color Darken(Color c, double t) => Color.FromRgb((byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));
}
