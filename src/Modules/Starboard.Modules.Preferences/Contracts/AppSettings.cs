namespace Starboard.Modules.Preferences.Contracts;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Default shell for tabs created after the setting is applied. Null uses automatic discovery.
    /// Existing tabs, including their restart path, retain the shell captured at creation.
    /// </summary>
    public string? ShellExecutable { get; init; }

    /// <summary>
    /// Theme applied in place to the WPF shell and every terminal tab.
    /// </summary>
    public string Theme { get; init; } = "Tokyo Night";

    public double CollapsedHeightDip { get; init; } = 200;

    public string FontFamily { get; init; } = "Cascadia Mono";

    public double FontSize { get; init; } = 13;

    public double Opacity { get; init; } = 0.97;

    public bool StartWithWindows { get; init; }

    public string PreferredMonitor { get; init; } = "Taskbar";

    public string ExpandShortcut { get; init; } = "Ctrl+Alt+E";

    public string ActivationShortcut { get; init; } = "Ctrl+Alt+S";
}
