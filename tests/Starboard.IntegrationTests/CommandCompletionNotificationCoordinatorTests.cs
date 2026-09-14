using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Windows.Composition;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class CommandCompletionNotificationCoordinatorTests
{
    [TestMethod]
    public void CurrentGenerationDeliversOnceOnlyWhenEnabled()
    {
        var desktopBoundary = new FakeDesktopNotificationBoundary();
        var currentSession = CreateSession(generation: 3);
        var coordinator = new CommandCompletionNotificationCoordinator(
            session => session == currentSession,
            desktopBoundary.ApplySettings,
            desktopBoundary.Notify);
        var completion = CreateCompletion(currentSession, exitCode: 0);

        coordinator.ApplySettings(false);
        coordinator.HandleCompletion(completion);
        coordinator.ApplySettings(true);
        coordinator.HandleCompletion(completion);

        Assert.HasCount(1, desktopBoundary.Notifications);
        var request = desktopBoundary.Notifications[0];
        Assert.AreEqual(currentSession.SessionId, request.SessionId);
        Assert.AreEqual(3, request.SessionGeneration);
        Assert.AreEqual(completion.ExecutionId.Value, request.ExecutionId);
        Assert.AreEqual(0, request.ExitCode);
    }

    [TestMethod]
    public void StaleGenerationAndStoppedCoordinatorDoNotDeliver()
    {
        var desktopBoundary = new FakeDesktopNotificationBoundary();
        var currentSession = CreateSession(generation: 4);
        var staleSession = new TerminalSessionReference(currentSession.SessionId, generation: 3);
        var coordinator = new CommandCompletionNotificationCoordinator(
            session => session == currentSession,
            desktopBoundary.ApplySettings,
            desktopBoundary.Notify);
        coordinator.ApplySettings(true);

        coordinator.HandleCompletion(CreateCompletion(staleSession, exitCode: 1));
        coordinator.Stop();
        coordinator.HandleCompletion(CreateCompletion(currentSession, exitCode: 1));
        coordinator.ApplySettings(false);

        Assert.IsEmpty(desktopBoundary.Notifications);
        Assert.HasCount(1, desktopBoundary.Settings);
        Assert.IsTrue(desktopBoundary.Settings[0]);
    }

    private static TerminalSessionReference CreateSession(long generation)
    {
        return new TerminalSessionReference(
            Guid.Parse("10000000-0000-0000-0000-000000000001"), generation);
    }

    private static TerminalCommandCompletion CreateCompletion(TerminalSessionReference session, int exitCode)
    {
        return new TerminalCommandCompletion(
            new TerminalCommandExecutionId(Guid.NewGuid()), session,
            new TerminalCommandExitResult(exitCode));
    }

    private sealed class FakeDesktopNotificationBoundary
    {
        private bool enabled;

        internal List<bool> Settings { get; } = [];

        internal List<CommandCompletionNotificationRequest> Notifications { get; } = [];

        internal void ApplySettings(CommandCompletionNotificationSettings settings)
        {
            enabled = settings.Enabled;
            Settings.Add(enabled);
        }

        internal void Notify(CommandCompletionNotificationRequest request)
        {
            if (enabled == true)
            {
                Notifications.Add(request);
            }
        }
    }
}
