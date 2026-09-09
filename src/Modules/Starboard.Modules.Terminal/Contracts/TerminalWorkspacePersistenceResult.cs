namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// The operation the terminal workspace store attempted. The host can surface this
/// independently from the Preferences transaction that controls restore on launch.
/// </summary>
public enum TerminalWorkspacePersistenceOperation
{
    Save,
    Delete,
    ShutdownFlush,
}

public enum TerminalWorkspacePersistenceStatus
{
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>
/// A user-safe persistence outcome. FailureDetail is suitable for a local status
/// message and must not contain terminal input, output, or workspace-file contents.
/// </summary>
public sealed record TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation Operation,
                                                        TerminalWorkspacePersistenceStatus Status,
                                                        string? FailureDetail)
{
    public bool Succeeded => Status == TerminalWorkspacePersistenceStatus.Succeeded;
}

/// <summary>
/// The renderer-visible save state. It is sent as a global message because saving a
/// workspace is not scoped to one live terminal session.
/// </summary>
public enum TerminalWorkspaceSaveState
{
    Saved,
    Failed,
    Deleted,
    Disabled,
}

public sealed record TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState State, string? Message);
