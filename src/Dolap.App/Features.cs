using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dolap.Core;

namespace Dolap.App;

/// <summary>Yol haritasi ozellikleri: akilli/proje cekmeceleri, toplu baslatma, hizli onizleme, bolumler, kirik kisayol onarimi.</summary>
public partial class MainWindow
{
    public const string SmartPrefix = "smart:";
    DispatcherTimer? _smartTimer;

    // ---------- akilli cekmeceler ----------
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out string path);
    static string Downloads() => SHGetKnownFolderPath(new("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0
        ? p : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    static readonly (string Kind, string Name, string Icon, string Color)[] SmartKinds =
    [
        ("downloads", "Son indirilenler", "download", "#0097A7"),
        ("recent", "Son açılanlar", "star", "#EF6C00"),
        ("week", "Bu hafta değişenler", "doc", "#7B3FC4"),
    ];

    /// <summary>Akilli kaynak: yalniz okur. Indirilenler 14 gun/30 oge, son acilanlar 30, bu hafta 7 gun/40 oge.</summary>
    static List<ShortcutDraft> SmartItems(string kind)
    {
        IEnumerable<FileSystemInfo> Entries(string dir)
        {
            try { return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFileSystemInfos().Where(e => !e.Attributes.HasFlag(FileAttributes.Hidden) && !e.Attributes.HasFlag(FileAttributes.System)).ToList() : []; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }
        IEnumerable<FileSystemInfo> src = kind switch
        {
            "downloads" => Entries(Downloads()).Where(e => e.LastWriteTime > DateTime.Now.AddDays(-14)).OrderByDescending(e => e.LastWriteTime).Take(30),
            "recent" => Entries(Environment.GetFolderPath(Environment.SpecialFolder.Recent)).Where(e => e.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.LastWriteTime).Take(45),
            _ => new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Downloads() }
                .Distinct().SelectMany(Entries).Where(e => e.LastWriteTime > DateTime.Now.AddDays(-7)).OrderByDescending(e => e.LastWriteTime).Take(40),
        };
        var list = new List<ShortcutDraft>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in src)
        {
            try
            {
                var d = ShortcutFactory.FromPath(e.FullName);
                // Son acilanlar: Windows'un .lnk izleri; hedefi gitmis olanlar ve tekrarlar atlanir.
                if (kind == "recent")
                {
                    if (d.Kind != ShortcutKind.Lnk || (!File.Exists(d.Target) && !Directory.Exists(d.Target))) continue;
                    d = ShortcutFactory.FromPath(d.Target);
                }
                if (seen.Add(d.Target)) list.Add(d);
            }
            catch (Exception ex) when (ex is DolapException or IOException or UnauthorizedAccessException) { }
        }
        return kind == "recent" ? list.Take(30).ToList() : list;
    }

    void RefreshSmart(Guid catId, string kind)
    {
        var items = SmartItems(kind);
        var current = _store.GetShortcuts(catId).Select(s => s.Target).ToList();
        if (current.SequenceEqual(items.Select(d => d.Target))) return;
        Guard(() => _store.ReplaceManaged(catId, items));
        LoadAll();
    }

    void RefreshAllSmart()
    {
        foreach (var c in _store.GetCategories(_cabinetId).Where(c => c.LinkedFolder?.StartsWith(SmartPrefix) == true))
            RefreshSmart(c.Id, c.LinkedFolder![SmartPrefix.Length..]);
    }

    void StartSmartTimer()
    {
        if (_smartTimer != null || _isolated) return;
        _smartTimer = new() { Interval = TimeSpan.FromMinutes(10) };
        _smartTimer.Tick += (_, _) => RefreshAllSmart();
        _smartTimer.Start();
    }

    // ---------- yeni cekmece: bos / proje / akilli ----------
    async Task AddDrawer()
    {
        var pick = await Choose("Yeni çekmece", "Ne tür bir çekmece eklensin?",
            "Boş çekmece", "Proje çekmecesi", "Son indirilenler", "Son açılanlar", "Bu hafta değişenler");
        switch (pick)
        {
            case null: return;
            case "Boş çekmece": await EditCategory(null); return;
            case "Proje çekmecesi": await CreateProject(); return;
        }
        var k = SmartKinds.First(x => x.Name == pick);
        Guard(() =>
        {
            var cat = _store.AddCategory(k.Name, k.Icon, k.Color, _cabinetId);
            _store.SetLinkedFolder(cat.Id, SmartPrefix + k.Kind);
            _store.ReplaceManaged(cat.Id, SmartItems(k.Kind));
            LoadAll();
            ShowToast($"\"{k.Name}\" çekmecesi kendiliğinden güncellenir (10 dakikada bir). Dosyalara dokunulmaz.");
        });
    }

    /// <summary>Proje cekmecesi: klasor, terminal, editor, notlar ve istege bagli siteler; "Tümünü aç" ile birlikte baslar.</summary>
    async Task CreateProject()
    {
        using var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Proje klasörünü seçin", UseDescriptionForTitle = true };
        if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var folder = d.SelectedPath; var name = new DirectoryInfo(folder).Name;
        var r = await Form("Proje çekmecesi", "Oluştur", new Field("Ad", name), new Field("Siteler (virgülle ayırın, isteğe bağlı)", ""));
        if (r == null) return;
        Guard(() =>
        {
            var cat = _store.AddCategory(r[0], "code", "#1E5BD8", _cabinetId);
            _store.AddShortcut(cat.Id, new("Klasör", ShortcutKind.Folder, folder));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wt = Path.Combine(local, @"Microsoft\WindowsApps\wt.exe");
            _store.AddShortcut(cat.Id, File.Exists(wt)
                ? new("Terminal", ShortcutKind.App, wt, Arguments: $"-d \"{folder}\"")
                : new("Terminal", ShortcutKind.App, Path.Combine(Environment.SystemDirectory, "cmd.exe"), WorkingDirectory: folder));
            var code = Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe");
            if (File.Exists(code)) _store.AddShortcut(cat.Id, new("Editör", ShortcutKind.App, code, Arguments: $"\"{folder}\""));
            // Notlar uygulamanin kendi klasorunde tutulur; proje klasorune dosya yazilmaz.
            var notes = Path.Combine(Paths.Data, "notlar", string.Concat(r[0].Split(Path.GetInvalidFileNameChars())) + ".md");
            Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
            if (!File.Exists(notes)) File.WriteAllText(notes, $"# {r[0]} notları{Environment.NewLine}{Environment.NewLine}");
            _store.AddShortcut(cat.Id, new("Notlar", ShortcutKind.File, notes));
            foreach (var site in r[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var url = site.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? site : "https://" + site;
                if (TargetClassifier.IsHttpUrl(url)) _store.AddShortcut(cat.Id, ShortcutFactory.FromPath(url));
            }
            LoadAll();
            ShowToast($"\"{r[0]}\" projesi hazır. Çekmece menüsünden \"Tümünü aç\" ile hepsini başlatabilirsiniz.");
        });
    }

    // ---------- toplu baslatma ----------
    async Task LaunchAll(CategoryVm c)
    {
        var items = c.Items.ToList();
        if (items.Count == 0) { ShowToast("Çekmece boş."); return; }
        if (items.Count > 6 && await Choose("Tümünü aç", $"\"{c.Name}\" içindeki {items.Count} öğe açılacak.", "İptal", "Hepsini aç") != "Hepsini aç") return;
        int ok = 0, fail = 0;
        foreach (var sv in items)
        {
            if (ShellLauncher.Launch(sv.Model).Status == LaunchStatus.Sent) ok++; else fail++;
            await Task.Delay(180); // pencerelerin ust uste acilip sistemi bogmamasi icin
        }
        ShowToast(fail == 0 ? $"{ok} öğe için başlatma isteği gönderildi." : $"{ok} öğe başlatıldı, {fail} öğe açılamadı (hedef bulunamadı ya da erişim yok).");
    }

    // ---------- bolumler ----------
    async Task EditSection(ShortcutVm sv)
    {
        var known = _store.GetShortcuts(sv.Model.CategoryId).Select(s => s.Section).Where(s => s != null).Distinct().ToList();
        var r = await Form("Bölüm", "Kaydet", new Field("Bölüm adı (boş = bölümsüz)" + (known.Count > 0 ? "  Var olanlar: " + string.Join(", ", known) : ""), sv.Model.Section ?? ""));
        if (r == null) return;
        EditItems("Bölüm değişti", () => _store.SetSection(sv.Model.Id, r[0]));
    }

    // ---------- hizli onizleme ----------
    static readonly HashSet<string> ImageExt = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico"];
    static readonly HashSet<string> TextExt = [".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".yaml", ".yml", ".cs", ".py", ".js", ".ts", ".html", ".css", ".sql", ".bat", ".ps1"];

    void ShowPreview(ShortcutVm sv)
    {
        var path = sv.Model.Kind == ShortcutKind.Lnk ? sv.Model.Target : sv.Model.SourceLinkPath ?? sv.Model.Target;
        UIElement body;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (sv.Model.Kind == ShortcutKind.Url) body = Note("Web bağlantısı:" + Environment.NewLine + sv.Model.Target);
            else if (Directory.Exists(path))
                body = Note(string.Join(Environment.NewLine, Directory.EnumerateFileSystemEntries(path).Take(40).Select(Path.GetFileName)) + Environment.NewLine + "…");
            else if (!File.Exists(path)) body = Note("Hedef bulunamadı: " + path);
            else if (ImageExt.Contains(ext))
            {
                var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.DecodePixelWidth = 1200; b.UriSource = new Uri(path); b.EndInit();
                body = new Image { Source = b, Stretch = Stretch.Uniform, MaxWidth = 900, MaxHeight = 620 };
            }
            else if (TextExt.Contains(ext))
            {
                using var f = new StreamReader(path);
                var buf = new char[64 * 1024]; var n = f.ReadBlock(buf, 0, buf.Length);
                body = new TextBox { Text = new string(buf, 0, n) + (n == buf.Length ? Environment.NewLine + "… (ilk 64 KB)" : ""), IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 13, Width = 820, Height = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
            }
            else if (Native.ShellIcon(path, 768, thumbnail: true) is { } thumb)
                body = new Image { Source = thumb, Stretch = Stretch.Uniform, MaxWidth = 768, MaxHeight = 620 };
            else body = Note("Bu dosya türü için önizleme yok.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        { body = Note("Önizleme açılamadı: " + e.Message); }

        var title = new TextBlock { Text = sv.Name, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) };
        var hint = new TextBlock { Text = "Kapatmak için Boşluk ya da Esc · Açmak için Enter", Foreground = Brushes.Gray, FontSize = 11, Margin = new(0, 8, 0, 0) };
        var stack = new StackPanel(); stack.Children.Add(title); stack.Children.Add(body); stack.Children.Add(hint);
        var win = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Önizleme - " + sv.Name,
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 18, 21, 27)), CornerRadius = new(12), Padding = new(18), Child = stack,
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 200, 215, 235)), BorderThickness = new(1) },
        };
        win.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Space) { win.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { win.Close(); LaunchItem(sv); e.Handled = true; }
        };
        win.Deactivated += (_, _) => { if (win.IsVisible) win.Close(); };
        win.Show(); win.Activate();

        static UIElement Note(string t) => new TextBlock { Text = t, Foreground = Brushes.LightGray, FontSize = 13, MaxWidth = 700, TextWrapping = TextWrapping.Wrap };
    }

    // ---------- kirik kisayollar ve tasinabilir duzen ----------
    /// <summary>Kirik kisayollari listeler: tek tek yeni konum secme ya da ortak oneki toplu esleme.</summary>
    public async Task RepairBroken()
    {
        ShowCabinet();
        var broken = _store.FindBroken();
        if (broken.Count == 0) { ShowToast("Kırık kısayol yok; tüm hedefler yerinde."); return; }
        var prefix = CommonPrefix(broken.Select(b => b.SourceLinkPath ?? b.Target).ToList());
        var pick = await Choose("Kırık kısayollar", $"{broken.Count} kısayolun hedefi bulunamadı. Başka bilgisayardan geldiyse ortak yolu toplu eşleyebilir ya da tek tek yeni konum seçebilirsiniz."
            + Environment.NewLine + string.Join(Environment.NewLine, broken.Take(6).Select(b => "• " + b.DisplayName)) + (broken.Count > 6 ? Environment.NewLine + "…" : ""),
            "Kapat", "Yolları toplu eşle", "Tek tek onar");
        if (pick == "Yolları toplu eşle")
        {
            var r = await Form("Yolları eşle", "Eşle", new Field("Eski yol öneki", prefix), new Field("Bu bilgisayardaki karşılığı", ""));
            if (r == null) return;
            var n = 0; Guard(() => n = _store.RemapPrefix(r[0], r[1]));
            (Application.Current as App)?.CabinetWindows.ToList().ForEach(w => w.Reload());
            ShowToast($"{n} kısayolun yolu güncellendi; {_store.FindBroken().Count} kırık kısayol kaldı.");
        }
        else if (pick == "Tek tek onar")
            foreach (var b in broken)
            {
                var a = await Choose("Kırık kısayol", $"\"{b.DisplayName}\"{Environment.NewLine}{b.SourceLinkPath ?? b.Target}", "Atla", "Yeni konum seç", "Dolaptan kaldır", "Bitir");
                if (a is null or "Bitir") break;
                if (a == "Dolaptan kaldır") Guard(() => _store.RemoveShortcut(b.Id));
                if (a == "Yeni konum seç")
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = b.DisplayName + " için yeni hedef" };
                    if (dlg.ShowDialog() == true) Guard(() => _store.UpdateShortcut(b.Id, ShortcutFactory.FromPath(dlg.FileName) with { DisplayName = b.DisplayName }));
                }
                (Application.Current as App)?.CabinetWindows.ToList().ForEach(w => w.Reload());
            }
    }

    /// <summary>Yollarin ortak klasor oneki (toplu esleme icin oneri). Test: MainWindow disinda saf mantik.</summary>
    public static string CommonPrefix(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return "";
        var parts = paths.Select(p => p.Split('\\')).ToList();
        var n = 0;
        while (parts.All(p => p.Length > n + 1 && string.Equals(p[n], parts[0][n], StringComparison.OrdinalIgnoreCase))) n++;
        return string.Join('\\', parts[0].Take(n));
    }
}
