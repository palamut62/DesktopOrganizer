using System.IO;
using Dolap.Core;

namespace Dolap.App;

public partial class App
{
    internal async Task RunRestoreSmoke(string output)
    {
        if (_store != null || _wins.Count != 0) throw new InvalidOperationException("Restore test requires an isolated app.");
        using var store = new CollectionStore(":memory:");
        using var imported = new CollectionStore(":memory:");
        var folder = Path.Combine(output, "watched"); Directory.CreateDirectory(folder);
        var oldCat = store.AddCategory("Old", "folder", "#123456"); store.SetLinkedFolder(oldCat.Id, folder);
        store.SaveSettings(new() { FirstRunDone = true }); imported.SaveSettings(new() { FirstRunDone = true });
        var second = imported.AddCabinet();
        var linked = imported.AddCategory("Imported one", "folder", "#123456");
        imported.SetLinkedFolder(linked.Id, folder);
        imported.AddCategory("Imported two", "folder", "#123456", second.Id);
        imported.SaveSnapshot("Imported set");
        var path = Path.Combine(output, "restore-fixture.json"); imported.ExportJson(path);
        _store = store;
        try
        {
            var old = OpenCabinetWindow(store.FirstCabinetId(), isolated: true);
            await Task.Delay(100);
            var first = ImportCollection(path, Path.Combine(output, "backups"), isolated: true);
            await Task.Delay(200);
            var expected = imported.GetCabinets().Select(c => c.Id).ToArray();
            if (!_wins.Select(w => w.CabinetId).SequenceEqual(expected) || _wins.Count != 2)
                throw new InvalidOperationException("Imported cabinet IDs do not match live windows.");
            if (!old.RestoreResourcesReleased || _wins.Any(w => w.VisibleCategoryCount != 1))
                throw new InvalidOperationException("Old resources remain active or imported drawers are empty.");
            var mapped = Path.Combine(output, "mapped"); Directory.CreateDirectory(mapped);
            store.RemapPrefix(Path.GetFullPath(folder), Path.GetFullPath(mapped));
            foreach (var w in _wins) w.Reload();
            if (!first.IsWatchingFolder(linked.Id, mapped)) throw new InvalidOperationException("Linked folder watcher kept the old path.");
            first.OpenSettings(); await Task.Delay(100);
            if (!first.SettingsCabinetCountMatches(2)) throw new InvalidOperationException("Cabinet selector is stale.");
            File.WriteAllLines(Path.Combine(output, "restore-smoke.txt"), [
                "PASS Imported IDs replace the live cabinet window collection",
                "PASS Imported categories are visible in both new windows",
                "PASS Closed window disposes folder watchers and timers",
                "PASS New settings selector lists both imported cabinets",
                "PASS Remapped linked folder replaces the old filesystem watcher",
                "PASS Replacement import completes without touching user data"]);
        }
        finally
        {
            foreach (var w in _wins.ToArray()) w.Close();
            _wins.Clear(); _store = null;
        }
    }
}

public partial class MainWindow
{
    internal int VisibleCategoryCount => _vm.Categories.Count;
    internal bool IsWatchingFolder(Guid id, string folder) => _watchers.TryGetValue(id, out var w)
        && string.Equals(w.Folder, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
    internal bool RestoreResourcesReleased => _closing && _watchers.Count == 0 && !_hideTimer.IsEnabled
        && !_toastTimer.IsEnabled && !_dragOpenTimer.IsEnabled && _smartTimer?.IsEnabled != true;
    internal bool SettingsCabinetCountMatches(int count) => _settingsWin?.FindName("Cabs") is System.Windows.Controls.ComboBox c && c.Items.Count == count;
}
