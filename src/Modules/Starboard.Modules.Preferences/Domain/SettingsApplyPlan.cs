using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Domain;

internal static class SettingsApplyPlan
{
    internal static IReadOnlyList<PreferenceApplyStep> ApplyOrder { get; } =
    [
        PreferenceApplyStep.Validation,
        PreferenceApplyStep.TerminalSettings,
        PreferenceApplyStep.DesktopSettings,
        PreferenceApplyStep.Persistence,
    ];

    internal static IReadOnlyList<PreferenceApplyStep> RollbackOrder { get; } =
    [
        PreferenceApplyStep.DesktopSettings,
        PreferenceApplyStep.TerminalSettings,
    ];
}
