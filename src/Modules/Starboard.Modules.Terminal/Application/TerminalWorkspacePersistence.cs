using System.IO;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Application;

internal sealed class TerminalWorkspacePersistence : IAsyncDisposable
{
    private static readonly TimeSpan _defaultDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _defaultShutdownFlushTimeout = TimeSpan.FromSeconds(2);

    private readonly ITerminalWorkspaceStore store;
    private readonly TerminalSessionCoordinator sessionCoordinator;
    private readonly IDiagnosticLog diagnosticLog;
    private readonly TimeSpan debounce;
    private readonly TimeSpan shutdownFlushTimeout;
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim writerLock = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();

    private CancellationTokenSource? debounceCancellation;
    private Task pendingSave = Task.CompletedTask;
    private TerminalWorkspaceConfiguration? latestConfiguration;
    private bool isInitialized;
    private bool isEnabled;
    private bool hasInspectedStoredSchema;
    private bool isRestoreInProgress;
    private bool isFutureSchemaSuspended;
    private bool isDisposing;
    private bool isDisposed;

    internal TerminalWorkspacePersistence(ITerminalWorkspaceStore store,
                                          TerminalSessionCoordinator sessionCoordinator,
                                          IDiagnosticLog diagnosticLog,
                                          TimeSpan? debounce = null,
                                          TimeSpan? shutdownFlushTimeout = null)
    {
        this.store = store;
        this.sessionCoordinator = sessionCoordinator;
        this.diagnosticLog = diagnosticLog;
        this.debounce = ValidateTimeout(debounce ?? _defaultDebounce, nameof(debounce));
        this.shutdownFlushTimeout = ValidateTimeout(shutdownFlushTimeout ?? _defaultShutdownFlushTimeout,
                                                    nameof(shutdownFlushTimeout));
        sessionCoordinator.WorkspaceChanged += SessionCoordinator_WorkspaceChanged;
    }

    internal event Action<TerminalWorkspaceSaveStatus>? StatusChanged;

    internal async Task<TerminalWorkspaceLoadResult> InitializeAsync(bool restoreEnabled,
                                                                     CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed || isDisposing, this);
            if (isInitialized == true)
            {
                throw new InvalidOperationException("Terminal workspace persistence has already initialized.");
            }

