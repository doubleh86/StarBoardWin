using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalLaunchContractTests
{
    private static readonly string[] BuiltInShellKindNames = ["Automatic", "Pwsh", "PowerShell", "Cmd"];
    private static readonly TerminalTabRequestId RequestId = new(
        Guid.Parse("40000000-0000-0000-0000-000000000001"));
    private static readonly TerminalSessionReference SourceSession = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"), 7);

    [TestMethod]
    public void CreateWslProfileUsesStableDataIdentifierWithoutExtendingShellKind()
    {
        var profile = TerminalLaunchProfile.CreateWsl("Ubuntu-24.04", "Ubuntu 24.04");

        CollectionAssert.AreEqual(BuiltInShellKindNames, Enum.GetNames<TerminalShellKind>());
        Assert.AreEqual("wsl:Ubuntu-24.04", profile.ProfileId.ToString());
        Assert.AreEqual(TerminalLaunchProfileKind.WslDistribution, profile.Kind);
        Assert.AreEqual("Ubuntu-24.04", profile.WslDistributionName);
        Assert.IsNull(profile.ShellKind);
    }

    [TestMethod]
    public void QueryResultCopiesProfilesAndKeepsDiscoveryFailureRetryable()
    {
        var source = new List<TerminalLaunchProfile>
        {
            TerminalLaunchProfile.CreateBuiltIn(TerminalShellKind.Pwsh, "PowerShell 7"),
        };
        var result = new TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus.WslDiscoveryFailed,
                                                          source, "WSL 목록을 불러오지 못했습니다.");

        source.Clear();

        Assert.AreEqual(1, result.Profiles.Count);
        Assert.AreEqual("shell:pwsh", result.Profiles[0].ProfileId.ToString());
        Assert.IsTrue(result.CanRetry);
        Assert.AreEqual("WSL 목록을 불러오지 못했습니다.", result.FailureMessage);
    }

    [TestMethod]
    public void QueryResultWithDuplicateProfileIdentifiersThrows()
    {
        var first = TerminalLaunchProfile.CreateWsl("Ubuntu");
        var duplicate = TerminalLaunchProfile.CreateWsl("Ubuntu", "Ubuntu duplicate label");

        Assert.ThrowsExactly<ArgumentException>(() =>
            new TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus.Succeeded,
                                                 [first, duplicate]));
    }

    [TestMethod]
    public void NewTabRequestAcceptsOnlyMatchingRequestAndCurrentSourceGeneration()
    {
        var token = new TerminalTabRequestToken(RequestId, SourceSession);
        var request = new TerminalNewTabRequest(token,
                                                TerminalLaunchProfileId.FromShell(TerminalShellKind.Pwsh));
        var retry = new TerminalNewTabRequest(token,
                                              TerminalLaunchProfileId.FromShell(TerminalShellKind.Pwsh));
        var newAction = new TerminalNewTabRequest(
            new TerminalTabRequestToken(TerminalTabRequestId.CreateNew(), SourceSession), request.ProfileId);
        var restartedSource = new TerminalSessionReference(SourceSession.SessionId, 8);

        Assert.IsTrue(request.CanApplyTo(retry, SourceSession));
        Assert.AreEqual(SourceSession.SessionId, request.SessionId);
        Assert.AreEqual(7, request.SessionGeneration);
        Assert.IsFalse(request.CanApplyTo(newAction, SourceSession));
        Assert.IsFalse(request.CanApplyTo(retry, restartedSource));
    }

    [TestMethod]
    public void DuplicateTabRequestAcceptsOnlyMatchingRequestAndCurrentSourceGeneration()
    {
        var request = new TerminalTabDuplicateRequest(new TerminalTabRequestToken(RequestId, SourceSession));
        var retry = new TerminalTabDuplicateRequest(new TerminalTabRequestToken(RequestId, SourceSession));
        var restartedSource = new TerminalSessionReference(SourceSession.SessionId, 8);

        Assert.IsTrue(request.CanApplyTo(retry, SourceSession));
        Assert.AreEqual(SourceSession.SessionId, request.SessionId);
        Assert.AreEqual(7, request.SessionGeneration);
        Assert.IsFalse(request.CanApplyTo(retry, restartedSource));
        Assert.IsFalse(request.CanApplyTo(null, SourceSession));
    }

    [TestMethod]
    public async Task HeightChangeCallbackCanReturnSavedHeightAfterPersistenceRollback()
    {
        var requestId = new TerminalCollapsedHeightChangeRequestId(
            Guid.Parse("50000000-0000-0000-0000-000000000001"));
        var request = new TerminalCollapsedHeightChangeRequest(requestId,
                                                               TerminalCollapsedHeightChangePhase.Commit,
                                                               requestedHeightDip: 360, lastSavedHeightDip: 200);
        TerminalCollapsedHeightChangeCallback callback = (change, _) =>
            ValueTask.FromResult(new TerminalCollapsedHeightChangeResult(
                change.RequestId, TerminalCollapsedHeightChangeStatus.Reverted, change.LastSavedHeightDip,
                "높이를 저장하지 못했습니다."));

        var result = await callback(request, CancellationToken.None);

        Assert.IsTrue(result.CanApplyTo(request));
        Assert.AreEqual(TerminalCollapsedHeightChangeStatus.Reverted, result.Status);
        Assert.AreEqual(200, result.AppliedHeightDip);
        Assert.AreEqual("높이를 저장하지 못했습니다.", result.FailureMessage);
    }
}
