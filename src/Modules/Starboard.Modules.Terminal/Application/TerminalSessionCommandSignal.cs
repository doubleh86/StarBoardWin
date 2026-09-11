using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal enum TerminalSessionCommandSignalKind
{
    Ready,
    Started,
    Finished,
    IntegrationLost,
}

/// <summary>
/// A private shell-integration signal. Command text and terminal output are deliberately excluded.
/// </summary>
internal readonly record struct TerminalSessionCommandSignal
{
    private TerminalSessionCommandSignal(TerminalSessionCommandSignalKind kind,
                                         TerminalCommandExecutionId executionId, int? exitCode)
    {
        Kind = kind;
        ExecutionId = executionId;
        ExitCode = exitCode;
    }

    internal TerminalSessionCommandSignalKind Kind { get; }

    internal TerminalCommandExecutionId ExecutionId { get; }

    internal int? ExitCode { get; }

    internal static TerminalSessionCommandSignal Ready()
    {
        return new TerminalSessionCommandSignal(TerminalSessionCommandSignalKind.Ready, default, null);
    }

    internal static TerminalSessionCommandSignal Started(TerminalCommandExecutionId executionId)
    {
        ValidateExecutionId(executionId);
        return new TerminalSessionCommandSignal(TerminalSessionCommandSignalKind.Started, executionId, null);
    }

    internal static TerminalSessionCommandSignal Finished(TerminalCommandExecutionId executionId, int? exitCode)
    {
        ValidateExecutionId(executionId);
        return new TerminalSessionCommandSignal(TerminalSessionCommandSignalKind.Finished, executionId, exitCode);
    }

    internal static TerminalSessionCommandSignal IntegrationLost()
    {
        return new TerminalSessionCommandSignal(TerminalSessionCommandSignalKind.IntegrationLost, default, null);
    }

    private static void ValidateExecutionId(TerminalCommandExecutionId executionId)
    {
        if (executionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A session command signal requires a valid execution identifier.",
                                        nameof(executionId));
        }
    }
}

internal readonly record struct TerminalSessionCommandStarted(TerminalSessionReference Session,
                                                              TerminalCommandExecutionId ExecutionId);

internal readonly record struct TerminalSessionCommandFinished(TerminalSessionReference Session,
                                                               TerminalCommandExecutionId ExecutionId,
                                                               int? ExitCode);
