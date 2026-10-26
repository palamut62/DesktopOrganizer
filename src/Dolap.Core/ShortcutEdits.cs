namespace Dolap.Core;

public sealed partial class CollectionStore
{
    sealed record ShortcutEdit(string Label, Dictionary<Guid, Shortcut?> Before, Dictionary<Guid, Shortcut?> After);
    readonly List<ShortcutEdit> _shortcutUndo = [];
    public string? UndoLabel => _shortcutUndo.LastOrDefault()?.Label;

    /// <summary>One user action, one transaction and one undo entry. Session-local, at most 30 actions.</summary>
    public void EditShortcuts(string label, Action action)
    {
        lock (_gate)
        {
            ShortcutEdit? edit = null;
            Tx(() =>
            {
                var before = GetShortcuts().ToDictionary(s => s.Id);
                action();
                var after = GetShortcuts().ToDictionary(s => s.Id);
                var changed = before.Keys.Union(after.Keys).Where(id => before.GetValueOrDefault(id) != after.GetValueOrDefault(id)).ToArray();
                if (changed.Length > 0) edit = new(label,
                    changed.ToDictionary(id => id, id => before.GetValueOrDefault(id)),
                    changed.ToDictionary(id => id, id => after.GetValueOrDefault(id)));
            });
            if (edit == null) return;
            _shortcutUndo.Add(edit);
            if (_shortcutUndo.Count > 30) _shortcutUndo.RemoveAt(0);
        }
    }

    public bool UndoShortcutEdit()
    {
        lock (_gate)
        {
            if (_shortcutUndo.Count == 0) return false;
            var edit = _shortcutUndo[^1];
            Tx(() =>
            {
                var current = GetShortcuts().ToDictionary(s => s.Id);
                var categories = GetCategories().Select(c => c.Id).ToHashSet();
                // A watcher or another command may have changed the same record. Never overwrite it.
                if (edit.After.Any(p => current.GetValueOrDefault(p.Key) != p.Value) ||
                    edit.Before.Values.Any(s => s != null && !categories.Contains(s.CategoryId)))
                    throw new DolapException("Bu öğeler daha sonra değişti; diğer değişiklikleri korumak için geri alınamadı.");
                var total = current.Count - edit.After.Values.Count(s => s != null) + edit.Before.Values.Count(s => s != null);
                if (total > MaxShortcuts) throw new DolapException("Geri alma kısayol sınırını aşar.");
                foreach (var id in edit.Before.Keys) Exec("DELETE FROM shortcuts WHERE id=$i", ("$i", id.ToString()));
                foreach (var s in edit.Before.Values.OfType<Shortcut>()) Insert(s);
            });
            _shortcutUndo.RemoveAt(_shortcutUndo.Count - 1);
            return true;
        }
    }

    public void BatchShortcuts(IReadOnlyCollection<Guid> ids, string action, Guid? destination = null, string? section = null)
    {
        if (ids.Count == 0) throw new DolapException("Önce en az bir kısayol seçin.");
        if (action is not ("move" or "section" or "remove")) throw new DolapException("Geçersiz toplu işlem.");
        EditShortcuts(action == "move" ? "Toplu taşıma" : action == "section" ? "Bölüm atama" : "Toplu kaldırma", () =>
        {
            var all = GetShortcuts().Select(s => s.Id).ToHashSet();
            if (ids.Any(id => !all.Contains(id))) throw new DolapException("Seçilen öğeler değişti; listeyi yeniden açın.");
            if (action == "move" && (destination == null || !GetCategories().Any(c => c.Id == destination)))
                throw new DolapException("Hedef çekmece bulunamadı.");
            foreach (var id in ids.Distinct())
                switch (action)
                {
                    case "move": MoveShortcut(id, destination!.Value); break;
                    case "section": SetSection(id, section); break;
                    case "remove": RemoveShortcut(id); break;
                }
        });
    }
}
