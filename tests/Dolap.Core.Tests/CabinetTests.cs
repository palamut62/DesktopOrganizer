using Dolap.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dolap.Core.Tests;

public class CabinetTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dolap-cab-" + Guid.NewGuid());
    public CabinetTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact] public void FreshStore_HasOneDefaultCabinet()
    {
        using var s = new CollectionStore(":memory:");
        Assert.Single(s.GetCabinets());
    }

    [Fact] public void AddCabinet_UsesOppositeEdge_AndKeepsOwnLayoutAndTheme()
    {
        using var s = new CollectionStore(":memory:");
        var first = s.GetCabinets()[0];
        var second = s.AddCabinet(themeId: "blocks");
        Assert.NotEqual(first.Edge, second.Edge);
        s.SaveEffective(second.Id, s.LoadEffective(second.Id) with { Scale = 1.25, OpenDirection = OpenDirection.Right, ThemeId = "blocks" });
        var e1 = s.LoadEffective(first.Id); var e2 = s.LoadEffective(second.Id);
        Assert.Equal("metal", e1.ThemeId); Assert.Equal("blocks", e2.ThemeId);
        Assert.Equal(1.0, e1.Scale); Assert.Equal(1.25, e2.Scale);
        Assert.Equal(OpenDirection.Right, e2.OpenDirection);
    }

    [Fact] public void Categories_AreScopedPerCabinet_AndMovable()
    {
        using var s = new CollectionStore(":memory:");
        var a = s.GetCabinets()[0]; var b = s.AddCabinet();
        var c1 = s.AddCategory("A", "folder", "#f00");          // ilk dolaba
        s.AddCategory("B", "folder", "#0f0", b.Id);
        Assert.Single(s.GetCategories(a.Id)); Assert.Single(s.GetCategories(b.Id)); Assert.Equal(2, s.GetCategories().Count);
        s.MoveCategoryToCabinet(c1.Id, b.Id);
        Assert.Empty(s.GetCategories(a.Id)); Assert.Equal(2, s.GetCategories(b.Id).Count);
    }

    [Fact] public void DeleteCabinet_MovesCategories_LastCannotBeDeleted()
    {
        using var s = new CollectionStore(":memory:");
        var a = s.GetCabinets()[0]; var b = s.AddCabinet();
        var cat = s.AddCategory("X", "folder", "#f00", b.Id);
        s.AddShortcut(cat.Id, new("n", ShortcutKind.App, "C:\\x.exe"));
        s.DeleteCabinet(b.Id);
        Assert.Single(s.GetCabinets());
        Assert.Single(s.GetCategories(a.Id));
        Assert.Single(s.GetShortcuts());
        Assert.Throws<DolapException>(() => s.DeleteCabinet(a.Id));
    }

    [Fact] public void MaxCabinets_Enforced()
    {
        using var s = new CollectionStore(":memory:");
        for (var i = 1; i < CollectionStore.MaxCabinets; i++) s.AddCabinet();
        Assert.Throws<DolapException>(() => s.AddCabinet());
    }

    [Fact] public void MigratesV1Database_ToDefaultCabinet_KeepingData()
    {
        var db = Path.Combine(_dir, "v1.db");
        using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE categories(id TEXT PRIMARY KEY, name TEXT NOT NULL, icon_key TEXT NOT NULL, color TEXT NOT NULL, order_index INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE shortcuts(id TEXT PRIMARY KEY, category_id TEXT NOT NULL REFERENCES categories(id), display_name TEXT NOT NULL, kind INTEGER NOT NULL, source_link_path TEXT, target TEXT NOT NULL, arguments TEXT, working_directory TEXT, order_index INTEGER NOT NULL);
                CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO categories VALUES('11111111-1111-1111-1111-111111111111','Eski','folder','#f00',0,'t','t');
                INSERT INTO settings VALUES('app','{"ThemeId":"blocks","Scale":1.25,"Edge":0}');
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
        }
        using var s = new CollectionStore(db);
        var cab = Assert.Single(s.GetCabinets());
        Assert.Equal("blocks", cab.ThemeId); Assert.Equal(DockEdge.Left, cab.Edge); Assert.Equal(1.25, cab.Scale);
        Assert.Equal(cab.Id, Assert.Single(s.GetCategories()).CabinetId);
        Assert.True(Directory.GetFiles(Path.Combine(_dir, "backups"), "sema-v1-*.db").Length == 1); // yukseltme oncesi yedek
    }

    [Fact] public void ExportImport_RoundTrips_Cabinets_AndAcceptsV1Pack()
    {
        var dir = Path.Combine(_dir, "bk");
        using var s = new CollectionStore(Path.Combine(_dir, "a.db"));
        var b = s.AddCabinet("Ikinci", "blocks");
        s.AddCategory("K", "folder", "#0f0", b.Id);
        var pack = Path.Combine(_dir, "p.json"); s.ExportJson(pack);
        s.DeleteCabinet(b.Id);
        s.ImportJson(pack, dir);
        Assert.Equal(2, s.GetCabinets().Count);
        Assert.Equal("blocks", s.GetCabinets()[1].ThemeId);

        var v1 = Path.Combine(_dir, "v1.json");
        File.WriteAllText(v1, """{"SchemaVersion":1,"Categories":[{"Id":"22222222-2222-2222-2222-222222222222","Name":"Z","IconKey":"f","Color":"#000","OrderIndex":0}],"Shortcuts":[],"Settings":{"ThemeId":"blocks"}}""");
        s.ImportJson(v1, dir);
        Assert.Single(s.GetCabinets());
        Assert.Equal(s.GetCabinets()[0].Id, Assert.Single(s.GetCategories()).CabinetId);
    }
}

