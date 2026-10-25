namespace Dolap.Core;

public static class ShortcutHealth
{
    public static bool IsBroken(Shortcut shortcut)
    {
        if (shortcut.Kind == ShortcutKind.Url) return false;
        if (shortcut.SourceLinkPath is { Length: > 0 } link && !File.Exists(link)) return true;
        var target = Environment.ExpandEnvironmentVariables(shortcut.Target);
        // Shell namespace and web targets are not filesystem paths.
        if (TargetClassifier.IsHttpUrl(target) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("::{", StringComparison.Ordinal)) return false;
        // Some shell links do not expose a filesystem target. Do not label these as missing.
        if (shortcut.Kind == ShortcutKind.Lnk && string.IsNullOrWhiteSpace(target)) return false;
        return !File.Exists(target) && !Directory.Exists(target);
    }
}
