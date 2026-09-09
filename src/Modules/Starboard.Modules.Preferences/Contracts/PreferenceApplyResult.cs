namespace Starboard.Modules.Preferences.Contracts;

/// <summary>
/// Describes the persisted and live outcome of one settings transaction.
/// </summary>
/// <remarks>
/// EffectiveSettings may differ from PersistedSettings only when apply or rollback failed.
/// Callers must surface that mismatch instead of reporting a successful save.
/// </remarks>
public sealed record PreferenceApplyResult(PreferenceApplyRequest Request, PreferenceApplyStatus Status,
                                           AppSettings EffectiveSettings, AppSettings PersistedSettings,
                                           IReadOnlyList<PreferenceApplyFailure> Failures)
{
    public bool Succeeded => Status == PreferenceApplyStatus.Applied;

    public bool PreviousSnapshotRestored =>
        Status == PreferenceApplyStatus.Rejected ||
        Status == PreferenceApplyStatus.FailedAndRestored;
}

public enum PreferenceApplyStatus
{
    Applied,
    Rejected,
    FailedAndRestored,
    FailedAndRestoreIncomplete,
}

public sealed record PreferenceApplyFailure(PreferenceApplyStep Step, PreferenceApplyFailureStage Stage, string Message);

public enum PreferenceApplyStep
{
    Validation,
    TerminalSettings,
    DesktopSettings,
    Persistence,
}

public enum PreferenceApplyFailureStage
{
    Validation,
    Apply,
    Rollback,
}
