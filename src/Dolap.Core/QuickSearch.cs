namespace Dolap.Core;

public sealed record SearchHit(string Name, string Location, Guid CabinetId, Guid CategoryId, Shortcut? Shortcut)
{
    public string Kind => Shortcut == null ? "Çekmece" : "Kısayol";
    public string Detail => $"{Kind} · {Location}";
}

public static class QuickSearch
{
    public static List<SearchHit> Find(CollectionStore store, string query, Guid? categoryId = null)
    {
        var cabinets = store.GetCabinets().ToDictionary(c => c.Id, c => c.Name);
        var categories = store.GetCategories().ToDictionary(c => c.Id);
        var hits = categories.Values.Where(c => c.LinkedFolder != "shelf:")
            .Select(c => new SearchHit(c.Name, cabinets.GetValueOrDefault(c.CabinetId, "Dolap"), c.CabinetId, c.Id, null))
            .Concat(store.GetShortcuts().Where(s => categories.ContainsKey(s.CategoryId)).Select(s =>
            {
                var c = categories[s.CategoryId];
                return new SearchHit(s.DisplayName, cabinets.GetValueOrDefault(c.CabinetId, "Dolap") + " / " + c.Name, c.CabinetId, c.Id, s);
            }));
        var q = TurkishText.Fold(query.Trim());
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return hits.Where(h => (categoryId == null || (h.CategoryId == categoryId && h.Shortcut != null)) && terms.All(t => TurkishText.Fold(h.Name + " " + h.Location + " " + h.Shortcut?.Target).Contains(t)))
            .OrderBy(h => TurkishText.Fold(h.Name) == q ? 0 : TurkishText.Fold(h.Name).StartsWith(q) ? 1 : 2)
            .ThenBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
