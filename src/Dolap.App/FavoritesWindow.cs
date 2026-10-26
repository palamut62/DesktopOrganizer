using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dolap.Core;

namespace Dolap.App;

public sealed class FavoritesWindow : Window
{
    readonly CollectionStore _store;
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Margin = new(8) };
    public FavoritesWindow(CollectionStore store)
    {
        _store = store; Title = "Favoriler"; ShowInTaskbar = false; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        Background = (Brush)Application.Current.FindResource("CardBrush");
        Content = new Border { Child = _row, BorderBrush = Brushes.SlateGray, BorderThickness = new(1), CornerRadius = new(8) };
        Loaded += (_, _) => Place(); Refresh();
    }
    public void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2); Top = area.Top + 12;
    }
    public void Refresh()
    {
        _row.Children.Clear();
        _row.Children.Add(new TextBlock { Text = "★", FontSize = 20, Foreground = Brushes.Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new(5) });
        var shortcuts = _store.GetShortcuts().ToDictionary(s => s.Id);
        foreach (var id in _store.LoadUsability().Favorites.Take(8))
        {
            var exists = shortcuts.TryGetValue(id, out var item);
            var vm = exists ? new ShortcutVm(item!, "Favoriler") : null;
            var content = new StackPanel();
            content.Children.Add(new Image { Source = vm?.Icon, Width = 28, Height = 28 });
            content.Children.Add(new TextBlock { Text = vm?.Name ?? "Kaldırılmış", TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center });
            var button = new Button { Content = content, Width = 76, Margin = new(2), Padding = new(3), Foreground = Brushes.White, Background = Brushes.Transparent, ToolTip = vm?.Subtitle ?? "Kısayol kaldırılmış. Sağ tık ile favoriden çıkarın.", ContextMenu = new() };
            button.Click += (_, _) => { if (item != null) (Application.Current as App)?.FirstWindow?.ShowToast(ShellLauncher.Launch(item).Message); };
            var remove = new MenuItem { Header = "Favorilerden kaldır" };
            remove.Click += (_, _) => { _store.ToggleFavorite(id); (Application.Current as App)?.RefreshUsability(); };
            button.ContextMenu.Items.Add(remove); _row.Children.Add(button);
        }
        if (IsLoaded) { UpdateLayout(); Place(); }
    }
}
