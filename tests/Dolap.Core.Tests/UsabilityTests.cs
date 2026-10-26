using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class UsabilityTests
{
    [Fact]
    public void SearchFindsOtherCabinetsDrawersAndTurkishWords()
    {
        using var s = new CollectionStore(":memory:");
        var cab = s.AddCabinet("Çalışma"); var c = s.AddCategory("İş Evrakları", "folder", "#123456", cab.Id);
        var item = s.AddShortcut(c.Id, new("Ölçüm Raporu", ShortcutKind.File, @"C:\Rapor.pdf"))!;
        Assert.Contains(QuickSearch.Find(s, "olcum calisma"), h => h.Shortcut?.Id == item.Id);
        Assert.Contains(QuickSearch.Find(s, "is evraklari"), h => h.Shortcut == null && h.CategoryId == c.Id);
        Assert.Single(QuickSearch.Find(s, "", c.Id));
    }

    [Fact]
    public void BulkMoveAndUndoPreserveIdsOrderAndUnrelatedWatcherChanges()
    {
        using var s = new CollectionStore(":memory:");
        var a = s.AddCategory("A", "folder", "#123456"); var b = s.AddCategory("B", "folder", "#123456");
        var first = s.AddShortcut(a.Id, new("One", ShortcutKind.File, @"C:\one.txt"))!;
        var second = s.AddShortcut(a.Id, new("Two", ShortcutKind.File, @"C:\two.txt"))!;
        s.BatchShortcuts([first.Id, second.Id], "move", b.Id);
        var watcher = s.AddShortcut(b.Id, new("Watcher", ShortcutKind.File, @"C:\watcher.txt"))!;
        Assert.Equal(3, s.GetShortcuts(b.Id).Count);
        Assert.True(s.UndoShortcutEdit());
        Assert.Equal(new[] { first, second }, s.GetShortcuts(a.Id));
        Assert.Equal(watcher, Assert.Single(s.GetShortcuts(b.Id)));
    }

    [Fact]
    public void FailedBatchIsAtomicAndDoesNotEnterHistory()
    {
        using var s = new CollectionStore(":memory:"); var c = s.AddCategory("A", "folder", "#123456");
        var first = s.AddShortcut(c.Id, new("One", ShortcutKind.File, @"C:\one.txt"))!;
        Assert.Throws<DolapException>(() => s.BatchShortcuts([first.Id, Guid.NewGuid()], "remove"));
        Assert.Equal(first, Assert.Single(s.GetShortcuts())); Assert.False(s.UndoShortcutEdit());
        Assert.Throws<InvalidOperationException>(() => s.EditShortcuts("Fail", () => { s.RemoveShortcut(first.Id); throw new InvalidOperationException(); }));
        Assert.Equal(first, Assert.Single(s.GetShortcuts())); Assert.False(s.UndoShortcutEdit());
    }

    [Fact]
    public void UndoRefusesToOverwriteLaterChanges()
    {
        using var s = new CollectionStore(":memory:"); var c = s.AddCategory("A", "folder", "#123456");
        var first = s.AddShortcut(c.Id, new("One", ShortcutKind.File, @"C:\one.txt"))!;
        s.EditShortcuts("Rename", () => s.UpdateShortcut(first.Id, new("New name", ShortcutKind.File, first.Target)));
        s.SetSection(first.Id, "Changed externally");
        Assert.Throws<DolapException>(() => s.UndoShortcutEdit());
        Assert.Equal("Changed externally", s.GetShortcuts()[0].Section);
    }

    [Fact]
    public void UndoSupportsReorderRenameSectionAndRemoval()
    {
        using var s = new CollectionStore(":memory:"); var c = s.AddCategory("A", "folder", "#123456");
        var first = s.AddShortcut(c.Id, new("One", ShortcutKind.File, @"C:\one.txt"))!;
        var second = s.AddShortcut(c.Id, new("Two", ShortcutKind.File, @"C:\two.txt"))!;
        s.EditShortcuts("Reorder", () => s.ReorderShortcuts([second.Id, first.Id]));
        s.EditShortcuts("Rename", () => s.UpdateShortcut(first.Id, new("New name", ShortcutKind.File, first.Target)));
        s.BatchShortcuts([first.Id, second.Id], "section", section: "Reports");
        s.BatchShortcuts([first.Id, second.Id], "remove");
        Assert.Empty(s.GetShortcuts());
        for (var i = 0; i < 4; i++) Assert.True(s.UndoShortcutEdit());
        Assert.Equal(new[] { first, second }, s.GetShortcuts()); Assert.False(s.UndoShortcutEdit());
    }

    [Fact]
    public void UndoHistoryIsBoundedAndNoOpDoesNotAddEntry()
    {
        using var s = new CollectionStore(":memory:"); var c = s.AddCategory("A", "folder", "#123456");
        var item = s.AddShortcut(c.Id, new("One", ShortcutKind.File, @"C:\one.txt"))!;
        for (var i = 0; i < 35; i++) { var section = i.ToString(); s.EditShortcuts("Section", () => s.SetSection(item.Id, section)); }
        s.EditShortcuts("No-op", () => { });
        var count = 0; while (s.UndoShortcutEdit()) count++;
        Assert.Equal(30, count); Assert.Equal("4", s.GetShortcuts()[0].Section);
    }

    [Fact]
    public void PreferencesRoundTripAndImportClearsUndo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dolap-ux-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            using var s = new CollectionStore(":memory:"); var c = s.AddCategory("A", "folder", "#123456");
            var item = s.AddShortcut(c.Id, new("One", ShortcutKind.File, @"C:\one.txt"))!;
            s.ToggleFavorite(item.Id); s.SaveDrawerPage(c.Id, 3);
            var file = Path.Combine(folder, "backup.json"); s.ExportJson(file);
            s.BatchShortcuts([item.Id], "remove"); s.ImportJson(file, folder);
            Assert.False(s.UndoShortcutEdit()); Assert.Equal(item.Id, Assert.Single(s.LoadUsability().Favorites));
            Assert.Equal(3, s.LoadUsability().DrawerPages[c.Id]);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void DuplicatesRespectArgumentsAndCaseSensitiveUrlPaths()
    {
        using var s = new CollectionStore(":memory:"); var c = s.AddCategory("Evrak", "folder", "#123456");
        s.AddShortcut(c.Id, new("One", ShortcutKind.File, @"C:\Folder\ONE.pdf"));
        Assert.Single(s.FindDuplicates(new("Copy", ShortcutKind.File, @"c:\folder\one.pdf")));
        Assert.Empty(s.FindDuplicates(new("Different args", ShortcutKind.File, @"C:\Folder\ONE.pdf", Arguments: "--other")));
        s.AddShortcut(c.Id, new("Site", ShortcutKind.Url, "https://example.org/Case"));
        Assert.Empty(s.FindDuplicates(new("Site", ShortcutKind.Url, "https://example.org/case")));
        Assert.Equal(c.Id, s.SuggestCategory(new("Report", ShortcutKind.File, @"C:\new.pdf"))?.Id);
    }
}
