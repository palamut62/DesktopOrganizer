namespace Dolap.Core;

public enum SkyKind { Clear, Cloudy, Rain, Snow }
public enum DayPhase { Day, Dusk, Night }
public sealed record OfficeState(SkyKind Sky, DayPhase Phase, bool Lights, bool Window, string Theme = "metal");
public sealed record WeatherNow(SkyKind Sky, bool IsDay, DateTime Sunrise, DateTime Sunset, string Place);

/// <summary>Ofis odasi sahne kurallari (saf, test edilebilir): saat/hava/ayar -> gokyuzu, gun evresi, tavan isigi.</summary>
public static class OfficeLogic
{
    /// <summary>Hava + saat + ayar -> sahne durumu. Hava yoksa mevsime gore yaklasik gun dogumu/batimi.</summary>
    public static OfficeState StateFor(AppSettings s, WeatherNow? w, DateTime now)
    {
        var (rise, set) = w is { } x ? (x.Sunrise, x.Sunset) : ApproxSun(now);
        var near = TimeSpan.FromMinutes(40);
        var phase = (now - set).Duration() <= near || (now - rise).Duration() <= near ? DayPhase.Dusk
            : (w?.IsDay ?? (now > rise && now < set)) ? DayPhase.Day : DayPhase.Night;
        var sky = w?.Sky ?? SkyKind.Clear;
        var lights = s.CeilingLights switch
        {
            "on" => true,
            "off" => false,
            _ => phase != DayPhase.Day || sky is SkyKind.Rain or SkyKind.Snow, // otomatik: aksam ve karanlik havada
        };
        // Oda dolabin temasina uyar: blok -> LEGO tuglali oda, metal -> endustriyel beton ofis.
        return new(sky, phase, lights, s.OfficeWindow, s.ThemeId == "blocks" ? "blocks" : "metal");
    }

    /// <summary>Hava servisi kapaliyken: Turkiye enlemi icin yillik ortalama egri (yaklasik, +-30 dk).</summary>
    static (DateTime Rise, DateTime Set) ApproxSun(DateTime now)
    {
        var c = Math.Cos(2 * Math.PI * (now.DayOfYear + 10) / 365.0); // kis +1, yaz -1
        var rise = 6.6 + 1.0 * c; var set = 18.6 - 1.4 * c;
        return (now.Date.AddHours(rise), now.Date.AddHours(set));
    }

    /// <summary>WMO hava kodu -> pencere gokyuzu.</summary>
    public static SkyKind Classify(int code) => code switch
    {
        0 or 1 => SkyKind.Clear,
        >= 71 and <= 77 or 85 or 86 => SkyKind.Snow,
        >= 51 and <= 67 or >= 80 and <= 82 or >= 95 => SkyKind.Rain,
        _ => SkyKind.Cloudy, // 2, 3, sis (45, 48)
    };

}
