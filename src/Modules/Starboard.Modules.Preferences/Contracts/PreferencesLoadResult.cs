namespace Starboard.Modules.Preferences.Contracts;

public sealed record PreferencesLoadResult(
    AppSettings Settings,
    bool UsedDefaults,
    string? RecoveryMessage);
