namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Opaque correlation for one command execution. It never contains the command text.
/// </summary>
public readonly record struct TerminalCommandExecutionId
{
    public TerminalCommandExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The terminal command execution identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalCommandExecutionId CreateNew()
    {
        return new TerminalCommandExecutionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}

/// <summary>
/// Exit status reported by the supported shell integration. Zero means success; every other value means failure.
/// </summary>
public readonly record struct TerminalCommandExitResult(int ExitCode)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// A command completion proven by matching explicit start and finish signals from one shell lifetime.
/// Command text and terminal output are intentionally absent from this contract.
/// </summary>
public sealed record TerminalCommandCompletion
{
    public TerminalCommandCompletion(TerminalCommandExecutionId executionId, TerminalSessionReference session,
                                     TerminalCommandExitResult exitResult)
    {
        if (executionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Command completion requires a valid execution identifier.",
                                        nameof(executionId));
        }

        if (session.SessionId == Guid.Empty || session.Generation < 1)
        {
            throw new ArgumentException("Command completion requires a valid runtime session reference.",
                                        nameof(session));
        }

        ExecutionId = executionId;
        Session = session;
        ExitResult = exitResult;
    }

    public TerminalCommandExecutionId ExecutionId { get; }

    public TerminalSessionReference Session { get; }

    public TerminalCommandExitResult ExitResult { get; }

    public bool IsCurrent(TerminalSessionReference currentSession)
    {
        return Session == currentSession;
    }
}

public sealed class TerminalCommandCompletedEventArgs : EventArgs
{
    public TerminalCommandCompletedEventArgs(TerminalCommandCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        Completion = completion;
    }

    public TerminalCommandCompletion Completion { get; }
}
