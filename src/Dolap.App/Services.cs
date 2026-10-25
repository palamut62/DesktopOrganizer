using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolap.Core;
using Microsoft.Win32;

namespace Dolap.App;

static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;

    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLong(IntPtr h, int i, IntPtr v);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory f);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbm); }

    /// <summary>Kabuk ikonunu 64px'e kadar alir (ICONONLY|BIGGERSIZEOK). Basarisizsa null.</summary>
    public static BitmapSource? ShellIcon(string path, int size = 64)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            if (f.GetImage(new SIZE { W = size, H = size }, 0x4 | 0x1, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                bgra.Freeze();
                return bgra;
            }
            finally { DeleteObject(hbm); }
        }
        catch (Exception) { return null; }
    }
}

public enum LaunchStatus { Sent, NotFound, AccessDenied, NoAssociation, Cancelled, Error }
public sealed record LaunchResult(LaunchStatus Status, string Message);

static class ShellLauncher
{
    /// <summary>Hedefi Windows Shell'e teslim eder. cmd/PowerShell'e metin birlestirilmez.
    /// shortcut: UI is parcacigindan cagrilir (ShellExecute STA ister); yavas hedefler icin Task'a tasinabilir.</summary>
    public static LaunchResult Launch(Shortcut s)
    {
        var path = s.Kind == ShortcutKind.Lnk && s.SourceLinkPath != null ? s.SourceLinkPath : s.Target;
        try
        {
            if (s.Kind == ShortcutKind.Url)
            {
                if (!TargetClassifier.IsHttpUrl(path)) return new(LaunchStatus.Error, "Yalnizca http ve https baglantilari acilir.");
            }
            else if (!File.Exists(path) && !Directory.Exists(path))
                return new(LaunchStatus.NotFound, "Hedef bulunamadi. Konumu guncelleyin veya kisayolu kaldirin.");

            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (s.Kind is ShortcutKind.App or ShortcutKind.File && !string.IsNullOrWhiteSpace(s.Arguments)) psi.Arguments = s.Arguments;
            if (!string.IsNullOrWhiteSpace(s.WorkingDirectory) && Directory.Exists(s.WorkingDirectory)) psi.WorkingDirectory = s.WorkingDirectory;
            else if (s.Kind != ShortcutKind.Url && !Directory.Exists(path)) psi.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
            Process.Start(psi)?.Dispose();
            return new(LaunchStatus.Sent, $"{s.DisplayName} icin baslatma istegi gonderildi.");
        }
        catch (Win32Exception e) when (e.NativeErrorCode is 2 or 3) { return new(LaunchStatus.NotFound, "Hedef bulunamadi."); }
        catch (Win32Exception e) when (e.NativeErrorCode == 5) { return new(LaunchStatus.AccessDenied, "Erisim izni yok. Hedefi ve sistem izinlerini kontrol edin."); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1155) { return new(LaunchStatus.NoAssociation, "Bu dosya turunu acacak uygulama bulunamadi."); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return new(LaunchStatus.Cancelled, "Islem iptal edildi."); }
        catch (Exception e) { return new(LaunchStatus.Error, "Acilamadi: " + e.Message); }
    }

    public static void RevealInExplorer(Shortcut s)
    {
        var p = s.Kind == ShortcutKind.Lnk && s.SourceLinkPath != null ? s.SourceLinkPath : s.Target;
        if (!File.Exists(p) && !Directory.Exists(p)) return;
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        psi.ArgumentList.Add("/select,");
        psi.ArgumentList.Add(p);
        Process.Start(psi)?.Dispose();
    }
}

static class ShortcutFactory
{
    /// <summary>Yol veya URL'den taslak uretir; .lnk icin parametre/calisma dizini korunur (WScript.Shell).</summary>
    public static ShortcutDraft FromPath(string input)
    {
        var t = input.Trim().Trim('"');
        var kind = TargetClassifier.Classify(t);
        if (kind == ShortcutKind.Url)
            return new(Uri.TryCreate(t, UriKind.Absolute, out var u) ? u.Host : t, kind, t);
        if (!File.Exists(t) && !Directory.Exists(t)) throw new DolapException("Dosya veya klasor bulunamadi: " + t);
        var name = kind == ShortcutKind.Folder ? new DirectoryInfo(t).Name : Path.GetFileNameWithoutExtension(t);
        if (kind != ShortcutKind.Lnk) return new(name, kind, t);
        string target = t, args = "", wd = "";
        try
        {
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic l = sh.CreateShortcut(t);
            target = string.IsNullOrEmpty((string)l.TargetPath) ? t : (string)l.TargetPath;
            args = l.Arguments; wd = l.WorkingDirectory;
        }
        catch (Exception) { /* cozumlenemezse .lnk'in kendisi hedef kalir */ }
        return new(name, kind, target, t, string.IsNullOrEmpty(args) ? null : args, string.IsNullOrEmpty(wd) ? null : wd);
    }
}

static class StartupService
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "MasaustuDolabi";
    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, true);
        if (k == null) return;
        if (on) k.SetValue(Name, "\"" + Environment.ProcessPath + "\"");
        else k.DeleteValue(Name, false);
    }
}

static class Paths
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasaustuDolabi");
    public static string Db => Path.Combine(Data, "dolap.db");
    public static string Backups => Path.Combine(Data, "backups");
}

/// <summary>Brushed-metal dokusu: kodla uretilir, dosya gerektirmez.</summary>
static class Textures
{
    public static ImageBrush Brushed()
    {
        const int W = 256, H = 128;
        var px = new byte[W * H * 4];
        var rnd = new Random(7);
        for (var y = 0; y < H; y++)
        {
            var row = rnd.Next(-14, 15);
            for (var x = 0; x < W; x++)
            {
                var v = Math.Clamp(128 + row + rnd.Next(-8, 9), 0, 255);
                var i = (y * W + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)v; px[i + 3] = 34; // premultiplied degil: Bgra32 -> a dusuk
                px[i] = (byte)(v * 34 / 255); px[i + 1] = px[i]; px[i + 2] = px[i];
            }
        }
        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Pbgra32, null, px, W * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, W, H), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }
}
