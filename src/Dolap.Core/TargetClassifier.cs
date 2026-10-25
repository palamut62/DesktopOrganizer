namespace Dolap.Core;

public static class TargetClassifier
{
    public static bool IsHttpUrl(string s) =>
        Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>Hedef metninden tur cikarir; desteklenmeyen protokol/bos girdi DolapException firlatir.</summary>
    public static ShortcutKind Classify(string target)
    {
        var t = target.Trim().Trim('"');
        if (t.Length == 0) throw new DolapException("Hedef bos olamaz.");
        if (IsHttpUrl(t)) return ShortcutKind.Url;
        if (t.Contains("://") || t.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            throw new DolapException("Yalnizca http ve https baglantilari desteklenir.");
        if (Directory.Exists(t)) return ShortcutKind.Folder;
        return Path.GetExtension(t).ToLowerInvariant() switch
        {
            ".lnk" => ShortcutKind.Lnk,
            ".exe" => ShortcutKind.App,
            _ => ShortcutKind.File,
        };
    }
}
