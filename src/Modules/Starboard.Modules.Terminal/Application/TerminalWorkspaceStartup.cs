using System.Runtime.CompilerServices;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

[assembly: InternalsVisibleTo("Starboard.IntegrationTests")]

namespace Starboard.Modules.Terminal.Application;

internal sealed class TerminalWorkspaceStartup(TerminalWorkspacePersistence persistence,
                                               TerminalSessionCoordinator sessions,
                                               IDiagnosticLog diagnosticLog)
{
    internal async Task StartAsync(bool restoreEnabled, ShellLaunchSpec shell, TerminalShellKind? shellKind,
                                   Func<TerminalShellKind, ShellLaunchSpec> resolveShell,
                                   Func<CancellationToken, Task> prepareRenderer, int columns, int rows,
                                   CancellationToken cancellationToken)
    {
        diagnosticLog.Write(DiagnosticLevel.Information, "Terminal", "Startup.RendererWaiting", "");
        await prepareRenderer(cancellationToken);
        diagnosticLog.Write(DiagnosticLevel.Information, "Terminal", "Startup.RendererReady", "");
        var load = await persistence.InitializeAsync(restoreEnabled, cancellationToken);
        var configuration = load.Status is TerminalWorkspaceLoadStatus.Loaded or TerminalWorkspaceLoadStatus.RecoveredFromBackup
            ? load.Configuration
            : null;
        diagnosticLog.Write(DiagnosticLevel.Information, "Terminal", $"Startup.Load.{load.Status}.N{configuration?.Tabs.Count ?? 0}", "");
        await sessions.StartAsync(shell, shellKind, configuration, resolveShell, columns, rows, cancellationToken);
        persistence.CompleteRestore();
        var snapshot = sessions.Snapshot;
        var failures = snapshot.Tabs.Count(tab => tab.State == TerminalSessionState.Failed);
        diagnosticLog.Write(DiagnosticLevel.Information, "Terminal", $"Startup.Restored.N{snapshot.Tabs.Count}.F{failures}", "");
    }
}
