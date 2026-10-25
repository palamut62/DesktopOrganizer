using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dolap.App;

/// <summary>Temaya uygun statik duvar kagidini uygular; ilk degisiklikten once kullanicinin orijinalini saklar, kapatilinca geri yukler.</summary>
static class WallpaperService
{
    const string Key = @"Control Panel\Desktop";
    const uint SPI_SETDESKWALLPAPER = 0x14, SPIF_UPDATE_AND_SEND = 0x3;
    static string BackupFile => Path.Combine(Paths.Data, "wallpaper-original.txt");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, string pv, uint flags);

    public static Task Apply(string themeId, bool left) => Task.Run(() =>
    {
        var name = $"{(themeId == "blocks" ? "blocks" : "metal")}-{(left ? "left" : "right")}.jpg";
        var src = Path.Combine(AppContext.BaseDirectory, "Wallpapers", name);
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(Paths.Data);
        var dst = Path.Combine(Paths.Data, name); // kurulum klasoru degisse de duvar kagidi kaybolmasin
        var copied = !File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length;
        if (copied) File.Copy(src, dst, true);
        SetFile(dst, force: copied);
    });

    /// <summary>Verilen dosyayi duvar kagidi yapar (Doldur). Ilk degisiklikten once kullanicinin orijinali saklanir.</summary>
    public static void SetFile(string dst, bool force = false)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, true);
        if (k == null) return;
        var cur = k.GetValue("Wallpaper") as string ?? "";
        if (!force && string.Equals(cur, dst, StringComparison.OrdinalIgnoreCase)) return; // zaten uygulanmis
        if (!File.Exists(BackupFile) && !cur.StartsWith(Paths.Data, StringComparison.OrdinalIgnoreCase))
            File.WriteAllLines(BackupFile, [SaveOriginal(cur), k.GetValue("WallpaperStyle") as string ?? "10", k.GetValue("TileWallpaper") as string ?? "0"]);
        k.SetValue("WallpaperStyle", "10"); k.SetValue("TileWallpaper", "0");
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, dst, SPIF_UPDATE_AND_SEND);
    }

    /// <summary>Orijinal dosya yoksa (Spotlight, silinmis kaynak) Windows'un donusturulmus kopyasi saklanir.</summary>
    static string SaveOriginal(string cur)
    {
        if (File.Exists(cur)) return cur;
        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        if (!File.Exists(transcoded)) return cur;
        var copy = Path.Combine(Paths.Data, "wallpaper-original.jpg");
        File.Copy(transcoded, copy, true);
        return copy;
    }

    public static Task Restore() => Task.Run(() =>
    {
        if (!File.Exists(BackupFile)) return;
        var l = File.ReadAllLines(BackupFile);
        using (var k = Registry.CurrentUser.OpenSubKey(Key, true))
        {
            k?.SetValue("WallpaperStyle", l.ElementAtOrDefault(1) ?? "10");
            k?.SetValue("TileWallpaper", l.ElementAtOrDefault(2) ?? "0");
        }
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, l.ElementAtOrDefault(0) ?? "", SPIF_UPDATE_AND_SEND);
        File.Delete(BackupFile);
    });
}
