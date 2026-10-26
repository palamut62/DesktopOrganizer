using System.IO;
using System.Windows.Threading;
using Dolap.Core;

namespace Dolap.App;

/// <summary>Klasore bagli cekmeceler: klasoru FileSystemWatcher ile izler, degisiklikten 800 ms sonra cekmeceyi eslitler.
/// Yalniz okuma yapar; klasordeki hicbir dosyaya yazmaz, silmez, tasimaz.</summary>
public partial class MainWindow
{
    readonly Dictionary<Guid, (string Folder, FileSystemWatcher Watcher, DispatcherTimer Debounce)> _watchers = [];

    /// <summary>LoadAll sonrasi: bagli kategoriler icin izleyici kur, artik bagli olmayanlarinkini kapat.</summary>
    void EnsureFolderWatchers(IEnumerable<Category> cats)
    {
        var linked = cats.Where(c => !string.IsNullOrWhiteSpace(c.LinkedFolder)).ToDictionary(c => c.Id, c => c.LinkedFolder!);
        foreach (var id in _watchers.Keys.ToList())
            if (!linked.TryGetValue(id, out var f) || !string.Equals(f, _watchers[id].Folder, StringComparison.OrdinalIgnoreCase))
                StopWatcher(id);
        foreach (var (id, folder) in linked)
        {
            if (_watchers.ContainsKey(id) || !Directory.Exists(folder)) continue;
            var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            var catId = id;
            debounce.Tick += (_, _) => { debounce.Stop(); SyncFolder(catId, folder, quiet: true); };
            var w = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            void Queue() => Dispatcher.BeginInvoke(() =>
            {
                if (_closing || !_watchers.TryGetValue(catId, out var active) || active.Watcher != w) return;
                debounce.Stop(); debounce.Start();
            });
            FileSystemEventHandler h = (_, _) => Queue();
            w.Created += h; w.Deleted += h; w.Renamed += (_, _) => Queue();
            w.Error += (_, _) => Queue();
            w.EnableRaisingEvents = true;
            _watchers[id] = (folder, w, debounce);
        }
    }

    void StopWatcher(Guid id)
    {
        if (!_watchers.Remove(id, out var x)) return;
        x.Debounce.Stop(); x.Watcher.EnableRaisingEvents = false; x.Watcher.Dispose();
    }

    /// <summary>Klasorun ust duzey ogelerini okuyup cekmeceye yansitir. Gizli/sistem dosyalari atlanir.</summary>
    void SyncFolder(Guid catId, string folder, bool quiet)
    {
        if (folder.StartsWith(SmartPrefix)) { RefreshSmart(catId, folder[SmartPrefix.Length..]); return; }
        if (!Directory.Exists(folder)) { if (!quiet) ShowToast("Bağlı klasör bulunamadı: " + folder); return; }
        var drafts = new List<ShortcutDraft>();
        try
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(folder))
            {
                if (Path.GetExtension(p).ToLowerInvariant() is ".crdownload" or ".part" or ".tmp" or ".download") continue;
                var attr = File.GetAttributes(p);
                if (attr.HasFlag(FileAttributes.Hidden) || attr.HasFlag(FileAttributes.System)) continue;
                try { drafts.Add(ShortcutFactory.FromPath(p)); }
                catch (Exception e) when (e is DolapException or IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowToast("Klasör okunamadı: " + e.Message); return; }
        (int Added, int Removed) r = default;
        var previousIds = _store.GetShortcuts(catId).Select(s => s.Id).ToHashSet();
        var desktop = string.Equals(folder, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StringComparison.OrdinalIgnoreCase);
        var ordered = desktop ? drafts.OrderByDescending(d => File.GetLastWriteTimeUtc(d.SourceLinkPath ?? d.Target)).ToList()
            : drafts.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        Guard(() => r = _store.SyncLinkedFolder(catId, folder, ordered));
        if (r.Added + r.Removed == 0) { if (!quiet) ShowToast("Çekmece klasörle güncel."); return; }
        LoadAll();
        ShowToast($"Klasör eşitlendi: {r.Added} eklendi, {r.Removed} çıkarıldı (dosyalara dokunulmadı).");
        if (desktop && r.Added > 0) QueueDesktopSort(_store.GetShortcuts(catId).Where(s => !previousIds.Contains(s.Id)).Select(s => s.Id));
    }

    void LinkFolder(CategoryVm c)
    {
        using var d = new System.Windows.Forms.FolderBrowserDialog { Description = $"\"{c.Name}\" çekmecesi hangi klasörü yansıtsın?", UseDescriptionForTitle = true };
        if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Guard(() => _store.SetLinkedFolder(c.Model.Id, d.SelectedPath));
        SyncFolder(c.Model.Id, d.SelectedPath, quiet: false);
        LoadAll();
    }

    bool? _desktopInboxEnabled;
    internal void SyncDesktopInbox(bool force = false)
    {
        if (_isolated) return;
        var enabled = _store.LoadUsability().DesktopInbox;
        if (!force && _desktopInboxEnabled == enabled) return;
        _desktopInboxEnabled = enabled;
        Guard(() =>
        {
            var inbox = _store.EnsureDesktopInbox(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            if (inbox != null) SyncFolder(inbox.Id, inbox.LinkedFolder!, quiet: true);
            LoadAll(); (System.Windows.Application.Current as App)?.RefreshOthers(this, reload: true);
        });
    }
}
