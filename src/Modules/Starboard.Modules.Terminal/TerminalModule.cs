using System.Windows;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.Modules.Terminal.Presentation;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal;

public sealed class TerminalModule : IDisposable
{
    private readonly TerminalSessionCoordinator sessionCoordinator;
    private readonly TerminalSavedTabService savedTabService;
    private readonly TerminalWorkspacePersistence workspacePersistence;
    private readonly TerminalView terminalView;
    private readonly Lock shutdownLock = new();
    private Task<TerminalWorkspacePersistenceResult>? shutdownTask;
    private volatile TerminalWorkspaceSaveStatus? shutdownWorkspaceSaveStatus;
    private volatile bool isCapturingShutdownStatus;
    private bool isDisposed;

    public TerminalModule(IDiagnosticLog diagnosticLog)
    {
        sessionCoordinator = new TerminalSessionCoordinator(new ConPtySessionFactory(diagnosticLog), diagnosticLog);
        var workspaceStore = new FileTerminalWorkspaceStore(FileTerminalWorkspaceStore.GetDefaultPath());
        workspacePersistence = new TerminalWorkspacePersistence(workspaceStore, sessionCoordinator, diagnosticLog);
        var savedTabStore = new FileTerminalSavedTabStore(FileTerminalSavedTabStore.GetDefaultPath());
        savedTabService = new TerminalSavedTabService(savedTabStore, sessionCoordinator, diagnosticLog);
        workspacePersistence.StatusChanged += HandleWorkspacePersistenceStatusChanged;
        terminalView = new TerminalView(diagnosticLog, sessionCoordinator, workspacePersistence, savedTabService);
    }

    public FrameworkElement Surface => terminalView;

    public Task StartAsync(TerminalOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return terminalView.StartAsync(options, cancellationToken);
    }

    public TerminalSettingsApplyResult ApplySettings(TerminalSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (terminalView.Dispatcher.CheckAccess() == true)
        {
            return terminalView.ApplySettings(settings);
        }

        return terminalView.Dispatcher.Invoke(() => terminalView.ApplySettings(settings));
    }

    public Task<TerminalWorkspacePersistenceResult> SetWorkspacePersistenceEnabledAsync(
        bool enabled, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return terminalView.SetWorkspacePersistenceEnabledAsync(enabled, cancellationToken);
    }

    public void NotifyPanelVisibilityChanged(bool isVisible)
    {
        if (isDisposed == true)
        {
            return;
        }

        if (terminalView.Dispatcher.CheckAccess() == true)
        {
            terminalView.NotifyPanelVisibilityChanged(isVisible);
            return;
        }

        terminalView.Dispatcher.Invoke(() => terminalView.NotifyPanelVisibilityChanged(isVisible));
    }

    public Task<TerminalWorkspacePersistenceResult> ShutdownAsync()
    {
        lock (shutdownLock)
        {
            shutdownTask ??= ShutdownCoreAsync();

            return shutdownTask;
        }
    }

    public void Dispose()
    {
        _ = ShutdownAsync().GetAwaiter().GetResult();
    }

    private async Task<TerminalWorkspacePersistenceResult> ShutdownCoreAsync()
    {
        if (isDisposed == true)
        {
            return CreateShutdownResult(shutdownWorkspaceSaveStatus);
        }

        isDisposed = true;
        await terminalView.DisposeAsync();
        await savedTabService.DisposeAsync();
        isCapturingShutdownStatus = true;
        try
        {
            await workspacePersistence.DisposeAsync();
        }
        finally
        {
            isCapturingShutdownStatus = false;
            workspacePersistence.StatusChanged -= HandleWorkspacePersistenceStatusChanged;
        }

        await sessionCoordinator.DisposeAsync();

        return CreateShutdownResult(shutdownWorkspaceSaveStatus);
    }

    private void HandleWorkspacePersistenceStatusChanged(TerminalWorkspaceSaveStatus status)
    {
        if (isCapturingShutdownStatus == true)
        {
            shutdownWorkspaceSaveStatus = status;
        }
    }

    internal static TerminalWorkspacePersistenceResult CreateShutdownResult(TerminalWorkspaceSaveStatus? status)
    {
        if (status?.State == TerminalWorkspaceSaveState.Saved)
        {
            return new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.ShutdownFlush,
                                                          TerminalWorkspacePersistenceStatus.Succeeded, null);
        }

        if (status?.State == TerminalWorkspaceSaveState.Failed)
        {
            return new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.ShutdownFlush,
                                                          TerminalWorkspacePersistenceStatus.Failed, status.Message);
        }

        return new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.ShutdownFlush,
                                                      TerminalWorkspacePersistenceStatus.Skipped, status?.Message);
    }
}
