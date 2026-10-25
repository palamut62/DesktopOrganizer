using System.IO;
using System.Media;
using System.Windows;

namespace Dolap.App;

/// <summary>tools/gen_sounds.py ile uretilen malzemeye ozel cekmece sesleri (gomulu WAV).</summary>
static class DrawerSounds
{
    static readonly Dictionary<string, byte[]> Cache = [];

    /// <summary>16 bit PCM WAV orneklerini olcekler (SoundPlayer'da ses seviyesi yok). 44 baytlik standart baslik varsayilir.</summary>
    static byte[] Scaled(byte[] wav, double k)
    {
        var o = (byte[])wav.Clone();
        for (var i = 44; i + 1 < o.Length; i += 2)
        {
            var v = (short)(o[i] | (o[i + 1] << 8));
            var n = (short)Math.Clamp(v * k, short.MinValue, short.MaxValue);
            o[i] = (byte)n; o[i + 1] = (byte)(n >> 8);
        }
        return o;
    }

    public static void Play(bool blocks, bool open, int volume = 100)
    {
        if (volume <= 0) return;
        var name = $"{(blocks ? "blocks" : "metal")}-{(open ? "open" : "close")}.wav";
        try
        {
            if (!Cache.TryGetValue(name, out var bytes))
            {
                using var s = Application.GetResourceStream(new Uri($"pack://application:,,,/Sounds/{name}"))!.Stream;
                using var ms = new MemoryStream(); s.CopyTo(ms);
                Cache[name] = bytes = ms.ToArray();
            }
            new SoundPlayer(new MemoryStream(volume >= 100 ? bytes : Scaled(bytes, volume / 100.0))).Play(); // asenkron; yeni ses oncekini keser
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NullReferenceException) { /* ses yoksa sessiz devam */ }
    }
}
