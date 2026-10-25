using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dolap.App.Controls;
using Dolap.Core;

namespace Dolap.App;

/// <summary>Opt-in isolated visual regression fixture. Never opens the user's database,
/// registers hotkeys, changes startup preferences, or launches shortcut targets.</summary>
internal static class DesignPreview
{
    public static void Start(string[] args)
    {
        var check = args.Contains("--visual-check");
        var flag = Array.IndexOf(args, "--visual-check");
        var output = check ? args.ElementAtOrDefault(flag + 1) : null;
        if (check && string.IsNullOrWhiteSpace(output)) { Application.Current.Shutdown(2); return; }
        if (check) Directory.CreateDirectory(output!);
        App.ApplyTheme("metal", false, false);
        var scene = new CabinetScene { HorizontalAlignment = HorizontalAlignment.Center };
        var cats = Fixtures();
        scene.SetCategories(cats);
        var layout = new Grid { Background = new SolidColorBrush(Color.FromRgb(12, 16, 23)), Width = 680, Height = 850 };
        var status = new TextBlock { Text = "METAL / ÖRNEK KOLEKSİYON", Foreground = Brushes.LightSteelBlue, FontSize = 13, Margin = new(24, 18, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var fit = new Viewbox { Child = scene, Stretch = Stretch.Uniform };
        var scroll = new Border { Child = fit, Margin = new(0, 40, 0, 42) };
        layout.Children.Add(scroll); layout.Children.Add(status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 12) };
        var themeButton = new Button { Content = "Metal / Blok", Style = (Style)Application.Current.FindResource("ModalButtonStyle"), Padding = new(12, 5, 12, 5) };
        var sideButton = new Button { Content = "Sol / Sağ", Style = (Style)Application.Current.FindResource("ModalButtonStyle"), Padding = new(12, 5, 12, 5), Margin = new(10, 0, 0, 0) };
        footer.Children.Add(themeButton); footer.Children.Add(sideButton); layout.Children.Add(footer);
        string theme = "metal"; int side = 1; int clicked = 0;
        void Refresh() { App.ApplyTheme(theme, false, false); scene.Configure(theme, side, false, false); status.Text = (theme == "metal" ? "METAL" : "BLOK") + " / ÖRNEK KOLEKSİYON"; }
        themeButton.Click += (_, _) => { theme = theme == "metal" ? "blocks" : "metal"; Refresh(); };
        sideButton.Click += (_, _) => { side *= -1; Refresh(); };
        scene.FrontClick += (sender, _) =>
        {
            var c = (CategoryVm)((Button)sender).DataContext; var next = !c.IsOpen;
            foreach (var other in cats) other.IsOpen = other == c && next;
        };
        scene.ItemClick += (sender, _) => { clicked++; status.Text = "Önizleme: " + ((ShortcutVm)((Button)sender).DataContext).Name; };
        var window = new Window { Title = "Masaüstü Dolabı - Tasarım Önizlemesi", Content = layout, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.Black, ResizeMode = ResizeMode.NoResize };
        window.Closed += (_, _) => Application.Current.Shutdown();
        window.Loaded += async (_, _) =>
        {
            if (!check) { cats[1].IsOpen = true; return; }
            var results = new List<string>();
            try
            {
                void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); results.Add("PASS " + name); }
                var mesh = (System.Windows.Media.Media3D.MeshGeometry3D)CabinetMesh.Box(0, 0, 0, 100, 120, 80, CabinetMesh.Finish("#888888")).Geometry;
                foreach (var axis in new[] { "side", "top" })
                {
                    var uv = Enumerable.Range(0, mesh.Positions.Count)
                        .Where(i => axis == "side" ? mesh.Normals[i].X > .99 : mesh.Normals[i].Y > .99)
                        .Select(i => mesh.TextureCoordinates[i]).ToArray();
                    Require(uv.Length > 0 && uv.Max(p => p.X) - uv.Min(p => p.X) > .8 && uv.Max(p => p.Y) - uv.Min(p => p.Y) > .8,
                        axis + " texture uses two dimensions instead of stretching a single row");
                }
                var identity = cats.Select(c => (c.Model.Id, Ids: string.Join(",", c.Items.Select(s => s.Model.Id)))).ToArray();
                await Task.Delay(300);
                await Capture("metal-closed");
                scene.DrawerButtons.ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(420);
                Require(cats[1].IsOpen && scene.DrawerTravel(cats[1]) > CabinetScene.Travel - 8, "Drawer button opens physical drawer");
                Require(cats.Count(c => c.IsOpen) == 1, "Only selected drawer opens");
                await Capture("metal-open");
                var item = scene.ShortcutButtons.First(b => b.IsEnabled && ((ShortcutVm)b.DataContext).CategoryName == cats[1].Name);
                item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(clicked == 1, "Shortcut control dispatches exactly one invocation");
                var stableHeight = scene.Height;
                for (var i = 0; i < 21; i++) { cats[1].IsOpen = !cats[1].IsOpen; await Task.Delay(8); }
                cats[1].IsOpen = true; await Task.Delay(420);
                Require(Math.Abs(scene.DrawerTravel(cats[1]) - CabinetScene.Travel) < .01 && scene.Height == stableHeight, "Interrupted animation converges without resizing cabinet");
                theme = "blocks"; Refresh(); await Task.Delay(180);
                Require(cats[1].IsOpen && identity.SequenceEqual(cats.Select(c => (c.Model.Id, string.Join(",", c.Items.Select(s => s.Model.Id))))), "Theme switch preserves open category and all shortcut identities");
                await Capture("blocks-open");
                side = -1; Refresh(); await Task.Delay(180); await Capture("blocks-left");
                theme = "metal"; Refresh(); await Task.Delay(180); await Capture("metal-left");
                side = 1; Refresh(); scene.SetView(35, 12); await Task.Delay(150); await Capture("metal-rotated");
                scene.SetView(-40, -15); theme = "blocks"; Refresh(); await Task.Delay(150); await Capture("blocks-rotated"); scene.SetView(0, 0); theme = "metal"; Refresh();
                // Yon oklari: art arda hizli tiklama (cift tik dahil) sifirlamamali; ok nesneyi o yone cevirmeli.
                theme = "metal"; side = 1; Refresh(); scene.SetView(0, 0); await Task.Delay(50);
                var left = scene.ArrowButtons[0]; var up = scene.ArrowButtons[1];
                void Press(System.Windows.Controls.Primitives.RepeatButton b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                void DoubleClick(System.Windows.Controls.Primitives.RepeatButton b) => b.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
                Press(left); await Task.Delay(40); Press(left); DoubleClick(left); await Task.Delay(40); Press(left);
                // Soguk baslangicta kareler yavas: sabit bekleme yerine animasyonun oturmasini bekle (en fazla 3 sn).
                for (var w = 0; w < 60 && Math.Abs(scene.ViewAngles.Yaw - scene.TargetAngles.Yaw) > .1; w++) await Task.Delay(50);
                await Task.Delay(100);
                Require(scene.TargetAngles.Yaw > 15 && Math.Abs(scene.ViewAngles.Yaw - scene.TargetAngles.Yaw) < .1, "Quick repeated left-arrow clicks accumulate (no reset)");
                var builtArrow = scene.ArrowButtons[0];
                scene.Configure(theme, side, false, false);
                Require(ReferenceEquals(builtArrow, scene.ArrowButtons[0]), "Re-applying same settings keeps controls (no rebuild mid-press)");
                Press(up); await Task.Delay(500);
                Require(scene.TargetAngles.Pitch < 0, "Up arrow tilts the cabinet front upward (camera lower)");
                await Capture("metal-arrows");
                scene.SetView(0, 0);
                scene.Configure(theme, side, true, true); cats[1].IsOpen = false;
                Require(scene.DrawerTravel(cats[1]) == 0, "Reduced motion closes immediately");
                Require(!scene.ShortcutButtons.Any(b => b.IsEnabled), "Closed drawers disable hidden shortcut controls");
                File.WriteAllLines(Path.Combine(output!, "visual-check.txt"), results);
                window.Close();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output!, "visual-check.txt"), string.Join(Environment.NewLine, results) + Environment.NewLine + ex); Application.Current.Shutdown(1); }

