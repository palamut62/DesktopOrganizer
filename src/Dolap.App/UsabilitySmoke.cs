using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolap.Core;

namespace Dolap.App;

public partial class MainWindow
{
    internal async Task RunUsabilitySmoke(string output)
    {
        var results = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            results.Add("PASS " + message); File.WriteAllLines(Path.Combine(output, "usability-smoke.txt"), results);
        }
        var category = _store.AddCategory("Sayfalı evrak", "folder", "#123456", _cabinetId);
        for (int i = 0; i < 28; i++) _store.AddShortcut(category.Id, new($"Belge {i:00}", ShortcutKind.File, Path.Combine(output, $"missing-{i}.pdf")));
        var other = _store.AddCabinet("İkinci çalışma dolabı");
        var external = _store.AddCategory("Diğer evrak", "folder", "#123456", other.Id);
        var hit = _store.AddShortcut(external.Id, new("Ölçüm listesi", ShortcutKind.File, Path.Combine(output, "missing-measure.txt")))!;
        LoadAll(); OpenOnly(_vm.Categories.Single(c => c.Model.Id == category.Id));
        Scene.ChangePage(_vm.Categories.Single(c => c.Model.Id == category.Id), 1);
        LoadAll();
        Check(_store.LoadUsability().DrawerPages[category.Id] == 1, "Drawer page survives scene rebuilding");
        Check(Scene.ShortcutButtons.Count(b => ((ShortcutVm)b.DataContext).Model.CategoryId == category.Id) == 12, "Large drawer renders only its current twelve cards");
        var quick = new QuickAccessWindow(this); quick.Show(); await Task.Delay(180);
        try
        {
            quick.Query.Text = "olcum";
            Check(quick.Results.Items.OfType<SearchHit>().Any(h => h.Shortcut?.Id == hit.Id), "Quick access finds Turkish text in another cabinet");
            Check(quick.Results.SelectionMode == SelectionMode.Extended, "Quick access enables native Ctrl and Shift multi-selection");
        }
        finally { quick.Close(); }
        quick = new QuickAccessWindow(this, category.Id); quick.Show(); await Task.Delay(100);
        try
        {
            quick.Results.SelectedItems.Clear(); quick.Results.SelectedItems.Add(quick.Results.Items[0]); quick.Results.SelectedItems.Add(quick.Results.Items[1]);
            Check(quick.SelectedIds().Length == 2, "Two rows can be selected for one bulk operation");
            var count = _store.GetShortcuts(category.Id).Count;
            // Exercise actual confirmation buttons, not a duplicate implementation.
            var remove = FindButtons(quick).Single(b => b.Content as string == "Kaldır");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(_store.GetShortcuts(category.Id).Count == count, "First remove click requests in-app confirmation without mutation");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(_store.GetShortcuts(category.Id).Count == count - 2, "Confirmed removal removes the two selected references");
            FindButtons(quick).Single(b => b.Content as string == "Geri al (Ctrl+Z)").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(_store.GetShortcuts(category.Id).Count == count, "Undo button restores the entire bulk operation");
            quick.UpdateLayout(); CaptureUsability(quick, Path.Combine(output, "quick-access.png"));
        }
        finally { quick.Close(); }
        _store.ToggleFavorite(hit.Id);
        var favorites = new FavoritesWindow(_store); favorites.Show(); await Task.Delay(100);
        try
        {
            Check(FindButtons(favorites).Any(b => b.ToolTip as string == hit.Target), "Favorite strip contains the pinned shortcut");
            CaptureUsability(favorites, Path.Combine(output, "favorites.png"));
        }
        finally { favorites.Close(); }
        var saved = _store.GetCabinets().Single(c => c.Id == _cabinetId);
        bool handled = false; WndProc(_hwnd, 0x007E, IntPtr.Zero, IntPtr.Zero, ref handled); await Task.Delay(100);
        Check(_store.GetCabinets().Single(c => c.Id == _cabinetId) == saved, "Display-change handling leaves saved monitor and position intact");
        OpenSettings(); await Task.Delay(80);
        Check(_settingsWin?.FindName("QuickHotkeyBox") is TextBox { Text: "Ctrl+Alt+Space" }, "Settings exposes the separate quick-access shortcut");
        _settingsWin?.Close();
        var desktopFixture = Path.Combine(output, "desktop-inbox-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(desktopFixture);
        var inbox = _store.EnsureDesktopInbox(desktopFixture)!;
        SyncFolder(inbox.Id, desktopFixture, quiet: true); LoadAll();
        var partial = Path.Combine(desktopFixture, "NusaSetup.zip.crdownload"); File.WriteAllText(partial, "fixture only");
        SyncFolder(inbox.Id, desktopFixture, quiet: true);
        Check(_store.GetShortcuts(inbox.Id).Count == 0, "Desktop inbox ignores incomplete browser downloads");
        var completed = Path.Combine(desktopFixture, "NusaSetup.zip"); File.Move(partial, completed);
        for (int i = 0; i < 40 && _store.GetShortcuts(inbox.Id).Count == 0; i++) await Task.Delay(100);
        Check(_store.GetShortcuts(inbox.Id).Any(s => s.Target == completed) && File.Exists(completed), "Completed desktop download appears automatically without moving the file");
        var assistant = new AssistantWindow(this, loadModels: false); assistant.Show(); await Task.Delay(120);
        try
        {
            Check(FindButtons(assistant).Single(b => b.Content as string == "Gösterilen planı uygula").IsEnabled == false,
                "Assistant cannot apply changes before a validated plan exists");
            CaptureUsability(assistant, Path.Combine(output, "assistant.png"));
        }
        finally { assistant.Close(); }
    }

    static void CaptureUsability(Window window, string file)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
}
