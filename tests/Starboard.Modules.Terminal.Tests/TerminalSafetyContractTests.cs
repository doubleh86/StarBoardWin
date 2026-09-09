using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Tests.TestDoubles;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalSafetyContractTests
{
    private static readonly Guid SessionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly TerminalConfirmationRequestId RequestId =
        new(Guid.Parse("20000000-0000-0000-0000-000000000001"));

    [TestMethod]
    public void ConfirmationResponseForCurrentRequestAndGenerationCanBeApplied()
    {
        var session = new TerminalSessionReference(SessionId, 3);
        var token = new TerminalConfirmationToken(RequestId, session);
        var response = new TerminalConfirmationResponse(token, TerminalConfirmationResult.Confirmed);
        var target = new TerminalSafetyContractTestDouble(token, session);

        var applied = target.TryApply(response);

        Assert.IsTrue(applied);
        CollectionAssert.AreEqual(new[] { TerminalConfirmationResult.Confirmed }, target.AppliedResults);
    }

    [TestMethod]
    public void ConfirmationResponseAfterSessionRestartIsRejected()
    {
        var originalSession = new TerminalSessionReference(SessionId, 3);
        var restartedSession = new TerminalSessionReference(SessionId, 4);
        var token = new TerminalConfirmationToken(RequestId, originalSession);
        var response = new TerminalConfirmationResponse(token, TerminalConfirmationResult.Confirmed);
        var target = new TerminalSafetyContractTestDouble(token, restartedSession);

        var applied = target.TryApply(response);

        Assert.IsFalse(applied);
        Assert.HasCount(0, target.AppliedResults);
    }

    [TestMethod]
    public void ReplacedConfirmationRequestRejectsLateResponse()
    {
        var session = new TerminalSessionReference(SessionId, 3);
        var originalToken = new TerminalConfirmationToken(RequestId, session);
        var replacementToken = new TerminalConfirmationToken(
            new TerminalConfirmationRequestId(Guid.Parse("20000000-0000-0000-0000-000000000002")), session);
        var response = new TerminalConfirmationResponse(originalToken, TerminalConfirmationResult.Confirmed);
        var target = new TerminalSafetyContractTestDouble(replacementToken, session);

        var applied = target.TryApply(response);

        Assert.IsFalse(applied);
        Assert.HasCount(0, target.AppliedResults);
    }

    [TestMethod]
    public void CancelledConfirmationPreservesExplicitResult()
    {
        var session = new TerminalSessionReference(SessionId, 1);
        var token = new TerminalConfirmationToken(RequestId, session);
        var response = new TerminalConfirmationResponse(token, TerminalConfirmationResult.Cancelled);
        var target = new TerminalSafetyContractTestDouble(token, session);

        var applied = target.TryApply(response);

        Assert.IsTrue(applied);
        CollectionAssert.AreEqual(new[] { TerminalConfirmationResult.Cancelled }, target.AppliedResults);
    }

    [TestMethod]
    public void RuntimeSessionReferenceWithInvalidIdentityOrGenerationThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new TerminalSessionReference(Guid.Empty, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TerminalSessionReference(SessionId, 0));
    }

    [TestMethod]
    public void ConfirmationTokenWithDefaultIdentifiersThrows()
    {
        var session = new TerminalSessionReference(SessionId, 1);

        Assert.ThrowsExactly<ArgumentException>(() => new TerminalConfirmationToken(default, session));
        Assert.ThrowsExactly<ArgumentException>(() => new TerminalConfirmationToken(RequestId, default));
    }

    [TestMethod]
    public void PasteConfirmationCapturesOnlyMultilineClipboardSnapshot()
    {
        var session = new TerminalSessionReference(SessionId, 1);
        var token = new TerminalConfirmationToken(RequestId, session);

        var request = new TerminalPasteConfirmationRequest(token, "PowerShell", "Get-Location\r\n");

        Assert.AreEqual("Get-Location\r\n", request.ClipboardText);
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TerminalPasteConfirmationRequest(token, "PowerShell", "Get-Location"));
    }

    [TestMethod]
    public void NewOutputSnapshotOnlyReturnsExactSessionGeneration()
    {
        var originalSession = new TerminalSessionReference(SessionId, 6);
        var restartedSession = new TerminalSessionReference(SessionId, 7);
        var source = new List<TerminalSessionNewOutputState>
        {
            new(originalSession, true),
        };
        var snapshot = new TerminalNewOutputStateSnapshot(source);
        source.Clear();

        var foundOriginal = snapshot.TryGetState(originalSession, out var originalState);
        var foundRestarted = snapshot.TryGetState(restartedSession, out _);

        Assert.IsTrue(foundOriginal);
        Assert.IsTrue(originalState.HasNewOutput);
        Assert.IsFalse(foundRestarted);
        Assert.HasCount(1, snapshot.States);
    }

    [TestMethod]
    public void NewOutputSnapshotWithDuplicateSessionGenerationThrows()
    {
        var session = new TerminalSessionReference(SessionId, 1);
        var states = new[]
        {
            new TerminalSessionNewOutputState(session, false),
            new TerminalSessionNewOutputState(session, true),
        };

        Assert.ThrowsExactly<ArgumentException>(() => new TerminalNewOutputStateSnapshot(states));
    }

    [TestMethod]
    public void NewOutputSnapshotWithTwoGenerationsOfSameSessionThrows()
    {
        var states = new[]
        {
            new TerminalSessionNewOutputState(new TerminalSessionReference(SessionId, 1), false),
            new TerminalSessionNewOutputState(new TerminalSessionReference(SessionId, 2), true),
        };

        Assert.ThrowsExactly<ArgumentException>(() => new TerminalNewOutputStateSnapshot(states));
    }
}
