using System.Text.Json.Nodes;
using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class ReviewRegressionTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dolap-regression-" + Guid.NewGuid());
    public ReviewRegressionTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void RemapHonorsDirectoryBoundaryAndUpdatesLinkedFolders()
    {
        using var s = new CollectionStore(":memory:");
        var cat = s.AddCategory("Files", "folder", "#123456");
        s.SetLinkedFolder(cat.Id, @"C:\Work\Linked");
        s.AddShortcut(cat.Id, new("Inside", ShortcutKind.File, @"C:\Work\a.txt", WorkingDirectory: @"C:\Work"));
        s.AddShortcut(cat.Id, new("Sibling", ShortcutKind.File, @"C:\Work-old\a.txt"));
        var smart = s.AddCategory("Smart", "folder", "#123456"); s.SetLinkedFolder(smart.Id, "smart:week");
        Assert.Equal(1, s.RemapPrefix(@"c:\work\", @"D:\New\"));
        var items = s.GetShortcuts();
        Assert.Equal(@"D:\New\a.txt", items.Single(x => x.DisplayName == "Inside").Target);
        Assert.Equal(@"D:\New", items.Single(x => x.DisplayName == "Inside").WorkingDirectory);
        Assert.Equal(@"C:\Work-old\a.txt", items.Single(x => x.DisplayName == "Sibling").Target);
        Assert.Equal(@"D:\New\Linked", s.GetCategories().Single(c => c.Id == cat.Id).LinkedFolder);
        Assert.Equal("smart:week", s.GetCategories().Single(c => c.Id == smart.Id).LinkedFolder);
    }

    [Theory]
    [InlineData(@"C:\", @"D:\", @"C:\folder\a.txt", @"D:\folder\a.txt")]
    [InlineData(@"\\server\share", @"D:\", @"\\server\share\a.txt", @"D:\a.txt")]
    public void RemapSupportsDriveRootsAndUnc(string from, string to, string before, string after)
    {
        using var s = new CollectionStore(":memory:");
        var c = s.AddCategory("A", "f", "#123456");
        s.AddShortcut(c.Id, new("A", ShortcutKind.File, before));
        s.RemapPrefix(from, to);
        Assert.Equal(after, s.GetShortcuts().Single().Target);
        Assert.Throws<DolapException>(() => s.RemapPrefix(from, ""));
    }

    [Fact]
    public void WorkSetsRoundTripWhileOldHistoryIsCleared()
    {
        using var source = new CollectionStore(":memory:");
        var id = source.FirstCabinetId();
        source.SaveSettings(source.LoadSettings() with { FreeX = .25, Design = new() { Visible = false } });
        var set = source.SaveSnapshot("Work"); source.SaveSnapshot(null);
        source.SaveSettings(source.LoadSettings() with { FreeX = .8, Design = new() { Visible = true } });
        var path = Path.Combine(dir, "pack.json"); source.ExportJson(path);
        using var target = new CollectionStore(":memory:");
        target.SaveSnapshot("Old"); target.SaveSnapshot(null);
        target.ImportJson(path, Path.Combine(dir, "backups"));
        Assert.Equal(set.Id, target.GetSnapshots(true).Single().Id);
        Assert.Empty(target.GetSnapshots(false));
        Assert.Equal(1, target.RestoreSnapshot(set.Id));
        Assert.Equal(.25, target.LoadEffective(id).FreeX);
        Assert.False(target.LoadEffective(id).Design.Visible);
        var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); legacy.Remove("WorkSets");
        File.WriteAllText(path, legacy.ToJsonString());
        target.ImportJson(path, Path.Combine(dir, "legacy-backup"));
        Assert.Empty(target.GetSnapshots(true));
        Assert.Single(target.GetCabinets());
    }

    [Fact]
    public void BrokenLinkChecksBothFileAndLocalTargetButAllowsShellAndWeb()
    {
        var link = Path.Combine(dir, "existing.lnk"); File.WriteAllText(link, "existence fixture");
        var target = Path.Combine(dir, "target.txt");
        using var s = new CollectionStore(":memory:");
        var c = s.AddCategory("A", "f", "#123456");
        var broken = s.AddShortcut(c.Id, new("Broken", ShortcutKind.Lnk, target, SourceLinkPath: link))!;
        Assert.Single(s.FindBroken()); Assert.True(ShortcutHealth.IsBroken(broken));
        File.WriteAllText(target, "test"); Assert.Empty(s.FindBroken());
        Assert.False(ShortcutHealth.IsBroken(broken with { Target = "shell:Downloads" }));
        Assert.False(ShortcutHealth.IsBroken(broken with { Target = "https://example.com" }));
        File.Delete(link); Assert.Single(s.FindBroken());
    }
}
