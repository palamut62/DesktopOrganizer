using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class LinkedFolderTests
{
    const string Root = @"C:\Proje";
    static ShortcutDraft F(string name, ShortcutKind kind = ShortcutKind.File) => new(name, kind, Path.Combine(Root, name));

    [Fact] public void Sync_AddsNew_RemovesVanished_KeepsManualAndNested()
    {
        using var s = new CollectionStore(":memory:");
        var cat = s.AddCategory("Proje", "folder", "#f00");
        s.SetLinkedFolder(cat.Id, Root);
        s.AddShortcut(cat.Id, new("Elle", ShortcutKind.App, @"D:\Araclar\x.exe"));           // klasor disi, dokunulmaz
        s.AddShortcut(cat.Id, new("Alt", ShortcutKind.File, @"C:\Proje\alt\derin.txt"));     // alt klasor, yonetilmez
        Assert.Equal((2, 0), s.SyncLinkedFolder(cat.Id, Root, [F("a.txt"), F("b.txt")]));
        Assert.Equal((1, 1), s.SyncLinkedFolder(cat.Id, Root, [F("b.txt"), F("c", ShortcutKind.Folder)]));
        var names = s.GetShortcuts(cat.Id).Select(x => x.DisplayName).Order().ToList();
        Assert.Equal(["Alt", "b.txt", "c", "Elle"], names);
        Assert.Equal(Root, s.GetCategories().Single().LinkedFolder);
    }

    [Fact] public void Sync_IsIdempotent_AndCaseInsensitive()
    {
        using var s = new CollectionStore(":memory:");
        var cat = s.AddCategory("P", "folder", "#f00");
        s.SyncLinkedFolder(cat.Id, Root, [F("A.TXT")]);
        Assert.Equal((0, 0), s.SyncLinkedFolder(cat.Id, Root.ToLowerInvariant(), [new("a", ShortcutKind.File, @"c:\proje\a.txt")]));
        Assert.Single(s.GetShortcuts(cat.Id));
    }

    [Fact] public void Sync_RespectsItemCap()
    {
        using var s = new CollectionStore(":memory:");
        var cat = s.AddCategory("P", "folder", "#f00");
        var many = Enumerable.Range(0, CollectionStore.MaxLinkedItems + 30).Select(i => F($"f{i}.txt")).ToList();
        Assert.Equal(CollectionStore.MaxLinkedItems, s.SyncLinkedFolder(cat.Id, Root, many).Added);
    }

    [Fact] public void Unlink_ClearsFolder_KeepsShortcuts()
    {
        using var s = new CollectionStore(":memory:");
        var cat = s.AddCategory("P", "folder", "#f00");
        s.SetLinkedFolder(cat.Id, Root); s.SyncLinkedFolder(cat.Id, Root, [F("a.txt")]);
        s.SetLinkedFolder(cat.Id, null);
        Assert.Null(s.GetCategories().Single().LinkedFolder);
        Assert.Single(s.GetShortcuts(cat.Id));
    }
}
