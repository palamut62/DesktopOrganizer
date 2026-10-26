using System.Text.Json;

namespace Dolap.Core;

public sealed record UsabilitySettings
{
    public string QuickHotkey { get; init; } = "Ctrl+Alt+Space";
    public List<Guid> Favorites { get; init; } = [];
    public Dictionary<Guid, int> DrawerPages { get; init; } = [];
    public bool DesktopInbox { get; init; } = true;
    public Guid? DesktopInboxCategory { get; init; }
    public string AssistantModel { get; init; } = "";
    public bool AutoClassifyDesktop { get; init; }
    public string AutoClassifyDay { get; init; } = "";
    public int AutoClassifyRequests { get; init; }
}

public sealed partial class CollectionStore
{
    public UsabilitySettings LoadUsability()
    {
        try { return Normalize(JsonSerializer.Deserialize<UsabilitySettings>(Scalar("SELECT value FROM settings WHERE key='usability'") as string ?? "{}") ?? new()); }
        catch (JsonException) { return new(); }
    }
    static UsabilitySettings Normalize(UsabilitySettings value) => value with
    {
        Favorites = (value.Favorites ?? []).Distinct().Take(8).ToList(),
        DrawerPages = (value.DrawerPages ?? []).Where(p => p.Value >= 0).ToDictionary(p => p.Key, p => Math.Min(p.Value, 2000)),
        QuickHotkey = string.IsNullOrWhiteSpace(value.QuickHotkey) ? "Ctrl+Alt+Space" : value.QuickHotkey,
        AssistantModel = value.AssistantModel ?? ""
    };
    public void SaveUsability(UsabilitySettings value) => Exec("INSERT INTO settings VALUES('usability',$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$v", JsonSerializer.Serialize(Normalize(value))));
    public void ToggleFavorite(Guid id)
    {
        var prefs = LoadUsability(); var ids = prefs.Favorites.ToList();
        if (!ids.Remove(id))
        {
            if (ids.Count >= 8) throw new DolapException("En fazla 8 favori ekleyebilirsiniz.");
            if (!GetShortcuts().Any(s => s.Id == id)) throw new DolapException("Kısayol artık mevcut değil.");
            ids.Add(id);
        }
        SaveUsability(prefs with { Favorites = ids });
    }
    public void SaveDrawerPage(Guid category, int page)
    {
        var prefs = LoadUsability(); prefs.DrawerPages[category] = Math.Max(0, page); SaveUsability(prefs);
    }
    public Category? EnsureDesktopInbox(string desktop)
    {
        var prefs = LoadUsability();
        var categories = GetCategories();
        var category = categories.FirstOrDefault(c => c.Id == prefs.DesktopInboxCategory);
        if (!prefs.DesktopInbox)
        {
            if (category != null && category.LinkedFolder != null) SetLinkedFolder(category.Id, null);
            return null;
        }
        category ??= categories.FirstOrDefault(c => string.Equals(c.LinkedFolder, desktop, StringComparison.OrdinalIgnoreCase));
        category ??= AddCategory("Masaüstüne Gelenler", "download", "#268DAD");
        if (category.LinkedFolder != desktop) SetLinkedFolder(category.Id, desktop);
        if (prefs.DesktopInboxCategory != category.Id) SaveUsability(prefs with { DesktopInboxCategory = category.Id });
        return category with { LinkedFolder = desktop };
    }
    public List<Shortcut> FindDuplicates(ShortcutDraft draft, Guid? except = null) => GetShortcuts()
        .Where(s => s.Id != except && SameTarget(s.Target, draft.Target) && (s.Arguments ?? "") == (draft.Arguments ?? "")).ToList();
    static bool SameTarget(string a, string b) =>
        TargetClassifier.IsHttpUrl(a) || TargetClassifier.IsHttpUrl(b)
            ? string.Equals(a, b, StringComparison.Ordinal)
            : string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Local suggestion only; never moves anything without user selection.</summary>
    public Category? SuggestCategory(ShortcutDraft draft)
    {
        var categories = GetCategories().Where(c => string.IsNullOrEmpty(c.LinkedFolder)).ToList();
        var extension = Path.GetExtension(draft.Target).ToLowerInvariant();
        var words = draft.Kind == ShortcutKind.Url ? new[] { "web", "site", "baglanti" }
            : extension is ".pdf" or ".docx" or ".xlsx" or ".txt" ? ["evrak", "belge", "is", "arsiv"]
            : extension is ".png" or ".jpg" or ".mp4" or ".mp3" ? ["medya", "resim", "muzik"]
            : extension is ".cs" or ".py" or ".sln" ? ["gelistirme", "kod", "proje"]
            : extension is ".zip" or ".rar" or ".7z" ? ["arsiv", "kurulum"] : Array.Empty<string>();
        var shortcuts = GetShortcuts();
        return categories.Select(c => (Category: c, Score:
                (words.Any(w => TurkishText.Fold(c.Name).Contains(w)) ? 5 : 0) +
                shortcuts.Count(s => s.CategoryId == c.Id && s.Kind == draft.Kind && extension.Length > 0 && string.Equals(Path.GetExtension(s.Target), extension, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.Category).FirstOrDefault();
    }
}
