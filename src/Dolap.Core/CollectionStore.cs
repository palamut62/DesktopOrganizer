using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Dolap.Core;

/// <summary>SQLite saklama + kategori/kisayol is kurallari. Her duzenleme tek islemde (transaction) yazilir.</summary>
public sealed class CollectionStore : IDisposable
{
    public const int MaxCategories = 50, MaxShortcuts = 2000, MaxCabinets = 6, SchemaVersion = 5, MaxLinkedItems = 200, MaxSnapshots = 30;
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
        if (v < 2)
        {
        if (v == 0)
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
        else if (DbPath != ":memory:") // sema yukseltmeden once ayri yedek
            BackupTo(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DbPath))!, "backups", $"sema-v{v}-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
        Tx(() =>
        {
            Exec("""
                CREATE TABLE IF NOT EXISTS cabinets(
                  id TEXT PRIMARY KEY, name TEXT NOT NULL, theme_id TEXT NOT NULL, edge INTEGER NOT NULL, monitor_device TEXT,
                  scale REAL NOT NULL, free_x REAL NOT NULL, free_y REAL NOT NULL, auto_hide INTEGER NOT NULL,
                  always_on_top INTEGER NOT NULL, open_direction INTEGER NOT NULL, order_index INTEGER NOT NULL,
                  yaw REAL NOT NULL DEFAULT 0, pitch REAL NOT NULL DEFAULT 0, open_category TEXT, design TEXT);
                ALTER TABLE categories ADD COLUMN cabinet_id TEXT;
                """);
            var legacy = LoadGlobalSettings(); // v1'de yerlesim ve tema ayarlarda duruyordu
            var cab = FromSettings(Guid.NewGuid(), "Dolap 1", legacy, 0);
            InsertCabinet(cab);
            Exec("UPDATE categories SET cabinet_id=$c", ("$c", cab.Id.ToString()));
            Exec("PRAGMA user_version = 2");
        });
        }
        else if (DbPath != ":memory:")
            BackupTo(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DbPath))!, "backups", $"sema-v{v}-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
        // v3: dolap basina 3B donus ve son acik cekmece
        if (v < 3)
            Tx(() =>
            {
                if (v == 2)
                    Exec("ALTER TABLE cabinets ADD COLUMN yaw REAL NOT NULL DEFAULT 0; ALTER TABLE cabinets ADD COLUMN pitch REAL NOT NULL DEFAULT 0; ALTER TABLE cabinets ADD COLUMN open_category TEXT;");
                Exec("PRAGMA user_version = 3");
            });
        // v4: klasore bagli cekmece
        if (v < 4)
            Tx(() =>
            {
                Exec("ALTER TABLE categories ADD COLUMN linked_folder TEXT");
                Exec("PRAGMA user_version = 4");
            });
        // v5: dolap tasarimi, cekmece bolumleri, yerlesim anlik goruntuleri (calisma setleri + gecmis)
        Tx(() =>
        {
            if (!HasColumn("cabinets", "design")) Exec("ALTER TABLE cabinets ADD COLUMN design TEXT");
            if (!HasColumn("shortcuts", "section")) Exec("ALTER TABLE shortcuts ADD COLUMN section TEXT");
            Exec("CREATE TABLE IF NOT EXISTS snapshots(id TEXT PRIMARY KEY, name TEXT, created_at TEXT NOT NULL, data TEXT NOT NULL)");
            Exec("PRAGMA user_version = 5");
        });
    }

    // ---- yardimcilar ----
    bool HasColumn(string table, string col) =>
        Query($"PRAGMA table_info({table})", r => r.GetString(1)).Any(c => c.Equals(col, StringComparison.OrdinalIgnoreCase));
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
        new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), Guid.Parse(r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6));
    const string CatCols = "id,name,icon_key,color,order_index,cabinet_id,linked_folder";

    /// <summary>cabinetId verilirse yalniz o dolabin kategorileri; yoksa hepsi.</summary>
    public List<Category> GetCategories(Guid? cabinetId = null) => cabinetId is { } c
        ? Query($"SELECT {CatCols} FROM categories WHERE cabinet_id=$c ORDER BY order_index", ReadCat, ("$c", c.ToString()))
        : Query($"SELECT {CatCols} FROM categories ORDER BY order_index", ReadCat);

    /// <summary>cabinetId verilmezse ilk dolaba eklenir.</summary>
    public Category AddCategory(string name, string iconKey, string color, Guid? cabinetId = null)
    {
        name = RequireName(name);
        var cab = cabinetId ?? FirstCabinetId();
        if (GetCategories().Count >= MaxCategories) throw new DolapException($"En fazla {MaxCategories} kategori eklenebilir.");
        var id = Guid.NewGuid();
        var next = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(order_index)+1,0) FROM categories WHERE cabinet_id=$c", ("$c", cab.ToString())));
        Exec("INSERT INTO categories(id,name,icon_key,color,order_index,created_at,updated_at,cabinet_id) VALUES($i,$n,$k,$c,$o,$t,$t,$b)",
            ("$i", id.ToString()), ("$n", name), ("$k", iconKey), ("$c", color), ("$o", next), ("$t", Now()), ("$b", cab.ToString()));
        return new Category(id, name, iconKey, color, next, cab);
    }

    public void SetLinkedFolder(Guid categoryId, string? folder) =>
        Exec("UPDATE categories SET linked_folder=$f, updated_at=$t WHERE id=$i",
            ("$i", categoryId.ToString()), ("$f", string.IsNullOrWhiteSpace(folder) ? null : folder), ("$t", Now()));

    /// <summary>Bagli klasorun guncel ust duzey ogelerini (taslak) cekmeceye yansitir, tek islemde:
    /// klasorde olup cekmecede olmayanlar eklenir; o klasorden gelip artik bulunmayanlar cekmeceden cikarilir.
    /// Kullanicinin elle ekledigi baska konumdaki kisayollara ve hicbir dosyaya dokunulmaz. (eklenen, cikarilan) doner.</summary>
    public (int Added, int Removed) SyncLinkedFolder(Guid categoryId, string folder, IReadOnlyList<ShortcutDraft> entries)
    {
        var added = 0; var removed = 0;
        Tx(() =>
        {
            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            static string Src(Shortcut s) => s.SourceLinkPath ?? s.Target;
            static string Key(string p) => Path.GetFullPath(p).ToLowerInvariant();
            var existing = GetShortcuts(categoryId)
                .Where(s => s.Kind != ShortcutKind.Url && Src(s).StartsWith(root, StringComparison.OrdinalIgnoreCase)
                            && !Src(s)[root.Length..].Contains('\\'))
                .ToList();
            var want = entries.Take(MaxLinkedItems).ToDictionary(d => Key(d.SourceLinkPath ?? d.Target), d => d);
            foreach (var s in existing.Where(s => !want.ContainsKey(Key(Src(s)))))
            { Exec("DELETE FROM shortcuts WHERE id=$i", ("$i", s.Id.ToString())); removed++; }
            var have = existing.Select(s => Key(Src(s))).ToHashSet();
            foreach (var (k, d) in want)
                if (!have.Contains(k) && AddShortcut(categoryId, d) != null) added++;
        });
        return (added, removed);
    }

    public void MoveCategoryToCabinet(Guid categoryId, Guid cabinetId)
    {
        var next = Convert.ToInt32(Scalar("SELECT COALESCE(MAX(order_index)+1,0) FROM categories WHERE cabinet_id=$c", ("$c", cabinetId.ToString())));
        Exec("UPDATE categories SET cabinet_id=$c,order_index=$o WHERE id=$i", ("$c", cabinetId.ToString()), ("$o", next), ("$i", categoryId.ToString()));
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
        r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8), r.IsDBNull(9) ? null : r.GetString(9));
    const string ScCols = "id,category_id,display_name,kind,source_link_path,target,arguments,working_directory,order_index,section";

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

    void Insert(Shortcut s) => Exec($"INSERT INTO shortcuts({ScCols}) VALUES($i,$c,$n,$k,$l,$t,$a,$w,$o,$s)",
        ("$i", s.Id.ToString()), ("$c", s.CategoryId.ToString()), ("$n", s.DisplayName), ("$k", (int)s.Kind),
        ("$l", s.SourceLinkPath), ("$t", s.Target), ("$a", s.Arguments), ("$w", s.WorkingDirectory), ("$o", s.OrderIndex), ("$s", s.Section));

    /// <summary>Cekmece ici bolum (alt sekme) adi; bos = bolumsuz.</summary>
    public void SetSection(Guid id, string? section) =>
        Exec("UPDATE shortcuts SET section=$s WHERE id=$i", ("$i", id.ToString()), ("$s", string.IsNullOrWhiteSpace(section) ? null : section.Trim()));

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

    // ---- dolaplar ----
    static Cabinet ReadCab(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), (DockEdge)r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4),
        r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetInt32(8) != 0, r.GetInt32(9) != 0, (OpenDirection)r.GetInt32(10), r.GetInt32(11),
        r.GetDouble(12), r.GetDouble(13), r.IsDBNull(14) ? null : Guid.Parse(r.GetString(14)), ReadDesign(r.IsDBNull(15) ? null : r.GetString(15)));
    const string CabCols = "id,name,theme_id,edge,monitor_device,scale,free_x,free_y,auto_hide,always_on_top,open_direction,order_index,yaw,pitch,open_category,design";
    static CabinetDesign ReadDesign(string? json)
    {
        if (json == null) return new();
        try { return JsonSerializer.Deserialize<CabinetDesign>(json) ?? new(); } catch (JsonException) { return new(); }
    }

    public List<Cabinet> GetCabinets() => Query($"SELECT {CabCols} FROM cabinets ORDER BY order_index", ReadCab);
    public Guid FirstCabinetId() => Guid.Parse((string)Scalar("SELECT id FROM cabinets ORDER BY order_index LIMIT 1")!);

    void InsertCabinet(Cabinet c) => Exec($"INSERT INTO cabinets({CabCols}) VALUES($i,$n,$t,$e,$m,$s,$x,$y,$a,$o,$d,$r,$w,$p,$c,$g)",
        ("$i", c.Id.ToString()), ("$n", c.Name), ("$t", c.ThemeId), ("$e", (int)c.Edge), ("$m", c.MonitorDevice), ("$s", c.Scale),
        ("$x", c.FreeX), ("$y", c.FreeY), ("$a", c.AutoHide ? 1 : 0), ("$o", c.AlwaysOnTop ? 1 : 0), ("$d", (int)c.OpenDirection), ("$r", c.OrderIndex),
        ("$w", c.Yaw), ("$p", c.Pitch), ("$c", c.OpenCategory?.ToString()), ("$g", JsonSerializer.Serialize(c.Design ?? new())));

    static Cabinet FromSettings(Guid id, string name, AppSettings s, int order) => new(id, name, s.ThemeId, s.Edge, s.MonitorDevice,
        Math.Clamp(s.Scale, 0.75, 1.5), Math.Clamp(s.FreeX, 0, 1), Math.Clamp(s.FreeY, 0, 1), s.AutoHide, s.AlwaysOnTop, s.OpenDirection, order,
        Math.Clamp(s.Yaw, -MaxYaw, MaxYaw), Math.Clamp(s.Pitch, -MaxPitch, MaxPitch), s.OpenCategory, s.Design);
    /// <summary>On yuzdeki kontroller okunur kalsin diye donus sinirli.</summary>
    public const double MaxYaw = 55, MaxPitch = 25;

    /// <summary>Yeni dolap: mevcut son dolabin karsi kenarina, ayni temayla yerlesir.</summary>
    public Cabinet AddCabinet(string? name = null, string? themeId = null)
    {
        var all = GetCabinets();
        if (all.Count >= MaxCabinets) throw new DolapException($"En fazla {MaxCabinets} dolap eklenebilir.");
        var last = all.Last();
        var edge = last.Edge == DockEdge.Right ? DockEdge.Left : DockEdge.Right;
        var c = new Cabinet(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? $"Dolap {all.Count + 1}" : RequireName(name), themeId ?? last.ThemeId, edge,
            last.MonitorDevice, last.Scale, 0.5, 0.1, false, false, OpenDirection.Auto, last.OrderIndex + 1);
        InsertCabinet(c);
        return c;
    }

    public void UpdateCabinet(Cabinet c)
    {
        c = c with { Name = RequireName(c.Name), Scale = Math.Clamp(c.Scale, 0.75, 1.5), FreeX = Math.Clamp(c.FreeX, 0, 1), FreeY = Math.Clamp(c.FreeY, 0, 1),
            Yaw = Math.Clamp(c.Yaw, -MaxYaw, MaxYaw), Pitch = Math.Clamp(c.Pitch, -MaxPitch, MaxPitch) };
        Exec("UPDATE cabinets SET name=$n,theme_id=$t,edge=$e,monitor_device=$m,scale=$s,free_x=$x,free_y=$y,auto_hide=$a,always_on_top=$o,open_direction=$d,yaw=$w,pitch=$p,open_category=$c,design=$g WHERE id=$i",
            ("$i", c.Id.ToString()), ("$n", c.Name), ("$t", c.ThemeId), ("$e", (int)c.Edge), ("$m", c.MonitorDevice), ("$s", c.Scale),
            ("$x", c.FreeX), ("$y", c.FreeY), ("$a", c.AutoHide ? 1 : 0), ("$o", c.AlwaysOnTop ? 1 : 0), ("$d", (int)c.OpenDirection),
            ("$w", c.Yaw), ("$p", c.Pitch), ("$c", c.OpenCategory?.ToString()), ("$g", JsonSerializer.Serialize(c.Design ?? new())));
    }

    /// <summary>Dolabi siler; kategoriler (ve kisayollar) moveTo'ya (yoksa ilk kalan dolaba) tasinir, veri silinmez. Son dolap silinemez.</summary>
    public void DeleteCabinet(Guid id, Guid? moveTo = null) => Tx(() =>
    {
        var others = GetCabinets().Where(c => c.Id != id).ToList();
        if (others.Count == 0) throw new DolapException("Son dolap silinemez.");
        var target = moveTo is { } m && others.Any(c => c.Id == m) ? m : others[0].Id;
        foreach (var cat in GetCategories(id)) MoveCategoryToCabinet(cat.Id, target);
        Exec("DELETE FROM cabinets WHERE id=$i", ("$i", id.ToString()));
    });

    // ---- yerlesim anlik goruntuleri: calisma setleri (adli) ve zaman makinesi (adsiz, son 30) ----
    public LayoutSnapshot SaveSnapshot(string? name)
    {
        var snap = new LayoutSnapshot(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? null : RequireName(name), DateTime.Now, GetCabinets());
        Tx(() =>
        {
            if (snap.Name != null) Exec("DELETE FROM snapshots WHERE name=$n", ("$n", snap.Name)); // ayni adli set guncellenir
            Exec("INSERT INTO snapshots VALUES($i,$n,$t,$d)", ("$i", snap.Id.ToString()), ("$n", snap.Name), ("$t", snap.CreatedAt.ToString("o")),
                ("$d", JsonSerializer.Serialize(snap.Cabinets)));
            Exec($"DELETE FROM snapshots WHERE name IS NULL AND id NOT IN (SELECT id FROM snapshots WHERE name IS NULL ORDER BY created_at DESC LIMIT {MaxSnapshots})");
        });
        return snap;
    }

    /// <summary>named: true = calisma setleri, false = otomatik gecmis; yeniden eskiye.</summary>
    public List<LayoutSnapshot> GetSnapshots(bool named) =>
        Query($"SELECT id,name,created_at,data FROM snapshots WHERE name IS {(named ? "NOT " : "")}NULL ORDER BY {(named ? "name" : "created_at DESC")}",
            r => new LayoutSnapshot(Guid.Parse(r.GetString(0)), r.IsDBNull(1) ? null : r.GetString(1), DateTime.Parse(r.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
                JsonSerializer.Deserialize<List<Cabinet>>(r.GetString(3)) ?? []));

    /// <summary>Kayitli yerlesimi uygular: hala var olan dolaplarin konum/tema/aci/tasarim/gorunurlugu geri gelir.
    /// Kategoriler ve kisayollar degismez. Uygulanan dolap sayisini doner.</summary>
    public int RestoreSnapshot(Guid id)
    {
        var snap = GetSnapshots(true).Concat(GetSnapshots(false)).FirstOrDefault(s => s.Id == id)
                   ?? throw new DolapException("Kayıtlı yerleşim bulunamadı.");
        var existing = GetCabinets().Select(c => c.Id).ToHashSet();
        var n = 0;
        Tx(() => { foreach (var c in snap.Cabinets.Where(c => existing.Contains(c.Id))) { UpdateCabinet(c); n++; } });
        return n;
    }

    public void DeleteSnapshot(Guid id) => Exec("DELETE FROM snapshots WHERE id=$i", ("$i", id.ToString()));

    // ---- kirik kisayollar ve tasinabilir duzen ----
    /// <summary>Hedefi (ya da .lnk dosyasi) artik bulunmayan yerel kisayollar.</summary>
    public List<Shortcut> FindBroken() => GetShortcuts()
        .Where(s => s.Kind != ShortcutKind.Url && !File.Exists(s.SourceLinkPath ?? s.Target) && !Directory.Exists(s.SourceLinkPath ?? s.Target))
        .ToList();

    /// <summary>Baska bilgisayardan gelen yollari toplu esler: eski onek -> yeni onek (buyuk/kucuk harf duyarsiz). Etkilenen kisayol sayisi.</summary>
    public int RemapPrefix(string oldPrefix, string newPrefix)
    {
        if (string.IsNullOrWhiteSpace(oldPrefix)) throw new DolapException("Eski yol öneki boş olamaz.");
        string? Map(string? p) => p != null && p.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase) ? newPrefix + p[oldPrefix.Length..] : p;
        var n = 0;
        Tx(() =>
        {
            foreach (var s in GetShortcuts().Where(s => s.Kind != ShortcutKind.Url))
            {
                var (t, l, w) = (Map(s.Target), Map(s.SourceLinkPath), Map(s.WorkingDirectory));
                if (t == s.Target && l == s.SourceLinkPath && w == s.WorkingDirectory) continue;
                Exec("UPDATE shortcuts SET target=$t,source_link_path=$l,working_directory=$w WHERE id=$i",
                    ("$i", s.Id.ToString()), ("$t", t), ("$l", l), ("$w", w));
                n++;
            }
        });
        return n;
    }

    /// <summary>Akilli cekmece: kategorinin tum icerigini verilen listeyle degistirir (tek islem). Elle ekleme bu cekmecede tutulmaz.</summary>
    public void ReplaceManaged(Guid categoryId, IReadOnlyList<ShortcutDraft> drafts) => Tx(() =>
    {
        Exec("DELETE FROM shortcuts WHERE category_id=$c", ("$c", categoryId.ToString()));
        foreach (var d in drafts.Take(60)) AddShortcut(categoryId, d);
    });

    // ---- ayarlar ----
    AppSettings LoadGlobalSettings()
    {
        var v = Scalar("SELECT value FROM settings WHERE key='app'") as string;
        if (v == null) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(v) ?? new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }

    /// <summary>Genel ayarlar + bu dolabin tema/yerlesimi.</summary>
    public AppSettings LoadEffective(Guid cabinetId)
    {
        var g = LoadGlobalSettings();
        var c = GetCabinets().FirstOrDefault(x => x.Id == cabinetId) ?? GetCabinets()[0];
        return g with
        {
            ThemeId = c.ThemeId, Edge = c.Edge, MonitorDevice = c.MonitorDevice, Scale = c.Scale, FreeX = c.FreeX, FreeY = c.FreeY,
            AutoHide = c.AutoHide, AlwaysOnTop = c.AlwaysOnTop, OpenDirection = c.OpenDirection,
            Yaw = c.Yaw, Pitch = c.Pitch, OpenCategory = c.OpenCategory, Design = c.Design ?? new(),
        };
    }

    /// <summary>Yerlesim/tema alanlari dolaba, kalani genel ayara yazilir (tek islem).</summary>
    public void SaveEffective(Guid cabinetId, AppSettings s) => Tx(() =>
    {
        var c = GetCabinets().FirstOrDefault(x => x.Id == cabinetId) ?? GetCabinets()[0];
        UpdateCabinet(FromSettings(c.Id, c.Name, s, c.OrderIndex));
        Exec("INSERT INTO settings VALUES('app',$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$v", JsonSerializer.Serialize(s)));
    });

    /// <summary>Eski API: ilk dolabin etkin ayarlari.</summary>
    public AppSettings LoadSettings() => LoadEffective(FirstCabinetId());
    public void SaveSettings(AppSettings s) => SaveEffective(FirstCabinetId(), s);

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

    sealed record Pack(int SchemaVersion, List<Category> Categories, List<Shortcut> Shortcuts, AppSettings Settings, List<Cabinet>? Cabinets = null);

    public void ExportJson(string path) => File.WriteAllText(path,
        JsonSerializer.Serialize(new Pack(SchemaVersion, GetCategories(), GetShortcuts(), LoadGlobalSettings(), GetCabinets()), Json));

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
        if (p is null || p.SchemaVersion is < 1 or > SchemaVersion) throw new DolapException("Yedek surumu desteklenmiyor.");
        // v1 paketi: tek varsayilan dolap uret, tum kategorileri ona bagla
        if (p.Cabinets is not { Count: > 0 })
        {
            var def = FromSettings(Guid.NewGuid(), "Dolap 1", p.Settings ?? new AppSettings(), 0);
            p = p with { Cabinets = [def], Categories = p.Categories.Select(c => c with { CabinetId = def.Id }).ToList() };
        }
        var cabIds = p.Cabinets.Select(c => c.Id).ToHashSet();
        var ids = p.Categories.Select(c => c.Id).ToHashSet();
        if (p.Categories.Count > MaxCategories || p.Shortcuts.Count > MaxShortcuts || p.Cabinets.Count > MaxCabinets
            || p.Categories.Any(c => !cabIds.Contains(c.CabinetId))
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
            Exec("DELETE FROM shortcuts; DELETE FROM categories; DELETE FROM cabinets;");
            foreach (var c in p.Cabinets!) InsertCabinet(c);
            foreach (var c in p.Categories)
                Exec("INSERT INTO categories(id,name,icon_key,color,order_index,created_at,updated_at,cabinet_id,linked_folder) VALUES($i,$n,$k,$c,$o,$t,$t,$b,$f)",
                    ("$i", c.Id.ToString()), ("$n", c.Name), ("$k", c.IconKey), ("$c", c.Color), ("$o", c.OrderIndex), ("$t", Now()), ("$b", c.CabinetId.ToString()), ("$f", c.LinkedFolder));
            foreach (var s in p.Shortcuts) Insert(s);
            Exec("INSERT INTO settings VALUES('app',$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$v", JsonSerializer.Serialize(p.Settings)));
        });
    }

    static string RequireName(string n)
    {
        n = (n ?? "").Trim();
        if (n.Length == 0) throw new DolapException("Ad bos olamaz.");
        return n.Length > 100 ? n[..100] : n;
    }

    public void Dispose() => _db.Dispose();
}
