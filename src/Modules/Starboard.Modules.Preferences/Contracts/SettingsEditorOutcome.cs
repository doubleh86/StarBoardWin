namespace Starboard.Modules.Preferences.Contracts;

public enum SettingsEditorCompletionKind
{
    Saved,
    Canceled,
}

public sealed record SettingsEditorOutcome(
    SettingsEditorCompletionKind CompletionKind,
    AppSettings Settings);
