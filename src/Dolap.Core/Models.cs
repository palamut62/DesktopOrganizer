namespace Dolap.Core;

public enum ShortcutKind { App, Lnk, File, Folder, Url }

public sealed record Category(Guid Id, string Name, string IconKey, string Color, int OrderIndex, Guid CabinetId = default);

public sealed record Shortcut(
    Guid Id, Guid CategoryId, string DisplayName, ShortcutKind Kind, string? SourceLinkPath,
    string Target, string? Arguments, string? WorkingDirectory, int OrderIndex);

/// <summary>Yeni/duzenlenen kisayol girdisi (kimlik ve sira store tarafindan verilir).</summary>
public sealed record ShortcutDraft(
    string DisplayName, ShortcutKind Kind, string Target,
    string? SourceLinkPath = null, string? Arguments = null, string? WorkingDirectory = null);

public enum DockEdge { Left, Right, Free }

/// <summary>Cekmecelerin acilma yonu. Auto: kenara gore (sag kenar -> sola acilir).</summary>
public enum OpenDirection { Auto, Left, Right }

/// <summary>Masaustundeki bir dolap: kendi temasi, yerlesimi ve kategorileri vardir.</summary>
public sealed record Cabinet(
    Guid Id, string Name, string ThemeId, DockEdge Edge, string? MonitorDevice, double Scale,
    double FreeX, double FreeY, bool AutoHide, bool AlwaysOnTop, OpenDirection OpenDirection, int OrderIndex,
    double Yaw = 0, double Pitch = 0, Guid? OpenCategory = null);

public sealed record AppSettings
{
    public string ThemeId { get; init; } = "metal";          // metal | blocks
    public string QualityProfile { get; init; } = "standard"; // standard | low
    public bool? ReduceMotion { get; init; }                  // null = sistem tercihi
    public bool StartWithWindows { get; init; }
    public string Hotkey { get; init; } = "Ctrl+Alt+D";
    public bool CloseAfterLaunch { get; init; }
    public double Scale { get; init; } = 1.0;                 // 0.75 - 1.5
    public DockEdge Edge { get; init; } = DockEdge.Right;
    public string? MonitorDevice { get; init; }               // null = birincil
    public double FreeX { get; init; } = 0.8;                 // calisma alanina normallestirilmis
    public double FreeY { get; init; } = 0.1;
    public bool AutoHide { get; init; }
    public bool AlwaysOnTop { get; init; }
    public OpenDirection OpenDirection { get; init; }
    public double Yaw { get; init; }                          // 3B donus (derece), dolap basina
    public double Pitch { get; init; }                        // bakis yuksekligi farki (derece)
    public Guid? OpenCategory { get; init; }                  // son acik cekmece
    public bool ThemedWallpaper { get; init; } = true;   // temaya uygun oda duvar kagidi
    public string RoomStyle { get; init; } = "office";       // office: pencereli ofis | classic: tema duvar kagidi
    public bool OfficeWindow { get; init; } = true;
    public bool WeatherEnabled { get; init; }                 // acikken sehir Open-Meteo'ya gonderilir
    public string WeatherCity { get; init; } = "";
    public bool WeatherUseDeviceLocation { get; init; } = true; // Windows konum hizmeti; izin yoksa sehir kullanilir
    public string CeilingLights { get; init; } = "auto";     // auto | on | off
    public bool DepthScale { get; init; } = true;         // odadaki konuma gore boyut: uzakta kucuk, yakinda buyuk
    public bool ShowSystemMonitor { get; init; } = true;  // duvardaki TV: canli sistem monitoru
    public double MonitorScale { get; init; } = 1.0;         // 0.6 - 1.6
    public string MonitorTheme { get; init; } = "dark";      // dark | wood | silver | neon
    public double MonitorX { get; init; } = 0.5;
    public double MonitorY { get; init; } = 0.04;
    public string? MonitorScreen { get; init; }
    public bool HideDesktopIcons { get; init; }          // uygulama acikken masaustu simgeleri gizli (dosyalar yerinde)
    public bool Sounds { get; init; } = true;            // cekmece acilma/kapanma sesi
    public bool FirstRunDone { get; init; }
}

public sealed class DolapException(string message) : Exception(message);
