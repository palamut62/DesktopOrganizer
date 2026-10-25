using System.IO;
using System.Media;
using System.Windows;

namespace Dolap.App;

/// <summary>tools/gen_sounds.py ile uretilen malzemeye ozel cekmece sesleri (gomulu WAV).</summary>
static class DrawerSounds
{
    static readonly Dictionary<string, byte[]> Cache = [];

    public static void Play(bool blocks, bool open)
    {
        var name = $"{(blocks ? "blocks" : "metal")}-{(open ? "open" : "close")}.wav";
        try
        {
            if (!Cache.TryGetValue(name, out var bytes))
            {
                using var s = Application.GetResourceStream(new Uri($"pack://application:,,,/Sounds/{name}"))!.Stream;
                using var ms = new MemoryStream(); s.CopyTo(ms);
                Cache[name] = bytes = ms.ToArray();
            }
            new SoundPlayer(new MemoryStream(bytes)).Play(); // asenkron; yeni ses oncekini keser
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NullReferenceException) { /* ses yoksa sessiz devam */ }
    }
}
