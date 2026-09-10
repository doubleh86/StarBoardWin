using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalSavedTabContractTests
{
    private static readonly TerminalSavedTabId SavedTabId = new(
        Guid.Parse("30000000-0000-0000-0000-000000000001"));
    private static readonly TerminalSavedTabRequestId RequestId = new(
        Guid.Parse("40000000-0000-0000-0000-000000000001"));

    [TestMethod]
    public void ContractPublishesPersistenceAndRuntimeLimits()
    {
        var contractType = typeof(TerminalSavedTabsContract);

        Assert.AreEqual(1, contractType.GetField(nameof(TerminalSavedTabsContract.CurrentSchemaVersion))
                              ?.GetRawConstantValue());
        Assert.AreEqual(20, contractType.GetField(nameof(TerminalSavedTabsContract.MaximumSavedTabs))
                               ?.GetRawConstantValue());
        Assert.AreEqual(8, contractType.GetField(nameof(TerminalSavedTabsContract.MaximumRunningTabs))
                              ?.GetRawConstantValue());
    }

    [TestMethod]
    public void SavedTabContainsOnlyReusableLaunchDefinition()
    {
        var savedTab = CreateSavedTab();

        Assert.AreEqual(SavedTabId, savedTab.SavedTabId);
        Assert.AreEqual("서버", savedTab.Name);
        Assert.AreEqual("C:\\Work\\Server", savedTab.StartingDirectory);
        Assert.AreEqual(TerminalShellKind.Pwsh, savedTab.ShellKind);
        Assert.IsFalse(typeof(TerminalSavedTab).GetProperties()
                       .Any(property => property.Name.Contains("Session", StringComparison.Ordinal) ||
                                        property.Name.Contains("Configuration", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("..\\Work")]
    [DataRow("\\\\server\\share")]
    [DataRow("C:\\%USERPROFILE%")]
    [DataRow("C:\\$(Get-Location)")]
    public void SavedTabRejectsUnsupportedStartingDirectory(string startingDirectory)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TerminalSavedTab(SavedTabId, "서버", startingDirectory, TerminalShellKind.Pwsh));
    }

    [TestMethod]
    public void SnapshotCapturesListAndAllowsDuplicateNames()
    {
        var source = new List<TerminalSavedTab>
        {
            CreateSavedTab(),
            new(new TerminalSavedTabId(CreateGuid(2)), "서버", "D:\\Work", TerminalShellKind.Cmd),
        };
        var snapshot = new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, source);

        source.Clear();

        Assert.HasCount(2, snapshot.Tabs);
        Assert.AreEqual("C:\\Work\\Server", snapshot.Tabs[0].StartingDirectory);
        Assert.AreEqual("D:\\Work", snapshot.Tabs[1].StartingDirectory);
    }

    [TestMethod]
    public void SnapshotRejectsMoreThanTwentyItems()
    {
        var tabs = Enumerable.Range(1, TerminalSavedTabsContract.MaximumSavedTabs + 1)
            .Select(index => new TerminalSavedTab(new TerminalSavedTabId(CreateGuid(index)), $"탭 {index}",
                                                  $"C:\\Work\\{index}", TerminalShellKind.Automatic))
            .ToArray();

        Assert.ThrowsExactly<ArgumentException>(() =>
            new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, tabs));
    }

    [TestMethod]
    public void LaunchRequestIdentityDistinguishesDuplicateFromNewLaunch()
    {
        var first = new TerminalSavedTabLaunchRequest(RequestId, SavedTabId);
        var retry = new TerminalSavedTabLaunchRequest(RequestId, SavedTabId);
        var later = new TerminalSavedTabLaunchRequest(new TerminalSavedTabRequestId(CreateGuid(2)), SavedTabId);

        Assert.IsTrue(retry.IsDuplicateOf(first));
        Assert.IsFalse(later.IsDuplicateOf(first));
    }

    [TestMethod]
    public void CancellationAndResultApplyOnlyToExactPendingLaunch()
    {
        var pending = new TerminalSavedTabLaunchRequest(RequestId, SavedTabId);
        var cancellation = new TerminalSavedTabLaunchCancellation(RequestId);
        var result = new TerminalSavedTabLaunchResult(RequestId, SavedTabId,
                                                      TerminalSavedTabLaunchStatus.Cancelled, null, null);
        var unrelated = new TerminalSavedTabLaunchRequest(new TerminalSavedTabRequestId(CreateGuid(2)), SavedTabId);

        Assert.IsTrue(cancellation.Cancels(pending));
        Assert.IsTrue(result.CanApplyTo(pending));
        Assert.IsFalse(result.CanApplyTo(unrelated));
        Assert.IsFalse(result.CanApplyTo(null));
    }

    [TestMethod]
    public void WorkspaceAndSavedTabPersistenceContractsRemainIndependent()
    {
        var workspacePropertyTypes = typeof(TerminalWorkspaceConfiguration).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();
        var savedTabPropertyTypes = typeof(TerminalSavedTabsSnapshot).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.IsFalse(workspacePropertyTypes.Contains(typeof(TerminalSavedTabsSnapshot)));
        Assert.IsFalse(savedTabPropertyTypes.Contains(typeof(TerminalWorkspaceConfiguration)));
        Assert.AreNotEqual(typeof(TerminalTabConfigurationId), typeof(TerminalSavedTabId));
    }

    [TestMethod]
    public void LaunchStatusesExplicitlyRepresentDuplicateCancellationAndRuntimeLimit()
    {
        Assert.IsTrue(Enum.IsDefined(TerminalSavedTabLaunchStatus.DuplicateRequest));
        Assert.IsTrue(Enum.IsDefined(TerminalSavedTabLaunchStatus.Cancelled));
        Assert.IsTrue(Enum.IsDefined(TerminalSavedTabLaunchStatus.RunningTabLimitReached));
    }

    private static TerminalSavedTab CreateSavedTab()
    {
        return new TerminalSavedTab(SavedTabId, "서버", "C:\\Work\\Server", TerminalShellKind.Pwsh);
    }

    private static Guid CreateGuid(int suffix)
    {
        return Guid.Parse($"30000000-0000-0000-0000-{suffix:D12}");
    }
}
