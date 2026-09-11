using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalCommandLifecycleTests
{
    private static readonly Guid SessionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly TerminalCommandExecutionId ExecutionId =
        new(Guid.Parse("20000000-0000-0000-0000-000000000001"));

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(-1073741510, false)]
    public void ExplicitStartAndFinishProduceCompletionWithExitResult(int exitCode, bool succeeded)
    {
        var session = new TerminalSessionReference(SessionId, 3);
        var tracker = new TerminalCommandLifecycleTracker(session);

        var started = tracker.TryApply(TerminalShellIntegrationSignal.CommandStarted(session, ExecutionId),
                                       out var startCompletion);
        var finished = tracker.TryApply(TerminalShellIntegrationSignal.CommandFinished(session, ExecutionId, exitCode),
                                        out var completion);

        Assert.IsTrue(started);
        Assert.IsNull(startCompletion);
        Assert.IsTrue(finished);
        Assert.IsNotNull(completion);
        Assert.AreEqual(ExecutionId, completion.ExecutionId);
        Assert.AreEqual(session, completion.Session);
        Assert.AreEqual(exitCode, completion.ExitResult.ExitCode);
        Assert.AreEqual(succeeded, completion.ExitResult.Succeeded);
        Assert.AreEqual(TerminalCommandLifecycleState.Ready, tracker.State);
    }

    [TestMethod]
    public void CompletionFromRestartedPreviousGenerationIsRejectedWithoutPoisoningCurrentTracker()
    {
        var previousSession = new TerminalSessionReference(SessionId, 3);
        var currentSession = new TerminalSessionReference(SessionId, 4);
        var tracker = new TerminalCommandLifecycleTracker(currentSession);

        var lateSignalAccepted = tracker.TryApply(
            TerminalShellIntegrationSignal.CommandFinished(previousSession, ExecutionId, 0), out var lateCompletion);
        var startAccepted = tracker.TryApply(
            TerminalShellIntegrationSignal.CommandStarted(currentSession, ExecutionId), out _);
        var finishAccepted = tracker.TryApply(
            TerminalShellIntegrationSignal.CommandFinished(currentSession, ExecutionId, 0), out var completion);

        Assert.IsFalse(lateSignalAccepted);
        Assert.IsNull(lateCompletion);
        Assert.IsTrue(startAccepted);
        Assert.IsTrue(finishAccepted);
        Assert.IsNotNull(completion);
        Assert.IsTrue(completion.IsCurrent(currentSession));
        Assert.IsFalse(completion.IsCurrent(previousSession));
    }

    [TestMethod]
    public void FinishWithoutMatchingStartDisablesGenerationAndNeverProducesCompletion()
    {
        var session = new TerminalSessionReference(SessionId, 1);
        var tracker = new TerminalCommandLifecycleTracker(session);

        var finishAccepted = tracker.TryApply(
            TerminalShellIntegrationSignal.CommandFinished(session, ExecutionId, 0), out var completion);
        var laterStartAccepted = tracker.TryApply(
            TerminalShellIntegrationSignal.CommandStarted(session, ExecutionId), out var laterCompletion);

        Assert.IsFalse(finishAccepted);
        Assert.IsNull(completion);
        Assert.AreEqual(TerminalCommandLifecycleState.Unavailable, tracker.State);
        Assert.IsFalse(laterStartAccepted);
        Assert.IsNull(laterCompletion);
    }

    [TestMethod]
    public void MismatchedExecutionFinishDisablesGenerationAndNeverProducesCompletion()
    {
        var session = new TerminalSessionReference(SessionId, 1);
        var otherExecutionId = new TerminalCommandExecutionId(
            Guid.Parse("20000000-0000-0000-0000-000000000002"));
        var tracker = new TerminalCommandLifecycleTracker(session);
        _ = tracker.TryApply(TerminalShellIntegrationSignal.CommandStarted(session, ExecutionId), out _);

        var accepted = tracker.TryApply(
            TerminalShellIntegrationSignal.CommandFinished(session, otherExecutionId, 0), out var completion);

        Assert.IsFalse(accepted);
        Assert.IsNull(completion);
        Assert.AreEqual(TerminalCommandLifecycleState.Unavailable, tracker.State);
    }

    [TestMethod]
    public void IntegrationLossAbandonsActiveCommandWithoutCompletion()
    {
        var session = new TerminalSessionReference(SessionId, 1);
        var tracker = new TerminalCommandLifecycleTracker(session);
        _ = tracker.TryApply(TerminalShellIntegrationSignal.CommandStarted(session, ExecutionId), out _);

        var accepted = tracker.TryApply(TerminalShellIntegrationSignal.IntegrationLost(session),
                                        out var completion);

        Assert.IsTrue(accepted);
        Assert.IsNull(completion);
        Assert.AreEqual(TerminalCommandLifecycleState.Unavailable, tracker.State);
    }

    [TestMethod]
    public void CommandCompletionContractsRejectDefaultCorrelationsAndContainNoTextPayload()
    {
        var session = new TerminalSessionReference(SessionId, 1);

        Assert.ThrowsExactly<ArgumentException>(() => new TerminalCommandExecutionId(Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TerminalCommandCompletion(default, session, new TerminalCommandExitResult(0)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new TerminalCommandCompletion(ExecutionId, default, new TerminalCommandExitResult(0)));

        var stringProperties = new[]
        {
            typeof(TerminalCommandCompletion),
            typeof(TerminalCommandExitResult),
            typeof(TerminalCommandCompletedEventArgs),
        }.SelectMany(type => type.GetProperties())
         .Where(property => property.PropertyType == typeof(string))
         .ToArray();

        Assert.AreEqual(0, stringProperties.Length,
                        "Completion contracts must not expose command text or terminal output.");
    }
}
