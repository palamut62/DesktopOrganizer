using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class V5Tests
{
    [Fact] public void DesignPersistsPerCabinet()
    {
        using var s = new CollectionStore(":memory:");
        var id = s.FirstCabinetId();
        var d = new CabinetDesign("#AA3300", "knob", "plain", "wide", Visible: false);
        s.SaveEffective(id, s.LoadEffective(id) with { Design = d });
        Assert.Equal(d, s.LoadEffective(id).Design);
        Assert.Equal(new CabinetDesign(), s.LoadEffective(s.AddCabinet().Id).Design);
    }

    [Fact] public void WorkSet_RestoresLayoutAndVisibility_NotCollection()
    {
        using var s = new CollectionStore(":memory:");
        var id = s.FirstCabinetId();
        var cat = s.AddCategory("A", "folder", "#f00");
        s.SaveEffective(id, s.LoadEffective(id) with { FreeX = 0.2, Design = new() { Visible = true } });
        var set = s.SaveSnapshot("Yazılım");
        s.SaveEffective(id, s.LoadEffective(id) with { FreeX = 0.9, Design = new() { Visible = false } });
        s.AddShortcut(cat.Id, new("x", ShortcutKind.App, @"C:\x.exe"));
        Assert.Equal(1, s.RestoreSnapshot(set.Id));
        Assert.Equal(0.2, s.LoadEffective(id).FreeX, 3);
        Assert.True(s.LoadEffective(id).Design.Visible);
        Assert.Single(s.GetShortcuts()); // koleksiyon degismez
        s.SaveSnapshot("Yazılım"); // ayni ad: guncellenir, cogalmaz
        Assert.Single(s.GetSnapshots(true));
    }

    [Fact] public void History_KeepsLatest30()
    {
        using var s = new CollectionStore(":memory:");
        for (var i = 0; i < 35; i++) s.SaveSnapshot(null);
        Assert.Equal(CollectionStore.MaxSnapshots, s.GetSnapshots(false).Count);
        Assert.Empty(s.GetSnapshots(true));
    }

    [Fact] public void Broken_DetectsMissing_AndRemapFixesPrefix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dolap-remap-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(dir, "Proje"));
        try
        {
            using var s = new CollectionStore(":memory:");
            var cat = s.AddCategory("A", "folder", "#f00");
            s.AddShortcut(cat.Id, new("Proje", ShortcutKind.Folder, @"Z:\EskiPC\Proje", WorkingDirectory: @"Z:\EskiPC"));
            s.AddShortcut(cat.Id, new("Web", ShortcutKind.Url, "https://example.com"));
            Assert.Single(s.FindBroken());
            Assert.Equal(1, s.RemapPrefix(@"z:\eskipc", dir));
            Assert.Empty(s.FindBroken());
            Assert.Equal(dir, s.GetShortcuts().Single(x => x.Kind == ShortcutKind.Folder).WorkingDirectory);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public void Section_AndManagedReplace()
    {
        using var s = new CollectionStore(":memory:");
        var cat = s.AddCategory("A", "folder", "#f00");
        var sc = s.AddShortcut(cat.Id, new("x", ShortcutKind.App, @"C:\x.exe"))!;
        s.SetSection(sc.Id, "  Araçlar ");
        Assert.Equal("Araçlar", s.GetShortcuts().Single().Section);
        s.ReplaceManaged(cat.Id, [new("a", ShortcutKind.File, @"C:\a.txt"), new("b", ShortcutKind.File, @"C:\b.txt")]);
        Assert.Equal(["a", "b"], s.GetShortcuts(cat.Id).Select(x => x.DisplayName));
    }
}
