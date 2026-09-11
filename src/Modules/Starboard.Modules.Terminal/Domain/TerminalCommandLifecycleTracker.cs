using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Domain;

internal enum TerminalCommandLifecycleState
{
    Ready,
    Executing,
    Unavailable,
}

internal enum TerminalShellIntegrationSignalKind
{
    CommandStarted,
    CommandFinished,
    IntegrationLost,
}

/// <summary>
/// Metadata-only control signal emitted by a supported shell integration. Terminal output is not a signal source.
/// </summary>
internal readonly record struct TerminalShellIntegrationSignal
{
    private TerminalShellIntegrationSignal(TerminalShellIntegrationSignalKind kind, TerminalSessionReference session,
                                           TerminalCommandExecutionId executionId, int? exitCode)
    {
        Kind = kind;
        Session = session;
        ExecutionId = executionId;
        ExitCode = exitCode;
    }

    internal TerminalShellIntegrationSignalKind Kind { get; }

    internal TerminalSessionReference Session { get; }

    internal TerminalCommandExecutionId ExecutionId { get; }

    internal int? ExitCode { get; }

    internal static TerminalShellIntegrationSignal CommandStarted(TerminalSessionReference session,
                                                                  TerminalCommandExecutionId executionId)
    {
        ValidateSessionAndExecution(session, executionId);
        return new TerminalShellIntegrationSignal(TerminalShellIntegrationSignalKind.CommandStarted, session,
                                                  executionId, null);
    }

    internal static TerminalShellIntegrationSignal CommandFinished(TerminalSessionReference session,
                                                                   TerminalCommandExecutionId executionId,
                                                                   int exitCode)
    {
        ValidateSessionAndExecution(session, executionId);
        return new TerminalShellIntegrationSignal(TerminalShellIntegrationSignalKind.CommandFinished, session,
                                                  executionId, exitCode);
    }

    internal static TerminalShellIntegrationSignal IntegrationLost(TerminalSessionReference session)
    {
        ValidateSession(session);
        return new TerminalShellIntegrationSignal(TerminalShellIntegrationSignalKind.IntegrationLost, session,
                                                  default, null);
    }

    private static void ValidateSessionAndExecution(TerminalSessionReference session,
                                                    TerminalCommandExecutionId executionId)
    {
        ValidateSession(session);
        if (executionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A shell integration command signal requires a valid execution identifier.",
                                        nameof(executionId));
        }
    }

    private static void ValidateSession(TerminalSessionReference session)
    {
        if (session.SessionId == Guid.Empty || session.Generation < 1)
        {
            throw new ArgumentException("A shell integration signal requires a valid runtime session reference.",
                                        nameof(session));
        }
    }
}

/// <summary>
/// Accepts only an ordered, explicit start/finish pair for one session generation. A protocol fault disables
/// completion for the rest of that generation rather than guessing from output or a prompt.
/// </summary>
internal sealed class TerminalCommandLifecycleTracker
{
    private readonly TerminalSessionReference _session;
    private TerminalCommandExecutionId? _activeExecutionId;

    internal TerminalCommandLifecycleTracker(TerminalSessionReference session)
    {
        if (session.SessionId == Guid.Empty || session.Generation < 1)
        {
            throw new ArgumentException("A command lifecycle tracker requires a valid runtime session reference.",
                                        nameof(session));
        }

        _session = session;
    }

    internal TerminalCommandLifecycleState State { get; private set; } = TerminalCommandLifecycleState.Ready;

    internal bool TryApply(TerminalShellIntegrationSignal signal, out TerminalCommandCompletion? completion)
    {
        completion = null;
        if (signal.Session != _session)
        {
            return false;
        }

        if (State == TerminalCommandLifecycleState.Unavailable)
        {
            return false;
        }

        if (signal.Kind == TerminalShellIntegrationSignalKind.IntegrationLost)
        {
            MarkUnavailable();
            return true;
        }

        if (signal.Kind == TerminalShellIntegrationSignalKind.CommandStarted &&
            State == TerminalCommandLifecycleState.Ready && signal.ExecutionId.Value != Guid.Empty &&
            signal.ExitCode is null)
        {
            _activeExecutionId = signal.ExecutionId;
            State = TerminalCommandLifecycleState.Executing;
            return true;
        }

        if (signal.Kind == TerminalShellIntegrationSignalKind.CommandFinished &&
            State == TerminalCommandLifecycleState.Executing &&
            _activeExecutionId == signal.ExecutionId && signal.ExitCode.HasValue == true)
        {
            completion = new TerminalCommandCompletion(signal.ExecutionId, _session,
                                                       new TerminalCommandExitResult(signal.ExitCode.Value));
            _activeExecutionId = null;
            State = TerminalCommandLifecycleState.Ready;
            return true;
        }

        MarkUnavailable();
        return false;
    }

    private void MarkUnavailable()
    {
        _activeExecutionId = null;
        State = TerminalCommandLifecycleState.Unavailable;
    }
}
