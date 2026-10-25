using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Dolap.Core;

/// <summary>SQLite saklama + kategori/kisayol is kurallari. Her duzenleme tek islemde (transaction) yazilir.</summary>
public sealed class CollectionStore : IDisposable
{
    public const int MaxCategories = 50, MaxShortcuts = 2000, SchemaVersion = 1;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly SqliteConnection _db;
    readonly object _gate = new();
    public string DbPath { get; }

    public CollectionStore(string dbPath)
    {
        DbPath = dbPath;
        if (dbPath != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _db = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _db.Open();
        Exec("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
        Migrate();
    }

    void Migrate()
    {
        var v = Convert.ToInt32(Scalar("PRAGMA user_version"));
        if (v > SchemaVersion) throw new DolapException("Veritabani bu surumden daha yeni; uygulamayi guncelleyin.");
        if (v == SchemaVersion) return;
        Tx(() => Exec("""
            CREATE TABLE IF NOT EXISTS categories(
              id TEXT PRIMARY KEY, name TEXT NOT NULL, icon_key TEXT NOT NULL, color TEXT NOT NULL,
              order_index INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS shortcuts(
              id TEXT PRIMARY KEY,
              category_id TEXT NOT NULL REFERENCES categories(id) ON DELETE RESTRICT,
              display_name TEXT NOT NULL, kind INTEGER NOT NULL, source_link_path TEXT,
              target TEXT NOT NULL, arguments TEXT, working_directory TEXT, order_index INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_sc_cat ON shortcuts(category_id, order_index);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            PRAGMA user_version = 1;
            """));
    }

    // ---- yardimcilar ----
    void Exec(string sql, params (string, object?)[] p)
    {
        lock (_gate) { using var c = Cmd(sql, p); c.ExecuteNonQuery(); }
    }
    object? Scalar(string sql, params (string, object?)[] p)
    {
        lock (_gate) { using var c = Cmd(sql, p); return c.ExecuteScalar(); }
    }
    SqliteCommand Cmd(string sql, (string, object?)[] p)
    {
        var c = _db.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in p) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c;
    }
    List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] p)
    {
        lock (_gate)
        {
            using var c = Cmd(sql, p);
            using var r = c.ExecuteReader();
            var l = new List<T>();
            while (r.Read()) l.Add(map(r));
            return l;
        }
    }
    void Tx(Action a)
    {
        lock (_gate)
        {
            using var t = _db.BeginTransaction();
            try { a(); t.Commit(); }
            catch { t.Rollback(); throw; }
        }
    }
    static string Now() => DateTime.UtcNow.ToString("o");

    // ---- kategoriler ----
    static Category ReadCat(SqliteDataReader r) =>
        new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4));

    public List<Category> GetCategories() =>
        Query("SELECT id,name,icon_key,color,order_index FROM categories ORDER BY order_index", ReadCat);

    public Category AddCategory(string name, string iconKey, string color)
    {
        name = RequireName(name);
        if (GetCategories().Count >= MaxCategories) throw new DolapException($"En fazla {MaxCategories} kategori eklenebilir.");
        var id = Guid.NewGuid();
        var next = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(order_index)+1,0) FROM categories"));
        Exec("INSERT INTO categories VALUES($i,$n,$k,$c,$o,$t,$t)",
            ("$i", id.ToString()), ("$n", name), ("$k", iconKey), ("$c", color), ("$o", next), ("$t", Now()));
        return new Category(id, name, iconKey, color, next);
    }

    public void UpdateCategory(Guid id, string name, string iconKey, string color) =>
        Exec("UPDATE categories SET name=$n,icon_key=$k,color=$c,updated_at=$t WHERE id=$i",
            ("$i", id.ToString()), ("$n", RequireName(name)), ("$k", iconKey), ("$c", color), ("$t", Now()));

    public void ReorderCategories(IReadOnlyList<Guid> ids) => Tx(() =>
    {
        for (var i = 0; i < ids.Count; i++)
            Exec("UPDATE categories SET order_index=$o WHERE id=$i", ("$o", i), ("$i", ids[i].ToString()));
    });

    /// <summary>moveTo verilirse ogeler oraya tasinir, yoksa yalniz referanslar silinir. Kaynak dosyalara dokunulmaz.</summary>
    public void DeleteCategory(Guid id, Guid? moveTo) => Tx(() =>
    {
        if (moveTo is { } m)
        {
            var next = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(order_index)+1,0) FROM shortcuts WHERE category_id=$c", ("$c", m.ToString())));
            foreach (var s in GetShortcuts(id))
                Exec("UPDATE shortcuts SET category_id=$c,order_index=$o WHERE id=$i",
                    ("$c", m.ToString()), ("$o", next++), ("$i", s.Id.ToString()));
        }
        else Exec("DELETE FROM shortcuts WHERE category_id=$c", ("$c", id.ToString()));
        Exec("DELETE FROM categories WHERE id=$i", ("$i", id.ToString()));
    });

    // ---- kisayollar ----
    static Shortcut ReadSc(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), (ShortcutKind)r.GetInt32(3),
        r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8));
    const string ScCols = "id,category_id,display_name,kind,source_link_path,target,arguments,working_directory,order_index";

    public List<Shortcut> GetShortcuts(Guid? categoryId = null) => categoryId is { } c
        ? Query($"SELECT {ScCols} FROM shortcuts WHERE category_id=$c ORDER BY order_index", ReadSc, ("$c", c.ToString()))
        : Query($"SELECT {ScCols} FROM shortcuts ORDER BY category_id, order_index", ReadSc);

    /// <summary>Ayni kategoride ayni hedef+parametre varsa null doner (tekrar eklenmez).</summary>
    public Shortcut? AddShortcut(Guid categoryId, ShortcutDraft d, bool allowDuplicate = false)
    {
        var name = RequireName(d.DisplayName);
        if (Convert.ToInt32(Scalar("SELECT COUNT(*) FROM shortcuts")) >= MaxShortcuts)
            throw new DolapException($"En fazla {MaxShortcuts} kisayol eklenebilir.");
        if (!allowDuplicate && Convert.ToInt32(Scalar(
                "SELECT COUNT(*) FROM shortcuts WHERE category_id=$c AND LOWER(target)=LOWER($t) AND COALESCE(arguments,'')=COALESCE($a,'')",
                ("$c", categoryId.ToString()), ("$t", d.Target), ("$a", d.Arguments))) > 0)
            return null;
        var next = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(order_index)+1,0) FROM shortcuts WHERE category_id=$c", ("$c", categoryId.ToString())));
        var s = new Shortcut(Guid.NewGuid(), categoryId, name, d.Kind, d.SourceLinkPath, d.Target, d.Arguments, d.WorkingDirectory, next);
        Insert(s);
        return s;
    }

    void Insert(Shortcut s) => Exec($"INSERT INTO shortcuts({ScCols}) VALUES($i,$c,$n,$k,$l,$t,$a,$w,$o)",
        ("$i", s.Id.ToString()), ("$c", s.CategoryId.ToString()), ("$n", s.DisplayName), ("$k", (int)s.Kind),
        ("$l", s.SourceLinkPath), ("$t", s.Target), ("$a", s.Arguments), ("$w", s.WorkingDirectory), ("$o", s.OrderIndex));

    public void UpdateShortcut(Guid id, ShortcutDraft d) =>
        Exec("UPDATE shortcuts SET display_name=$n,kind=$k,target=$t,source_link_path=$l,arguments=$a,working_directory=$w WHERE id=$i",
            ("$i", id.ToString()), ("$n", RequireName(d.DisplayName)), ("$k", (int)d.Kind), ("$t", d.Target),
            ("$l", d.SourceLinkPath), ("$a", d.Arguments), ("$w", d.WorkingDirectory));

    public void MoveShortcut(Guid id, Guid toCategory)
    {
        var next = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(order_index)+1,0) FROM shortcuts WHERE category_id=$c", ("$c", toCategory.ToString())));
        Exec("UPDATE shortcuts SET category_id=$c,order_index=$o WHERE id=$i", ("$c", toCategory.ToString()), ("$o", next), ("$i", id.ToString()));
    }

    public void ReorderShortcuts(IReadOnlyList<Guid> ids) => Tx(() =>
    {
        for (var i = 0; i < ids.Count; i++)
            Exec("UPDATE shortcuts SET order_index=$o WHERE id=$i", ("$o", i), ("$i", ids[i].ToString()));
    });

    public Shortcut? RemoveShortcut(Guid id)
    {
        var s = Query($"SELECT {ScCols} FROM shortcuts WHERE id=$i", ReadSc, ("$i", id.ToString())).FirstOrDefault();
        if (s != null) Exec("DELETE FROM shortcuts WHERE id=$i", ("$i", id.ToString()));
        return s;
    }

    /// <summary>Geri al: silinen kaydi ayni kimlik ve sirayla geri koyar.</summary>
    public void Restore(Shortcut s) => Insert(s);

    // ---- ayarlar ----
    public AppSettings LoadSettings()
    {
        var v = Scalar("SELECT value FROM settings WHERE key='app'") as string;
        if (v == null) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(v) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }
    public void SaveSettings(AppSettings s) =>
        Exec("INSERT INTO settings VALUES('app',$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$v", JsonSerializer.Serialize(s)));

    // ---- yedek / disa-iceri aktarma ----
    public void BackupTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path)) File.Delete(path);
        lock (_gate)
        {
            using var dst = new SqliteConnection($"Data Source={path};Pooling=False");
            dst.Open();
            _db.BackupDatabase(dst);
        }
    }

    /// <summary>Gunde bir yedek alir, son 7 yedegi tutar.</summary>
    public void DailyBackup(string dir)
    {
        var f = Path.Combine(dir, $"dolap-{DateTime.Now:yyyyMMdd}.db");
        if (!File.Exists(f)) BackupTo(f);
        foreach (var old in Directory.GetFiles(dir, "dolap-*.db").OrderByDescending(x => x).Skip(7)) File.Delete(old);
    }

    sealed record Pack(int SchemaVersion, List<Category> Categories, List<Shortcut> Shortcuts, AppSettings Settings);

    public void ExportJson(string path) => File.WriteAllText(path,
        JsonSerializer.Serialize(new Pack(SchemaVersion, GetCategories(), GetShortcuts(), LoadSettings()), Json));

    /// <summary>Paketi dogrular; (kategori, kisayol) sayisini doner. Mevcut veriye dokunmaz.</summary>
    public static (int Categories, int Shortcuts) PreviewJson(string path)
    {
        var p = ReadPack(path);
        return (p.Categories.Count, p.Shortcuts.Count);
    }

    static Pack ReadPack(string path)
    {
        if (new FileInfo(path).Length > 20_000_000) throw new DolapException("Yedek dosyasi cok buyuk.");
        Pack? p;
        try { p = JsonSerializer.Deserialize<Pack>(File.ReadAllText(path)); }
        catch (JsonException) { throw new DolapException("Yedek dosyasi okunamadi veya bozuk."); }
        if (p is null || p.SchemaVersion != SchemaVersion) throw new DolapException("Yedek surumu desteklenmiyor.");
        var ids = p.Categories.Select(c => c.Id).ToHashSet();
        if (p.Categories.Count > MaxCategories || p.Shortcuts.Count > MaxShortcuts
            || p.Shortcuts.Any(s => !ids.Contains(s.CategoryId)))
            throw new DolapException("Yedek icerigi gecersiz.");
        return p;
    }

    /// <summary>Mevcut veriyi once yedekler (backupDir), sonra tek islemde degistirir.</summary>
    public void ImportJson(string path, string backupDir)
    {
        var p = ReadPack(path);
        BackupTo(Path.Combine(backupDir, $"oncesi-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
        Tx(() =>
        {
            Exec("DELETE FROM shortcuts; DELETE FROM categories;");
            foreach (var c in p.Categories)
                Exec("INSERT INTO categories VALUES($i,$n,$k,$c,$o,$t,$t)", ("$i", c.Id.ToString()), ("$n", c.Name),
                    ("$k", c.IconKey), ("$c", c.Color), ("$o", c.OrderIndex), ("$t", Now()));
            foreach (var s in p.Shortcuts) Insert(s);
        });
        SaveSettings(p.Settings);
    }

    static string RequireName(string n)
    {
        n = (n ?? "").Trim();
        if (n.Length == 0) throw new DolapException("Ad bos olamaz.");
        return n.Length > 100 ? n[..100] : n;
    }

    public void Dispose() => _db.Dispose();
}
