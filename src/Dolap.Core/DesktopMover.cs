namespace Dolap.Core;

public sealed record FileMove(string From, string To);

/// <summary>Masaustu ogesini cekmecenin klasorune tasir. Asla uzerine yazmaz, silmez; kullanimdaki veya yarim dosyayi atlar.</summary>
public static class DesktopMover
{
    public static string DrawerFolder(string root, string drawerName)
    {
        var safe = string.Concat(drawerName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim(' ', '.');
        return Path.Combine(root, safe.Length == 0 ? "Çekmece" : safe);
    }

    /// <summary>Tasinamazsa (yok, kullanimda, yarim indirme, farkli birim klasoru) null doner; hicbir sey degismez.</summary>
    public static FileMove? Move(string source, string folder)
    {
        source = Path.GetFullPath(source);
        var isDir = Directory.Exists(source);
        if (!isDir && !File.Exists(source)) return null;
        if (Path.GetExtension(source).ToLowerInvariant() is ".crdownload" or ".part" or ".tmp" or ".download") return null;
        try
        {
            if (!isDir) { using var _ = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None); }
            Directory.CreateDirectory(folder);
            var dest = Free(Path.Combine(folder, Path.GetFileName(source)));
            if (isDir) Directory.Move(source, dest); else File.Move(source, dest, overwrite: false);
            return new(source, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Geri tasir; eski yer doluysa yanina "(2)" ile koyar. Hedef artik yoksa sessizce atlar.</summary>
    public static void Undo(FileMove m)
    {
        if (!File.Exists(m.To) && !Directory.Exists(m.To)) return;
        var back = Free(m.From);
        if (Directory.Exists(m.To)) Directory.Move(m.To, back); else File.Move(m.To, back, overwrite: false);
    }

    static string Free(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!; var name = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }
}
