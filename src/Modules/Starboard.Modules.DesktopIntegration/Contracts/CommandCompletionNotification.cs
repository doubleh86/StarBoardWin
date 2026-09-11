namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// Opt-in state for generic command completion notifications. Disabled is the default.
/// </summary>
public readonly record struct CommandCompletionNotificationSettings(bool Enabled);

/// <summary>
/// Metadata-only request created by the host after it verifies the current terminal session generation.
/// The desktop module supplies fixed product text and must not activate the panel when presenting it.
/// </summary>
public sealed record CommandCompletionNotificationRequest
{
    public CommandCompletionNotificationRequest(Guid sessionId, long sessionGeneration, Guid executionId,
                                                int exitCode)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A notification request requires a valid runtime session identifier.",
                                        nameof(sessionId));
        }

        if (sessionGeneration < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionGeneration), sessionGeneration,
                                                  "A notification request requires a positive session generation.");
        }

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException("A notification request requires a valid command execution identifier.",
                                        nameof(executionId));
        }

        SessionId = sessionId;
        SessionGeneration = sessionGeneration;
        ExecutionId = executionId;
        ExitCode = exitCode;
    }

    public Guid SessionId { get; }

    public long SessionGeneration { get; }

    public Guid ExecutionId { get; }

    public int ExitCode { get; }

    public bool Succeeded => ExitCode == 0;

    public bool IsCurrent(Guid sessionId, long sessionGeneration)
    {
        return SessionId == sessionId && SessionGeneration == sessionGeneration;
    }
}
