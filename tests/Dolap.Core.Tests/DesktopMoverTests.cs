using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class DesktopMoverTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "dolap-mover-" + Guid.NewGuid().ToString("N"));
    public DesktopMoverTests() => Directory.CreateDirectory(Path.Combine(_root, "Desktop"));
    public void Dispose() => Directory.Delete(_root, true);
    string Desk(string name, string text = "x") { var p = Path.Combine(_root, "Desktop", name); File.WriteAllText(p, text); return p; }
    static AssistantAction A(string kind, string name = "", params string[] ids) => new(kind, "", "", name, "", ids.ToList());

    [Fact]
    public void Move_NeverOverwrites_AndUndoRestores()
    {
        var folder = DesktopMover.DrawerFolder(Path.Combine(_root, "Dolap"), "İş: Fatura?");
        Assert.EndsWith("İş_ Fatura_", folder);
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "a.pdf"), "eski");
        var m = DesktopMover.Move(Desk("a.pdf", "yeni"), folder)!;
        Assert.Equal(Path.Combine(folder, "a (2).pdf"), m.To);
        Assert.Equal("eski", File.ReadAllText(Path.Combine(folder, "a.pdf")));
        Assert.False(File.Exists(m.From));
        DesktopMover.Undo(m);
        Assert.Equal("yeni", File.ReadAllText(m.From)); Assert.False(File.Exists(m.To));
    }

    [Fact]
    public void Move_SkipsPartialDownloads_LockedAndMissingFiles()
    {
        var folder = Path.Combine(_root, "Dolap", "X");
        Assert.Null(DesktopMover.Move(Desk("kurulum.zip.crdownload"), folder));
        Assert.Null(DesktopMover.Move(Path.Combine(_root, "Desktop", "yok.txt"), folder));
        var locked = Desk("acik.docx");
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.Null(DesktopMover.Move(locked, folder));
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void ShortcutUndo_RunsFileRevertBeforeRestoringRecords()
    {
        using var s = new CollectionStore(":memory:");
        var inbox = s.AddCategory("Gelenler", "folder", "#123456"); var docs = s.AddCategory("Belgeler", "folder", "#123456");
        var path = Desk("rapor.pdf");
        var item = s.AddShortcut(inbox.Id, new("rapor.pdf", ShortcutKind.File, path))!;
        s.EditShortcuts("Taşı", () =>
        {
            var m = DesktopMover.Move(path, Path.Combine(_root, "Dolap", "Belgeler"))!;
            s.MoveShortcut(item.Id, docs.Id); s.UpdateShortcut(item.Id, new("rapor.pdf", ShortcutKind.File, m.To));
            return () => DesktopMover.Undo(m);
        });
        Assert.False(File.Exists(path));
        Assert.True(s.UndoShortcutEdit());
        Assert.True(File.Exists(path)); Assert.Equal(item, Assert.Single(s.GetShortcuts()));
    }

    [Fact]
    public void Checkpoint_RestoresCabinetsDrawersAndSettings()
    {
        using var s = new CollectionStore(":memory:");
        var before = s.AssistantState();
        using var cp = s.Checkpoint();
        s.ApplyAssistant(new("", [A("create_cabinet", "Yeni"), new("set_setting", s.FirstCabinetId().ToString(), "", "ThemeId", "blocks", [])]));
        Assert.NotEqual(before, s.AssistantState());
        s.Restore(cp);
        Assert.Equal(before, s.AssistantState());
    }

    [Fact]
    public void Confirmation_RequiredForRiskyOrLargePlans_NotForSafeOnes()
    {
        Assert.False(AssistantProtocol.NeedsConfirmation(new("", [A("create_drawer", "Fatura"), A("move_shortcuts", "", "a", "b")])));
        Assert.True(AssistantProtocol.NeedsConfirmation(new("", [A("remove_shortcuts", "", "a")])));
        Assert.True(AssistantProtocol.NeedsConfirmation(new("", [A("launch", "", "a")])));
        Assert.True(AssistantProtocol.NeedsConfirmation(new("", [new("set_setting", "", "", "StartWithWindows", "true", [])])));
        Assert.True(AssistantProtocol.NeedsConfirmation(new("", [A("move_shortcuts", "", Enumerable.Range(0, 16).Select(i => i.ToString()).ToArray())])));
    }
}
