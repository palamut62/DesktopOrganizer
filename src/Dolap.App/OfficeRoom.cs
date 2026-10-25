using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolap.Core;

namespace Dolap.App;

/// <summary>Ofis odasi duvar kagidi: tools/gen_office.py katmanlarini (gokyuzu + oda + gunes izi + tavan isigi) birlestirir.
/// Ayni durum icin dosya onbellekte tutulur; yeniden cizilmez.</summary>
static class OfficeRoom
{
    const int W = 2560, H = 1600;
    static readonly Int32Rect Win = new(486, 304, 1203 - 486, 880 - 304); // gen_office.py WIN ile ayni

    /// <summary>Durumu cizip JPEG yolunu doner. STA is parcacigi gerektirir (WPF cizimi).</summary>
    public static string Render(OfficeState st)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Office");
        // Katman surumu ada dahil: gen_office.py yeniden calisirsa eski onbellek kullanilmaz.
        var ver = File.GetLastWriteTimeUtc(Path.Combine(dir, $"base-{st.Theme}-day-win.png")).Ticks / TimeSpan.TicksPerSecond % 1000000;
        var name = $"office-{ver}-{st.Theme}-{st.Sky}-{st.Phase}-{(st.Lights ? "isik" : "karanlik")}-{(st.Window ? "pencere" : "duz")}.jpg".ToLowerInvariant();
        var dst = Path.Combine(Paths.Data, name);
        if (File.Exists(dst)) return dst;
        BitmapImage Load(string n)
        {
            var b = new BitmapImage();
            b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(Path.Combine(dir, n)); b.EndInit();
            b.Freeze(); return b;
        }
        var full = new Rect(0, 0, W, H);
        var phase = st.Phase.ToString().ToLowerInvariant();
        var win = st.Window ? "win" : "nowin";
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, full);
            if (st.Window) dc.DrawImage(Load($"sky-{st.Sky.ToString().ToLowerInvariant()}-{phase}.png"), new Rect(Win.X, Win.Y, Win.Width, Win.Height));
            dc.DrawImage(Load($"base-{st.Theme}-{(st.Phase == DayPhase.Night ? "night" : "day")}-{win}.png"), full);
            if (st.Phase == DayPhase.Dusk) { dc.PushOpacity(.55); dc.DrawImage(Load($"base-{st.Theme}-night-{win}.png"), full); dc.Pop(); }
            if (st.Window && st.Phase == DayPhase.Day && st.Sky == SkyKind.Clear) dc.DrawImage(Load("sunbeam.png"), full);
            if (st.Lights) dc.DrawImage(Load("lights.png"), full);
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Paths.Data);
        var tmp = dst + ".tmp";
        using (var f = File.Create(tmp)) enc.Save(f);
        File.Move(tmp, dst, true); // yarim dosya duvar kagidi olmasin
        return dst;
    }
}

/// <summary>Open-Meteo (anahtarsiz): sehir adi -> koordinat -> anlik hava kodu, gunduz/gece, gun dogumu/batimi.</summary>
static class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Windows konum hizmetinden (kullanici izniyle) yaklasik konum; izin/konum yoksa null.</summary>
    public static async Task<(double Lat, double Lon)?> DeviceLocation()
    {
        try
        {
            if (await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync() != Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed) return null;
            var g = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default };
            var p = await g.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
            return (p.Coordinate.Point.Position.Latitude, p.Coordinate.Point.Position.Longitude);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException or TaskCanceledException)
        {
            return null;
        }
    }

    public static Task<WeatherNow> FetchAt(double lat, double lon, string place) => Forecast(lat, lon, place);

    public static async Task<WeatherNow> Fetch(string city)
    {
        var geo = await GetJson($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language=tr");
        if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new DolapException($"\"{city}\" bulunamadı. Şehir adını kontrol edin.");
        var r = results[0];
        var lat = r.GetProperty("latitude").GetDouble(); var lon = r.GetProperty("longitude").GetDouble();
        var place = r.GetProperty("name").GetString() ?? city;
        return await Forecast(lat, lon, place);
    }

    static async Task<WeatherNow> Forecast(double lat, double lon, string place)
    {
        var inv = CultureInfo.InvariantCulture;
        var fc = await GetJson($"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}&current=weather_code,is_day&daily=sunrise,sunset&timezone=auto&forecast_days=1");
        var cur = fc.RootElement.GetProperty("current");
        var daily = fc.RootElement.GetProperty("daily");
        return new(OfficeLogic.Classify(cur.GetProperty("weather_code").GetInt32()), cur.GetProperty("is_day").GetInt32() == 1,
            DateTime.Parse(daily.GetProperty("sunrise")[0].GetString()!, inv), DateTime.Parse(daily.GetProperty("sunset")[0].GetString()!, inv), place);
    }

    static async Task<JsonDocument> GetJson(string url)
    {
        try { return JsonDocument.Parse(await Http.GetStringAsync(url)); }
        catch (HttpRequestException e) { throw new DolapException("Hava durumu alınamadı (bağlantı): " + e.Message); }
        catch (TaskCanceledException) { throw new DolapException("Hava durumu servisi zamanında yanıt vermedi."); }
        catch (JsonException) { throw new DolapException("Hava durumu yanıtı okunamadı."); }
    }
}