            async Task Capture(string name)
            {
                scene.FinishAnimations(); layout.UpdateLayout();
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var bitmap = new RenderTargetBitmap((int)layout.ActualWidth * 2, (int)layout.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(layout);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output!, name + ".png")); encoder.Save(file);
            }
        };
        window.Show();
    }

    static List<CategoryVm> Fixtures()
    {
        var result = new List<CategoryVm>();
        foreach (var (name, color, glyph) in new[] { ("İş", "#C62E36", "briefcase"), ("Geliştirme", "#175AC9", "code"), ("Medya", "#E7AD16", "play"), ("Arşiv", "#26804B", "archive") })
        {
            var c = new CategoryVm(new(Guid.NewGuid(), name, glyph, color, result.Count));
            foreach (var title in new[] { "Projeler", "Terminal", "Belgeler", "GitHub", "Araçlar", "Notlar" })
                c.Items.Add(new ShortcutVm(new(Guid.NewGuid(), c.Model.Id, title, title == "GitHub" ? ShortcutKind.Url : ShortcutKind.Folder, null, title == "GitHub" ? "https://github.com" : Environment.GetFolderPath(Environment.SpecialFolder.Windows), null, null, c.Items.Count), name));
            c.Refresh(); result.Add(c);
        }
        return result;
    }
}
