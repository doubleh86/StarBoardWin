using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Preferences.Domain;

namespace Starboard.Modules.Preferences.Tests.Contracts;

[TestClass]
public sealed class PreferenceApplyContractTests
{
    [TestMethod]
    public void ApplyOrderPersistsOnlyAfterLiveSubsystemsApply()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                PreferenceApplyStep.Validation,
                PreferenceApplyStep.TerminalSettings,
                PreferenceApplyStep.DesktopSettings,
                PreferenceApplyStep.Persistence,
            },
            SettingsApplyPlan.ApplyOrder.ToArray());
    }

    [TestMethod]
    public void RollbackOrderReversesLiveSubsystemsToPreviousSnapshot()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                PreferenceApplyStep.DesktopSettings,
                PreferenceApplyStep.TerminalSettings,
            },
            SettingsApplyPlan.RollbackOrder.ToArray());
    }

    [TestMethod]
    public void ResultWithRollbackFailurePreservesAllSnapshots()
    {
        var previous = new AppSettings();
        var requested = previous with
        {
            Theme = "Dark",
            StartWithWindows = true,
        };
        var effective = previous with
        {
            Theme = "Dark",
        };
        var failure = new PreferenceApplyFailure(
            PreferenceApplyStep.DesktopSettings,
            PreferenceApplyFailureStage.Rollback,
            "Startup rollback failed.");
        var result = new PreferenceApplyResult(
            new PreferenceApplyRequest(previous, requested),
            PreferenceApplyStatus.FailedAndRestoreIncomplete,
            effective,
            previous,
            [failure]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.PreviousSnapshotRestored);
        Assert.AreEqual(previous, result.Request.PreviousSettings);
        Assert.AreEqual(requested, result.Request.RequestedSettings);
        Assert.AreEqual(effective, result.EffectiveSettings);
        Assert.AreEqual(previous, result.PersistedSettings);
        Assert.AreEqual(PreferenceApplyFailureStage.Rollback, result.Failures[0].Stage);
    }

    [TestMethod]
    public void ResultWithSuccessfulRollbackReportsPreviousSnapshotRestored()
    {
        var previous = new AppSettings();
        var requested = previous with
        {
            ExpandShortcut = "Ctrl+Shift+E",
        };
        var result = new PreferenceApplyResult(
            new PreferenceApplyRequest(previous, requested),
            PreferenceApplyStatus.FailedAndRestored,
            previous,
            previous,
            [
                new PreferenceApplyFailure(
                    PreferenceApplyStep.DesktopSettings,
                    PreferenceApplyFailureStage.Apply,
                    "Shortcut is already registered."),
            ]);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.PreviousSnapshotRestored);
        Assert.AreEqual(previous, result.EffectiveSettings);
        Assert.AreEqual(previous, result.PersistedSettings);
    }
}
