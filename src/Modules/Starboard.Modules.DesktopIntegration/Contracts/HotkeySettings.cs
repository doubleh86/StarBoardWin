namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// Canonical global shortcut gestures owned by desktop integration.
/// </summary>
/// <remarks>
/// Replacement must either preserve the previous registrations or report their restored or
/// incomplete effective state through DesktopSettingsApplyResult.
/// </remarks>
public sealed record HotkeySettings(string ExpandShortcut, string ActivationShortcut);
