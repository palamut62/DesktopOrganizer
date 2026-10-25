namespace Dolap.Core;

public enum ShortcutKind { App, Lnk, File, Folder, Url }

public sealed record Category(Guid Id, string Name, string IconKey, string Color, int OrderIndex);

public sealed record Shortcut(
    Guid Id, Guid CategoryId, string DisplayName, ShortcutKind Kind, string? SourceLinkPath,
    string Target, string? Arguments, string? WorkingDirectory, int OrderIndex);

/// <summary>Yeni/duzenlenen kisayol girdisi (kimlik ve sira store tarafindan verilir).</summary>
public sealed record ShortcutDraft(
    string DisplayName, ShortcutKind Kind, string Target,
    string? SourceLinkPath = null, string? Arguments = null, string? WorkingDirectory = null);

public enum DockEdge { Left, Right, Free }

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
    public bool FirstRunDone { get; init; }
}

public sealed class DolapException(string message) : Exception(message);
