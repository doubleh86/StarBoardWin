using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Windows.Composition;

internal sealed class CommandCompletionNotificationCoordinator
{
    private readonly Func<TerminalSessionReference, bool> isCurrentSession;
    private readonly Action<CommandCompletionNotificationSettings> applySettings;
    private readonly Action<CommandCompletionNotificationRequest> notifyCompletion;
    private readonly Lock stateLock = new();
    private bool isStopped;

    internal CommandCompletionNotificationCoordinator(Func<TerminalSessionReference, bool> isCurrentSession,
                                                      Action<CommandCompletionNotificationSettings> applySettings,
                                                      Action<CommandCompletionNotificationRequest> notifyCompletion)
    {
        ArgumentNullException.ThrowIfNull(isCurrentSession);
        ArgumentNullException.ThrowIfNull(applySettings);
        ArgumentNullException.ThrowIfNull(notifyCompletion);
        this.isCurrentSession = isCurrentSession;
        this.applySettings = applySettings;
        this.notifyCompletion = notifyCompletion;
    }

    internal void ApplySettings(bool enabled)
    {
        lock (stateLock)
        {
            if (isStopped == true)
            {
                return;
            }

            applySettings(new CommandCompletionNotificationSettings(enabled));
        }
    }

    internal void HandleCompletion(TerminalCommandCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        lock (stateLock)
        {
            if (isStopped == true || isCurrentSession(completion.Session) == false)
            {
                return;
            }

            var request = new CommandCompletionNotificationRequest(
                completion.Session.SessionId, completion.Session.Generation,
                completion.ExecutionId.Value, completion.ExitResult.ExitCode);
            notifyCompletion(request);
        }
    }

    internal void Stop()
    {
        lock (stateLock)
        {
            isStopped = true;
        }
    }
}
