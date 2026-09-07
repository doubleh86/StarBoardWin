namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Live terminal settings and the default used to create future tabs.
/// </summary>
/// <remarks>
/// Applying Appearance must preserve every existing terminal session. A change to
/// DefaultShellExecutable applies only to tabs created afterward. Existing tabs retain
/// the shell captured when they were created, including when that tab is restarted.
/// A null default selects the module's normal shell discovery order.
/// </remarks>
public sealed record TerminalSettings(
    TerminalAppearanceSettings Appearance,
    string? DefaultShellExecutable);

public sealed record TerminalSettingsApplyResult(
    TerminalSettings RequestedSettings,
    TerminalSettings PreviousSettings,
    TerminalSettings EffectiveSettings,
    TerminalSettingsApplyStatus Status,
    string? FailureMessage);

public enum TerminalSettingsApplyStatus
{
    Applied,
    FailedWithoutChange,
    FailedAndRestored,
    FailedAndRestoreIncomplete,
}
