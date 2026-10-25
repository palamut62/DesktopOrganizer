using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class CoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dolap-test-" + Guid.NewGuid());
    readonly CollectionStore _s;
    public CoreTests() { Directory.CreateDirectory(_dir); _s = new CollectionStore(Path.Combine(_dir, "t.db")); }
    public void Dispose() { _s.Dispose(); try { Directory.Delete(_dir, true); } catch { } }

    static ShortcutDraft D(string n, string t = "C:\\x.exe") => new(n, ShortcutKind.App, t);

    [Theory]
    [InlineData("İş", "is")] [InlineData("IŞIK", "isik")] [InlineData("Geliştirme", "gelistirme")] [InlineData("ığüşöç", "igusoc")]
    public void Fold_TurkishLetters(string input, string expected) => Assert.Equal(expected, TurkishText.Fold(input));

    [Fact] public void Search_CaseInsensitive() => Assert.True(TurkishText.Matches("Yapay Zeka", TurkishText.Fold("ZEKA")));

    [Fact] public void Classify()
    {
        Assert.Equal(ShortcutKind.Url, TargetClassifier.Classify("https://x.com/a"));
        Assert.Equal(ShortcutKind.Lnk, TargetClassifier.Classify("C:\\a b\\Çalış.lnk"));
        Assert.Throws<DolapException>(() => TargetClassifier.Classify("file:///c:/x"));
        Assert.Throws<DolapException>(() => TargetClassifier.Classify("javascript:alert(1)"));
    }

    [Fact] public void Categories_PersistAndReorder()
    {
        var a = _s.AddCategory("A", "folder", "#f00"); var b = _s.AddCategory("B", "folder", "#0f0");
        _s.ReorderCategories([b.Id, a.Id]);
        Assert.Equal(["B", "A"], _s.GetCategories().Select(c => c.Name));
    }

    [Fact] public void Shortcut_DuplicateSkipped_UnlessAllowed()
    {
        var c = _s.AddCategory("A", "folder", "#f00");
        Assert.NotNull(_s.AddShortcut(c.Id, D("x")));
        Assert.Null(_s.AddShortcut(c.Id, D("x2")));
        Assert.NotNull(_s.AddShortcut(c.Id, D("x3"), allowDuplicate: true));
        var c2 = _s.AddCategory("B", "folder", "#0f0");
        Assert.NotNull(_s.AddShortcut(c2.Id, D("x")));
    }

    [Fact] public void RemoveAndRestore_KeepsIdAndOrder()
    {
        var c = _s.AddCategory("A", "folder", "#f00");
        var s = _s.AddShortcut(c.Id, D("x"))!;
        var removed = _s.RemoveShortcut(s.Id)!;
        Assert.Empty(_s.GetShortcuts());
        _s.Restore(removed);
        Assert.Equal(s, _s.GetShortcuts().Single());
    }

    [Fact] public void DeleteCategory_MoveOrDrop()
    {
        var a = _s.AddCategory("A", "f", "#f00"); var b = _s.AddCategory("B", "f", "#0f0");
        _s.AddShortcut(a.Id, D("x"));
        _s.DeleteCategory(a.Id, b.Id);
        Assert.Single(_s.GetShortcuts(b.Id));
        _s.DeleteCategory(b.Id, null);
        Assert.Empty(_s.GetShortcuts());
    }

    [Fact] public void ExportImport_RoundTrip_AndCorruptRejected()
    {
        var c = _s.AddCategory("A", "f", "#f00"); _s.AddShortcut(c.Id, D("x"));
        var pack = Path.Combine(_dir, "p.json"); _s.ExportJson(pack);
        _s.DeleteCategory(c.Id, null);
        _s.ImportJson(pack, Path.Combine(_dir, "bk"));
        Assert.Single(_s.GetShortcuts());
        var bad = Path.Combine(_dir, "bad.json"); File.WriteAllText(bad, "{nope");
        Assert.Throws<DolapException>(() => _s.ImportJson(bad, Path.Combine(_dir, "bk")));
        Assert.Single(_s.GetShortcuts());
    }

    [Fact] public void Settings_Persist()
    {
        _s.SaveSettings(new AppSettings { ThemeId = "blocks", Scale = 1.25 });
        Assert.Equal("blocks", _s.LoadSettings().ThemeId);
    }

    [Fact] public void Backup_CreatesReadableCopy()
    {
        _s.AddCategory("A", "f", "#f00");
        var f = Path.Combine(_dir, "b.db"); _s.BackupTo(f);
        using var copy = new CollectionStore(f);
        Assert.Single(copy.GetCategories());
    }
}
