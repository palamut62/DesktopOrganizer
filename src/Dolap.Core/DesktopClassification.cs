namespace Dolap.Core;

public sealed record DesktopAssignment(Guid ShortcutId, Guid CategoryId);
public static class DesktopClassification
{
    // Automatic mode has a deliberately narrower capability set than the interactive assistant.
    public static List<DesktopAssignment> Validate(AssistantPlan plan, IReadOnlyList<Shortcut> batch, IReadOnlyList<Category> categories)
    {
        AssistantProtocol.Validate(plan);
        var allowedItems = batch.Select(s => s.Id).ToHashSet();
        var allowedTargets = categories.Where(c => c.LinkedFolder == null).Select(c => c.Id).ToHashSet();
        var seen = new HashSet<Guid>(); var result = new List<DesktopAssignment>();
        foreach (var action in plan.Actions)
        {
            if (action.Kind != "move_shortcuts" || !Guid.TryParse(action.TargetId, out var target) || !allowedTargets.Contains(target))
                throw new DolapException("Otomatik sınıflandırıcı izin verilmeyen bir işlem önerdi; uygulanmadı.");
            foreach (var text in action.Ids)
            {
                if (!Guid.TryParse(text, out var id) || !allowedItems.Contains(id) || !seen.Add(id))
                    throw new DolapException("Otomatik sınıflandırma öğeleri geçersiz; uygulanmadı.");
                result.Add(new(id, target));
            }
        }
        return result;
    }
}