            isInitialized = true;
            isEnabled = restoreEnabled;
        }

        if (restoreEnabled == false)
        {
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Disabled, null));
            return new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.NotFound, null, null);
        }

        TerminalWorkspaceLoadResult result;
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                                                                                           lifetimeCancellation.Token);
            result = await store.LoadAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceException(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "LoadWorkspace",
                                "The terminal workspace configuration could not be loaded.", exception);
            result = new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.Invalid, null,
                                                     "저장된 작업공간을 읽지 못해 기본 탭으로 시작합니다.");
        }

        if (result.Status == TerminalWorkspaceLoadStatus.FutureSchema)
        {
            lock (stateLock)
            {
                hasInspectedStoredSchema = true;
                isFutureSchemaSuspended = true;
            }

            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Disabled, result.Message));
        }
        else if (result.Message is not null)
        {
            lock (stateLock)
            {
                hasInspectedStoredSchema = true;
                latestConfiguration = result.Configuration;
                isRestoreInProgress = result.Configuration is not null;
            }

            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "LoadWorkspace", result.Message);
        }
        else
        {
            lock (stateLock)
            {
                hasInspectedStoredSchema = true;
                latestConfiguration = result.Configuration;
                isRestoreInProgress = result.Configuration is not null;
            }
        }

        return result;
    }

    internal void CompleteRestore()
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed || isDisposing, this);
            if (isInitialized == false)
            {
                throw new InvalidOperationException("Terminal workspace persistence has not initialized.");
            }

            isRestoreInProgress = false;
        }
    }

    internal async Task<TerminalWorkspacePersistenceResult> SetEnabledAsync(bool enabled,
                                                                            CancellationToken cancellationToken)
    {
        if (enabled == true && await InspectStoredSchemaIfNeededAsync(cancellationToken).ConfigureAwait(false) == false)
        {
            return new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.Save,
                                                          TerminalWorkspacePersistenceStatus.Skipped,
                                                          "더 새로운 작업공간 파일을 보존하기 위해 자동 저장이 중단되었습니다.");
        }

        TerminalWorkspaceConfiguration? configuration = null;
        Task pending;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed || isDisposing, this);
            if (isInitialized == false)
            {
                throw new InvalidOperationException("Terminal workspace persistence has not initialized.");
            }

            if (enabled == true && isFutureSchemaSuspended == true)
            {
                return new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.Save,
                                                              TerminalWorkspacePersistenceStatus.Skipped,
                                                              "더 새로운 작업공간 파일을 보존하기 위해 자동 저장이 중단되었습니다.");
            }

            if (enabled == true && TryCreateConfiguration(sessionCoordinator.Snapshot, out configuration) == false)
            {
                return new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.Save,
                                                              TerminalWorkspacePersistenceStatus.Failed,
                                                              "지원되는 셸 종류만 작업공간에 저장할 수 있습니다.");
            }

            isEnabled = enabled;
            debounceCancellation?.Cancel();
            debounceCancellation?.Dispose();
            debounceCancellation = null;
            pending = pendingSave;
            if (enabled == true)
            {
                latestConfiguration = configuration;
            }
            else
            {
                latestConfiguration = null;
            }
        }

        await ObservePendingSaveAsync(pending).ConfigureAwait(false);
        if (enabled == false)
        {
            return await DeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        return await SaveAsync(configuration!, TerminalWorkspacePersistenceOperation.Save,
                               cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> InspectStoredSchemaIfNeededAsync(CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed || isDisposing, this);
            if (isInitialized == false)
            {
                throw new InvalidOperationException("Terminal workspace persistence has not initialized.");
            }

            if (hasInspectedStoredSchema == true)
            {
                return isFutureSchemaSuspended == false;
            }
        }

        TerminalWorkspaceLoadResult result;
        try
        {
            await writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                result = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writerLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceException(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "InspectWorkspaceSchema",
                                "The terminal workspace schema could not be inspected before enabling persistence.",
                                exception);
            result = new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.Invalid, null, null);
        }

        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed || isDisposing, this);
            hasInspectedStoredSchema = true;
            isFutureSchemaSuspended = result.Status == TerminalWorkspaceLoadStatus.FutureSchema;
        }

        if (result.Status == TerminalWorkspaceLoadStatus.FutureSchema)
        {
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Disabled, result.Message));
            return false;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        TerminalWorkspaceConfiguration? configuration;
        Task pending;
        lock (stateLock)
        {
            if (isDisposed == true || isDisposing == true)
            {
                return;
            }

            isDisposing = true;
            sessionCoordinator.WorkspaceChanged -= SessionCoordinator_WorkspaceChanged;
            debounceCancellation?.Cancel();
            debounceCancellation?.Dispose();
            debounceCancellation = null;
            pending = pendingSave;
            configuration = isEnabled == true && isFutureSchemaSuspended == false
                ? latestConfiguration
                : null;
        }

        if (configuration is null && isEnabled == true && isFutureSchemaSuspended == false)
        {
            _ = TryCreateConfiguration(sessionCoordinator.Snapshot, out configuration);
        }

        var shutdownTask = FlushForShutdownAsync(pending, configuration);
        var completedWithinDeadline = false;
        try
        {
            await shutdownTask.WaitAsync(shutdownFlushTimeout).ConfigureAwait(false);
            completedWithinDeadline = true;
        }
        catch (TimeoutException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "FlushWorkspace",
                                "Terminal workspace persistence exceeded its shutdown deadline.", exception);
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Failed,
                                                          "종료 전에 최신 작업공간 저장을 완료하지 못했습니다."));
            ObserveLateTask(shutdownTask);
        }
        finally
        {
            lifetimeCancellation.Cancel();
            lock (stateLock)
            {
                isDisposed = true;
                isDisposing = false;
            }

            if (completedWithinDeadline == true || shutdownTask.IsCompleted == true)
            {
                DisposeResources();
            }
            else
            {
                _ = shutdownTask.ContinueWith(_ => DisposeResources(), CancellationToken.None,
                                              TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private void SessionCoordinator_WorkspaceChanged(TerminalWorkspaceSnapshot snapshot)
    {
        lock (stateLock)
        {
            if (isInitialized == false || isEnabled == false || isRestoreInProgress == true ||
                isFutureSchemaSuspended == true || isDisposing == true || isDisposed == true)
            {
                return;
            }
        }

        if (TryCreateConfiguration(snapshot, out var configuration) == false)
        {
            var publishFailure = false;
            lock (stateLock)
            {
                if (isInitialized == true && isEnabled == true && isRestoreInProgress == false &&
                    isFutureSchemaSuspended == false && isDisposing == false && isDisposed == false)
                {
                    latestConfiguration = null;
                    publishFailure = true;
                }
            }

            if (publishFailure == true)
            {
                PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Failed,
                                                              "지원되는 셸 종류만 작업공간에 저장할 수 있습니다."));
            }

            return;
        }

        CancellationTokenSource saveCancellation;
        lock (stateLock)
        {
            if (isInitialized == false || isEnabled == false || isRestoreInProgress == true ||
                isFutureSchemaSuspended == true || isDisposing == true || isDisposed == true)
            {
                return;
            }

            if (ConfigurationsEqual(latestConfiguration, configuration) == true)
            {
                return;
            }

            latestConfiguration = configuration;
            debounceCancellation?.Cancel();
            debounceCancellation?.Dispose();
            saveCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            debounceCancellation = saveCancellation;
            pendingSave = SaveAfterDebounceAsync(configuration!, saveCancellation);
        }
    }

    private async Task SaveAfterDebounceAsync(TerminalWorkspaceConfiguration configuration,
                                              CancellationTokenSource saveCancellation)
    {
        try
        {
            await Task.Delay(debounce, saveCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (saveCancellation.IsCancellationRequested == true)
        {
            return;
        }

        _ = await SaveAsync(configuration, TerminalWorkspacePersistenceOperation.Save,
                            lifetimeCancellation.Token).ConfigureAwait(false);
    }

    private async Task<TerminalWorkspacePersistenceResult> SaveAsync(TerminalWorkspaceConfiguration configuration,
                                                                     TerminalWorkspacePersistenceOperation operation,
                                                                     CancellationToken cancellationToken)
    {
        try
        {
            await writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await store.SaveAsync(configuration, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writerLock.Release();
            }

            var result = new TerminalWorkspacePersistenceResult(operation,
                                                                TerminalWorkspacePersistenceStatus.Succeeded, null);
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Saved, null));
            return result;
        }
        catch (TerminalWorkspaceFutureSchemaException exception)
        {
            lock (stateLock)
            {
                isFutureSchemaSuspended = true;
            }

            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation.ToString(),
                                "A newer terminal workspace schema stopped automatic persistence.", exception);
            var result = new TerminalWorkspacePersistenceResult(operation,
                                                                TerminalWorkspacePersistenceStatus.Skipped,
                                                                "더 새로운 작업공간 파일을 보존하기 위해 자동 저장이 중단되었습니다.");
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Disabled,
                                                          result.FailureDetail));
            return result;
        }
        catch (Exception exception) when (IsPersistenceException(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation.ToString(),
                                "The terminal workspace configuration could not be persisted.", exception);
            var result = new TerminalWorkspacePersistenceResult(operation,
                                                                TerminalWorkspacePersistenceStatus.Failed,
                                                                "구성을 저장하지 못했습니다.");
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Failed,
                                                          result.FailureDetail));
            return result;
        }
    }

    private async Task<TerminalWorkspacePersistenceResult> DeleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await store.DeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writerLock.Release();
            }

            var result = new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.Delete,
                                                                TerminalWorkspacePersistenceStatus.Succeeded, null);
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Deleted, null));
            return result;
        }
        catch (Exception exception) when (IsPersistenceException(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "DeleteWorkspace",
                                "The terminal workspace configuration could not be deleted.", exception);
            var result = new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.Delete,
                                                                TerminalWorkspacePersistenceStatus.Failed,
                                                                "저장된 작업공간을 삭제하지 못했습니다.");
            PublishStatus(new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Failed,
                                                          result.FailureDetail));
            return result;
        }
    }

    private async Task FlushForShutdownAsync(Task pending, TerminalWorkspaceConfiguration? configuration)
    {
        await ObservePendingSaveAsync(pending).ConfigureAwait(false);
        if (configuration is null)
        {
            return;
        }

        _ = await SaveAsync(configuration, TerminalWorkspacePersistenceOperation.ShutdownFlush,
                            lifetimeCancellation.Token).ConfigureAwait(false);
    }

    private static async Task ObservePendingSaveAsync(Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PublishStatus(TerminalWorkspaceSaveStatus status)
    {
        lock (stateLock)
        {
            if (isDisposed == true)
            {
                return;
            }
        }

        StatusChanged?.Invoke(status);
    }

    private void ObserveLateTask(Task task)
    {
        _ = task.ContinueWith(completedTask => diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal",
                                                                   "FlushWorkspace",
                                                                   "A workspace flush failed after shutdown.",
                                                                   completedTask.Exception),
                                  CancellationToken.None,
                                  TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                                  TaskScheduler.Default);
    }

    private static TerminalWorkspaceConfiguration CreateConfiguration(TerminalWorkspaceSnapshot snapshot)
    {
        if (snapshot.Tabs.Any(tab => tab.ShellKind is null) == true)
        {
            throw new NotSupportedException("The terminal workspace contains a custom shell executable.");
        }

        var tabs = snapshot.Tabs
            .Select((tab, order) => new TerminalWorkspaceTabConfiguration(tab.ConfigurationId, tab.Name, order,
                                                                          tab.StartingDirectory, tab.ShellKind!.Value))
            .ToArray();
        var activeConfigurationId = snapshot.ActiveSessionId is { } activeSessionId
            ? snapshot.Tabs.FirstOrDefault(tab => tab.SessionId == activeSessionId)?.ConfigurationId
            : null;
        var configuration = new TerminalWorkspaceConfiguration(TerminalWorkspaceConfigurationValidator.CurrentSchemaVersion,
                                                               tabs, activeConfigurationId);
        TerminalWorkspaceConfigurationValidator.Validate(configuration);
        return configuration;
    }

    private static bool TryCreateConfiguration(TerminalWorkspaceSnapshot snapshot,
                                               out TerminalWorkspaceConfiguration? configuration)
    {
        try
        {
            configuration = CreateConfiguration(snapshot);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            configuration = null;
            return false;
        }
    }

    private static bool ConfigurationsEqual(TerminalWorkspaceConfiguration? left,
                                            TerminalWorkspaceConfiguration? right)
    {
        if (ReferenceEquals(left, right) == true)
        {
            return true;
        }

        if (left is null || right is null || left.SchemaVersion != right.SchemaVersion ||
            left.ActiveTabConfigurationId != right.ActiveTabConfigurationId ||
            left.Tabs.Count != right.Tabs.Count)
        {
            return false;
        }

        return left.Tabs.SequenceEqual(right.Tabs);
    }

    private void DisposeResources()
    {
        writerLock.Dispose();
        lifetimeCancellation.Dispose();
    }

    private static bool IsPersistenceException(Exception exception)
    {
        return exception is ArgumentException or
               IOException or
               InvalidDataException or
               InvalidOperationException or
               NotSupportedException or
               OperationCanceledException or
               UnauthorizedAccessException;
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return timeout;
    }
}
