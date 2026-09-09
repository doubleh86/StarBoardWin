namespace Starboard.Modules.DesktopIntegration.Contracts;

public sealed record DesktopSettings(double CollapsedHeightDip, double Opacity,
                                     PreferredMonitorBehavior PreferredMonitorBehavior, HotkeySettings Hotkeys,
                                     StartupSettings Startup);

public enum PreferredMonitorBehavior
{
    TaskbarMonitor,
}

/// <summary>
/// Reports the effective desktop snapshot after apply and any attempted rollback.
/// </summary>
public sealed record DesktopSettingsApplyResult(DesktopSettings RequestedSettings, DesktopSettings PreviousSettings,
                                                DesktopSettings EffectiveSettings, DesktopSettingsApplyStatus Status,
                                                IReadOnlyList<DesktopSettingsOperationResult> Operations);

public enum DesktopSettingsApplyStatus
{
    Applied,
    FailedWithoutChange,
    FailedAndRestored,
    FailedAndRestoreIncomplete,
}

public sealed record DesktopSettingsOperationResult(DesktopSettingsOperation Operation,
                                                    DesktopSettingsOperationStatus Status, string? FailureMessage);

public enum DesktopSettingsOperation
{
    PanelAppearance,
    MonitorBehavior,
    Hotkeys,
    Startup,
}

public enum DesktopSettingsOperationStatus
{
    Applied,
    Unchanged,
    Failed,
    Restored,
    RestoreFailed,
}
