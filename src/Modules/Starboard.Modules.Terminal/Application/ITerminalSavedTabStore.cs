using System.IO;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal enum TerminalSavedTabsLoadStatus
{
    NotFound,
    Loaded,
    Invalid,
    FutureSchema,
}

internal sealed record TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus Status,
                                                   TerminalSavedTabsSnapshot? Snapshot,
                                                   string? Message);

internal sealed class TerminalSavedTabsFutureSchemaException : IOException
{
    internal TerminalSavedTabsFutureSchemaException()
        : base("A newer saved terminal tabs schema cannot be overwritten.")
    {
    }
}

internal interface ITerminalSavedTabStore
{
    Task<TerminalSavedTabsLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(TerminalSavedTabsSnapshot snapshot, CancellationToken cancellationToken);
}
