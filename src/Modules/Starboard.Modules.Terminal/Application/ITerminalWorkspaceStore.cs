using System.IO;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal enum TerminalWorkspaceLoadStatus
{
    NotFound,
    Loaded,
    RecoveredFromBackup,
    Invalid,
    FutureSchema,
}

internal sealed record TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus Status,
                                                   TerminalWorkspaceConfiguration? Configuration,
                                                   string? Message);

internal sealed class TerminalWorkspaceFutureSchemaException : IOException
{
    internal TerminalWorkspaceFutureSchemaException()
        : base("A newer terminal workspace schema cannot be overwritten.")
    {
    }
}

internal interface ITerminalWorkspaceStore
{
    Task<TerminalWorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(TerminalWorkspaceConfiguration configuration, CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}
