using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Dolap.Core;

namespace Dolap.App;

/// <summary>Dolaptaki ogeyi disariya surukleme: masaustundeki bir dosyayi tutup baska uygulamaya atmakla ayni
/// kabuk davranisi (FileDrop). Dosya tasinmaz, kopyalama/baglama kararini birakilan uygulama verir.</summary>
public static class FileDragOut
{
    /// <summary>Surukleme mesafesi; bunun altindaki hareket tik sayilir.</summary>
    public const double Threshold = 8;

    public static string? PathOf(Shortcut s)
    {
        if (s.Kind == ShortcutKind.Url) return null;
        foreach (var p in new[] { s.Target, s.SourceLinkPath })
            if (!string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))) return p;
        return null;
    }

    public static DataObject? Data(Shortcut s)
    {
        var data = new DataObject();
        if (s.Kind == ShortcutKind.Url) { data.SetText(s.Target); return data; }
        if (PathOf(s) is not { } path) return null;
        data.SetFileDropList(new StringCollection { path });
        data.SetText(path); // yol metni bekleyen uygulamalar icin
        return data;
    }

    public static void Start(DependencyObject source, Shortcut s)
    {
        if (Data(s) is not { } data) return;
        try { DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Link); }
        catch (COMException) { } // hedef uygulama surukleme sirasinda kapanabilir
    }

    /// <summary>Tek bir ogeyi gosteren denetime surukleme kaynagi davranisi ekler.</summary>
    public static void Attach(FrameworkElement element, Func<Shortcut> item)
    {
        Point? press = null;
        element.PreviewMouseLeftButtonDown += (_, e) => press = e.GetPosition(element);
        element.PreviewMouseLeftButtonUp += (_, _) => press = null;
        element.PreviewMouseMove += (_, e) =>
        {
            if (press is not { } p || e.LeftButton != MouseButtonState.Pressed) { press = null; return; }
            if ((e.GetPosition(element) - p).Length <= Threshold) return;
            press = null;
            Start(element, item());
        };
    }
}
