namespace Starboard.Modules.Preferences.Contracts;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? ShellExecutable { get; init; }

    public string Theme { get; init; } = "Tokyo Night";

    public double CollapsedHeightDip { get; init; } = 148;

    public string FontFamily { get; init; } = "Cascadia Mono";

    public double FontSize { get; init; } = 13;

    public double Opacity { get; init; } = 0.97;

    public bool StartWithWindows { get; init; }

    public string PreferredMonitor { get; init; } = "Taskbar";

    public string ExpandShortcut { get; init; } = "Ctrl+Alt+E";
}
