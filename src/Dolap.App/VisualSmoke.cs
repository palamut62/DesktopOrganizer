using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolap.Core;

namespace Dolap.App;

internal static class VisualSmoke
{
    public static void Start(string[] args)
    {
        var output = args.ElementAtOrDefault(Array.IndexOf(args, "--app-smoke") + 1);
        if (string.IsNullOrWhiteSpace(output)) { Application.Current.Shutdown(2); return; }
        Directory.CreateDirectory(output);
        var store = new CollectionStore(":memory:");
        foreach (var (name, color) in new[] { ("İş", "#C62E36"), ("Geliştirme", "#175AC9"), ("Medya", "#E7AD16"), ("Arşiv", "#26804B") })
        {
            var cat = store.AddCategory(name, "folder", color);
            for (var n = 0; n < 7; n++) store.AddShortcut(cat.Id, new("Belge " + (n + 1), ShortcutKind.File, Path.Combine(output, "missing-" + n + ".txt")));
        }
        store.SaveSettings(new() { FirstRunDone = true, Scale = .85, Edge = DockEdge.Right });
        var window = new MainWindow(store, isolated: true);
        window.Loaded += async (_, _) =>
        {
            try { await window.RunVisualSmoke(output); window.Close(); store.Dispose(); Application.Current.Shutdown(); }
            catch (Exception ex) { File.AppendAllText(Path.Combine(output, "app-smoke.txt"), Environment.NewLine + ex); window.Close(); store.Dispose(); Application.Current.Shutdown(1); }
        };
        window.Show();
    }
}

public partial class MainWindow
{
    internal async Task RunVisualSmoke(string output)
    {
        var results = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            results.Add("PASS " + message);
            File.WriteAllLines(Path.Combine(output, "app-smoke.txt"), results);
        }
        await Task.Delay(250);
        Check(Scene.DrawerCount == 4, "Production window loads categories through its real store and viewmodel");
        Scene.DrawerButtons.ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(450);
        Check(_vm.Categories[1].IsOpen && Scene.DrawerTravel(_vm.Categories[1]) > Controls.CabinetScene.Travel - 8, "Production Front_Click opens 3D drawer");
        var firstItem = Scene.ShortcutButtons.First(b => b.IsEnabled);
        firstItem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        firstItem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(_vm.ToastVisible && _vm.Toast.Contains("Hedef bulunamadi"), "Production item click reaches launcher and safely reports missing target");
        _vm.ToastVisible = false;
        var before = _store.GetShortcuts().Select(s => s.Id).ToArray();
        ApplySettings(Settings with { ThemeId = "blocks" });
        await Task.Delay(180);
        Check(_vm.Categories[1].IsOpen && Scene.DrawerTravel(_vm.Categories[1]) == Controls.CabinetScene.Travel, "Production theme switch retains open drawer");
        Check(_store.GetCategories().Count == 4 && before.SequenceEqual(_store.GetShortcuts().Select(s => s.Id)), "Theme change preserves real store collection");
        var add = FindButtons(Scene).First(b => b.IsEnabled && b.Content as string == "+ Kısayol ekle");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        Check(ModalOpen, "Projected tray Add control opens production edit modal");
        CancelModal();
        Check(!ModalOpen, "Production modal can be dismissed without data changes");
        ApplySettings(Settings with { Edge = DockEdge.Left, ThemeId = "metal", ReduceMotion = true });
        await Task.Delay(150);
        Check(_vm.Side == -1 && Scene.DrawerCount == 4, "Production left docking rebuilds the scene correctly");
        // Pencere odak kaybedince (Deactivated) cekmeceler kapanir; kapatma testinden once acik oldugundan emin ol.
        OpenOnly(_vm.Categories[1]);
        Scene.DrawerButtons.ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!_vm.Categories[1].IsOpen && Scene.DrawerTravel(_vm.Categories[1]) == 0, "Production reduced-motion close is immediate");
        OpenSettings(); await Task.Delay(150);
        Check(_settingsWin is { IsLoaded: true }, "Production settings window loads with new resource dictionaries");
        _settingsWin!.Close();
        // Capture the production control, preserving alpha in the desktop's empty area.
        foreach (var theme in new[] { "metal", "blocks" })
        {
            ApplySettings(Settings with { ThemeId = theme, Edge = DockEdge.Right });
            OpenOnly(_vm.Categories[1]); await Task.Delay(150); UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * 2), (int)Math.Ceiling(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(Root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, theme + "-production.png")); encoder.Save(file);
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Check(pixel[3] == 0, theme + " production screenshot preserves transparent empty pixels");
        }
    }

    static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        if (root is Button b) yield return b;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindButtons(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
