using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dolap.App;

/// <summary>Windows'un "Masaustu simgelerini goster" anahtarini acip kapatir. Dosyalar tasinmaz/silinmez; yalniz gorunum.</summary>
static class DesktopIcons
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    public static bool AreVisible()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue("HideIcons") is not int v || v == 0;
    }

    public static void SetVisible(bool visible)
    {
        if (AreVisible() == visible) return;
        var view = DefView();
        // 0x7402: Explorer'in masaustu sag tik menusundeki "Masaustu simgelerini goster" komutu (ayari da kalici yazar).
        if (view != IntPtr.Zero) SendMessage(view, 0x0111, (IntPtr)0x7402, IntPtr.Zero);
    }

    static IntPtr DefView()
    {
        var view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        for (var w = IntPtr.Zero; view == IntPtr.Zero;)
        {
            w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
            if (w == IntPtr.Zero) break;
            view = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return view;
    }
}