public class CabinetViewTests
{
    [Fact] public void YawPitchAndOpenDrawer_PersistPerCabinet_AndAreClamped()
    {
        using var s = new CollectionStore(":memory:");
        var id = s.GetCabinets()[0].Id;
        var cat = s.AddCategory("A", "folder", "#f00");
        s.SaveEffective(id, s.LoadEffective(id) with { Yaw = 30, Pitch = -10, OpenCategory = cat.Id });
        var e = s.LoadEffective(id);
        Assert.Equal(30, e.Yaw); Assert.Equal(-10, e.Pitch); Assert.Equal(cat.Id, e.OpenCategory);
        s.SaveEffective(id, e with { Yaw = 500, Pitch = -500 });
        Assert.Equal(CollectionStore.MaxYaw, s.LoadEffective(id).Yaw);
        Assert.Equal(-CollectionStore.MaxPitch, s.LoadEffective(id).Pitch);
    }

    [Fact] public void MigratesV2Database_AddingViewColumns()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dolap-v2-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "v2.db");
        using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE categories(id TEXT PRIMARY KEY, name TEXT NOT NULL, icon_key TEXT NOT NULL, color TEXT NOT NULL, order_index INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, cabinet_id TEXT);
                CREATE TABLE shortcuts(id TEXT PRIMARY KEY, category_id TEXT NOT NULL, display_name TEXT NOT NULL, kind INTEGER NOT NULL, source_link_path TEXT, target TEXT NOT NULL, arguments TEXT, working_directory TEXT, order_index INTEGER NOT NULL);
                CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE cabinets(id TEXT PRIMARY KEY, name TEXT NOT NULL, theme_id TEXT NOT NULL, edge INTEGER NOT NULL, monitor_device TEXT, scale REAL NOT NULL, free_x REAL NOT NULL, free_y REAL NOT NULL, auto_hide INTEGER NOT NULL, always_on_top INTEGER NOT NULL, open_direction INTEGER NOT NULL, order_index INTEGER NOT NULL);
                INSERT INTO cabinets VALUES('33333333-3333-3333-3333-333333333333','Eski','metal',1,NULL,1,0.5,0.1,0,0,0,0);
                PRAGMA user_version = 2;
                """;
            cmd.ExecuteNonQuery();
        }
        using (var s = new CollectionStore(db))
        {
            var cab = Assert.Single(s.GetCabinets());
            Assert.Equal("Eski", cab.Name); Assert.Equal(0, cab.Yaw); Assert.Null(cab.OpenCategory);
        }
        SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch { }
    }
}
