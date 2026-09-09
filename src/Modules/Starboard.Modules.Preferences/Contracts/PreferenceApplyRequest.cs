namespace Starboard.Modules.Preferences.Contracts;

/// <summary>
/// Captures both sides of a settings transaction before any live subsystem is changed.
/// </summary>
public sealed record PreferenceApplyRequest(AppSettings PreviousSettings, AppSettings RequestedSettings);
