namespace Starboard.Modules.Preferences.Contracts;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 7;

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

    /// <summary>
    /// Restores only saved terminal-tab configuration on the next launch. This never
    /// restores a previous shell process, command, output, or interactive session.
    /// </summary>
    public bool RestoreWorkspaceOnLaunch { get; init; }

    /// <summary>
    /// Shows a generic, non-activating notification only after an explicit shell completion signal.
    /// Command text and terminal output are never persisted with this preference.
    /// </summary>
    public bool CommandCompletionNotificationsEnabled { get; init; }
}
