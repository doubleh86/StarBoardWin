using System.IO;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Application;

internal sealed class TerminalSavedTabService : IAsyncDisposable
{
    private static readonly TimeSpan _defaultIoTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _defaultShutdownFlushTimeout = TimeSpan.FromSeconds(2);

    private readonly ITerminalSavedTabStore store;
    private readonly TerminalSessionCoordinator sessionCoordinator;
    private readonly IDiagnosticLog diagnosticLog;
    private readonly Func<TerminalShellKind, ShellLaunchSpec> shellResolver;
    private readonly Func<TerminalSavedTabId> savedTabIdFactory;
    private readonly TimeSpan ioTimeout;
    private readonly TimeSpan shutdownFlushTimeout;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly Lock stateLock = new();
    private readonly Lock launchLock = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Dictionary<TerminalSavedTabRequestId, CancellationTokenSource> pendingLaunches = [];

    private TerminalSavedTabsSnapshot snapshot = CreateEmptySnapshot();
    private bool isInitialized;
    private bool isFutureSchemaSuspended;
    private bool isDisposing;
    private bool isDisposed;

    internal TerminalSavedTabService(ITerminalSavedTabStore store,
                                     TerminalSessionCoordinator sessionCoordinator,
                                     IDiagnosticLog diagnosticLog,
                                     Func<TerminalShellKind, ShellLaunchSpec>? shellResolver = null,
                                     Func<TerminalSavedTabId>? savedTabIdFactory = null,
                                     TimeSpan? ioTimeout = null,
                                     TimeSpan? shutdownFlushTimeout = null)
    {
        this.store = store;
        this.sessionCoordinator = sessionCoordinator;
        this.diagnosticLog = diagnosticLog;
        this.shellResolver = shellResolver ?? ShellResolver.Resolve;
        this.savedTabIdFactory = savedTabIdFactory ?? TerminalSavedTabId.CreateNew;
        this.ioTimeout = ValidateTimeout(ioTimeout ?? _defaultIoTimeout, nameof(ioTimeout));
        this.shutdownFlushTimeout = ValidateTimeout(shutdownFlushTimeout ?? _defaultShutdownFlushTimeout,
                                                    nameof(shutdownFlushTimeout));
    }

    internal TerminalSavedTabsSnapshot Snapshot
    {
        get
        {
            lock (stateLock)
            {
                return snapshot;
            }
        }
    }

    internal async Task<TerminalSavedTabsLoadResult> InitializeAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                ThrowIfDisposedOrDisposing();
                if (isInitialized == true)
                {
                    throw new InvalidOperationException("Saved terminal tab persistence has already initialized.");
                }

                isInitialized = true;
            }

            try
            {
                using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, lifetimeCancellation.Token);
                operationCancellation.CancelAfter(ioTimeout);
                var loadTask = store.LoadAsync(operationCancellation.Token);
                var result = await loadTask.WaitAsync(ioTimeout, operationCancellation.Token).ConfigureAwait(false);
                lock (stateLock)
                {
                    snapshot = result.Snapshot ?? CreateEmptySnapshot();
                    isFutureSchemaSuspended = result.Status == TerminalSavedTabsLoadStatus.FutureSchema;
                }

                if (result.Message is not null)
                {
                    diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "LoadSavedTabs", result.Message);
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (Exception exception) when (IsPersistenceException(exception) == true)
            {
                WriteSafePersistenceDiagnostic("LoadSavedTabs", exception);
                var result = new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.Invalid,
                                                             CreateEmptySnapshot(),
                                                             "저장한 탭을 읽지 못해 빈 목록으로 시작합니다.");
                lock (stateLock)
                {
                    snapshot = result.Snapshot!;
                }

                return result;
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async Task<TerminalSavedTabOperationResult> CreateAsync(TerminalSavedTabCreateRequest request,
                                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await MutateAsync(request.RequestId, TerminalSavedTabOperation.Create,
                                 tabs =>
                                 {
                                     if (tabs.Count >= TerminalSavedTabsContract.MaximumSavedTabs)
                                     {
                                         return MutationDecision.Failed(TerminalSavedTabOperationStatus.SavedTabLimitReached,
                                                                        "저장한 탭은 최대 20개까지 만들 수 있습니다.");
                                     }

                                     var validation = ValidateDefinitionForPersistence(request.StartingDirectory,
                                                                                       request.ShellKind);
                                     if (validation is not null)
                                     {
                                         return validation;
                                     }

                                     var savedTabId = savedTabIdFactory();
                                     if (savedTabId.Value == Guid.Empty || tabs.Any(tab => tab.SavedTabId == savedTabId))
                                     {
                                         throw new InvalidOperationException("The saved terminal tab identifier must be unique.");
                                     }

                                     var savedTab = new TerminalSavedTab(savedTabId, request.Name,
                                                                         request.StartingDirectory, request.ShellKind);
                                     return MutationDecision.Succeeded([.. tabs, savedTab]);
                                 }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<TerminalSavedTabOperationResult> UpdateAsync(TerminalSavedTabUpdateRequest request,
                                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await MutateAsync(request.RequestId, TerminalSavedTabOperation.Update,
                                 tabs =>
                                 {
                                     var index = tabs.FindIndex(tab => tab.SavedTabId == request.SavedTab.SavedTabId);
                                     if (index < 0)
                                     {
                                         return MutationDecision.Failed(TerminalSavedTabOperationStatus.SavedTabNotFound,
                                                                        "저장한 탭을 찾을 수 없습니다.");
                                     }

                                     var validation = ValidateDefinitionForPersistence(
                                         request.SavedTab.StartingDirectory, request.SavedTab.ShellKind);
                                     if (validation is not null)
                                     {
                                         return validation;
                                     }

                                     tabs[index] = request.SavedTab;
                                     return MutationDecision.Succeeded(tabs);
                                 }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<TerminalSavedTabOperationResult> DeleteAsync(TerminalSavedTabDeleteRequest request,
                                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await MutateAsync(request.RequestId, TerminalSavedTabOperation.Delete,
                                 tabs =>
                                 {
                                     var removed = tabs.RemoveAll(tab => tab.SavedTabId == request.SavedTabId);
                                     return removed == 0
                                         ? MutationDecision.Failed(TerminalSavedTabOperationStatus.SavedTabNotFound,
                                                                   "저장한 탭을 찾을 수 없습니다.")
                                         : MutationDecision.Succeeded(tabs);
                                 }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<TerminalSavedTabLaunchResult> LaunchAsync(TerminalSavedTabLaunchRequest request,
                                                                  CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CancellationTokenSource launchCancellation;
        lock (launchLock)
        {
            ThrowIfDisposedOrDisposing();
            if (pendingLaunches.ContainsKey(request.RequestId) == true)
            {
                return new TerminalSavedTabLaunchResult(request.RequestId, request.SavedTabId,
                                                        TerminalSavedTabLaunchStatus.DuplicateRequest, null, null);
            }

            launchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                                                                                 lifetimeCancellation.Token);
            pendingLaunches.Add(request.RequestId, launchCancellation);
        }

        try
        {
            TerminalSavedTab? savedTab;
            lock (stateLock)
            {
                ThrowIfNotInitialized();
                savedTab = snapshot.Tabs.FirstOrDefault(tab => tab.SavedTabId == request.SavedTabId);
            }

            if (savedTab is null)
            {
                return CreateLaunchFailure(request, TerminalSavedTabLaunchStatus.SavedTabNotFound,
                                           "저장한 탭을 찾을 수 없습니다.");
            }

            var directoryValidation = TerminalStartingDirectory.ValidateExisting(savedTab.StartingDirectory);
            if (directoryValidation.Succeeded == false || directoryValidation.NormalizedPath is null)
            {
                return CreateLaunchFailure(request, TerminalSavedTabLaunchStatus.StartingDirectoryUnavailable,
                                           "시작 폴더가 없거나 접근할 수 없습니다.");
            }

            ShellLaunchSpec shell;
            try
            {
                shell = shellResolver(savedTab.ShellKind) with
                {
                    WorkingDirectory = directoryValidation.NormalizedPath,
                };
            }
            catch (Exception exception) when (IsShellResolutionException(exception) == true)
            {
                WriteSafeLaunchDiagnostic("ResolveSavedTabShell", exception);
                return CreateLaunchFailure(request, TerminalSavedTabLaunchStatus.ShellUnavailable,
                                           "선택한 셸을 찾을 수 없습니다.");
            }

            var session = await sessionCoordinator.AddSavedTabAsync(savedTab, shell,
                                                                    launchCancellation.Token).ConfigureAwait(false);
            if (session is null)
            {
                return CreateLaunchFailure(request, TerminalSavedTabLaunchStatus.RunningTabLimitReached,
                                           "실행 중인 탭은 최대 8개까지 열 수 있습니다.");
            }

            return new TerminalSavedTabLaunchResult(request.RequestId, request.SavedTabId,
                                                    TerminalSavedTabLaunchStatus.Started, session, null);
        }
        catch (OperationCanceledException)
        {
            return CreateLaunchFailure(request, TerminalSavedTabLaunchStatus.Cancelled, null);
        }
        catch (Exception exception) when (IsLaunchException(exception) == true)
        {
            WriteSafeLaunchDiagnostic("LaunchSavedTab", exception);
            return CreateLaunchFailure(request, TerminalSavedTabLaunchStatus.Failed,
                                       "저장한 탭을 시작하지 못했습니다.");
        }
        finally
        {
            lock (launchLock)
            {
                pendingLaunches.Remove(request.RequestId);
            }

            launchCancellation.Dispose();
        }
    }

    internal bool CancelLaunch(TerminalSavedTabLaunchCancellation cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        lock (launchLock)
        {
            if (pendingLaunches.TryGetValue(cancellation.RequestId, out var pending) == false)
            {
                return false;
            }

            pending.Cancel();
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (stateLock)
        {
            if (isDisposed == true || isDisposing == true)
            {
                return;
            }

            isDisposing = true;
        }

        lifetimeCancellation.Cancel();
        lock (launchLock)
        {
            foreach (var pending in pendingLaunches.Values)
            {
                pending.Cancel();
            }
        }

        var acquired = false;
        try
        {
            acquired = await operationLock.WaitAsync(shutdownFlushTimeout).ConfigureAwait(false);
        }
        finally
        {
            if (acquired == true)
            {
                operationLock.Release();
            }

            lock (stateLock)
            {
                isDisposed = true;
                isDisposing = false;
            }

            if (acquired == true)
            {
                lifetimeCancellation.Dispose();
                operationLock.Dispose();
            }
        }
    }

    private async Task<TerminalSavedTabOperationResult> MutateAsync(TerminalSavedTabRequestId requestId,
                                                                    TerminalSavedTabOperation operation,
                                                                    Func<List<TerminalSavedTab>, MutationDecision> mutate,
                                                                    CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TerminalSavedTabsSnapshot currentSnapshot;
            lock (stateLock)
            {
                ThrowIfNotInitialized();
                if (isFutureSchemaSuspended == true)
                {
                    return CreateOperationFailure(requestId, operation,
                                                  TerminalSavedTabOperationStatus.Failed,
                                                  "더 새로운 저장한 탭 파일을 보존하기 위해 편집이 중단되었습니다.");
                }

                currentSnapshot = snapshot;
            }

            var decision = mutate(currentSnapshot.Tabs.ToList());
            if (decision.Status != TerminalSavedTabOperationStatus.Succeeded || decision.Tabs is null)
            {
                return CreateOperationFailure(requestId, operation, decision.Status, decision.FailureMessage);
            }

            var candidate = new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion,
                                                          decision.Tabs);
            try
            {
                using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, lifetimeCancellation.Token);
                operationCancellation.CancelAfter(ioTimeout);
                await store.SaveAsync(candidate, operationCancellation.Token)
                    .WaitAsync(ioTimeout, operationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (TerminalSavedTabsFutureSchemaException exception)
            {
                lock (stateLock)
                {
                    isFutureSchemaSuspended = true;
                }

                WriteSafePersistenceDiagnostic(operation.ToString(), exception);
                return CreateOperationFailure(requestId, operation, TerminalSavedTabOperationStatus.Failed,
                                              "더 새로운 저장한 탭 파일을 보존하기 위해 편집이 중단되었습니다.");
            }
            catch (Exception exception) when (IsPersistenceException(exception) == true)
            {
                WriteSafePersistenceDiagnostic(operation.ToString(), exception);
                return CreateOperationFailure(requestId, operation, TerminalSavedTabOperationStatus.Failed,
                                              "저장한 탭 변경 사항을 저장하지 못했습니다.");
            }

            lock (stateLock)
            {
                snapshot = candidate;
            }

            return new TerminalSavedTabOperationResult(requestId, operation,
                                                       TerminalSavedTabOperationStatus.Succeeded, candidate, null);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private MutationDecision? ValidateDefinitionForPersistence(string startingDirectory,
                                                               TerminalShellKind shellKind)
    {
        var directoryValidation = TerminalStartingDirectory.ValidateExisting(startingDirectory);
        if (directoryValidation.Succeeded == false)
        {
            return MutationDecision.Failed(TerminalSavedTabOperationStatus.InvalidStartingDirectory,
                                           "시작 폴더가 없거나 접근할 수 없습니다.");
        }

        try
        {
            _ = shellResolver(shellKind);
            return null;
        }
        catch (Exception exception) when (IsShellResolutionException(exception) == true)
        {
            WriteSafeLaunchDiagnostic("ValidateSavedTabShell", exception);
            return MutationDecision.Failed(TerminalSavedTabOperationStatus.UnsupportedShell,
                                           "선택한 셸을 찾을 수 없습니다.");
        }
    }

    private void ThrowIfNotInitialized()
    {
        ThrowIfDisposedOrDisposing();
        if (isInitialized == false)
        {
            throw new InvalidOperationException("Saved terminal tab persistence has not initialized.");
        }
    }

    private void ThrowIfDisposedOrDisposing()
    {
        ObjectDisposedException.ThrowIf(isDisposed || isDisposing, this);
    }

    private void WriteSafePersistenceDiagnostic(string operation, Exception exception)
    {
        diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation,
                            $"Saved terminal tab persistence failed ({exception.GetType().Name}).");
    }

    private void WriteSafeLaunchDiagnostic(string operation, Exception exception)
    {
        diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation,
                            $"A saved terminal tab launch policy failed ({exception.GetType().Name}).");
    }

    private static TerminalSavedTabOperationResult CreateOperationFailure(TerminalSavedTabRequestId requestId,
                                                                          TerminalSavedTabOperation operation,
                                                                          TerminalSavedTabOperationStatus status,
                                                                          string? message)
    {
        return new TerminalSavedTabOperationResult(requestId, operation, status, null, message);
    }

    private static TerminalSavedTabLaunchResult CreateLaunchFailure(TerminalSavedTabLaunchRequest request,
                                                                    TerminalSavedTabLaunchStatus status,
                                                                    string? message)
    {
        return new TerminalSavedTabLaunchResult(request.RequestId, request.SavedTabId, status, null, message);
    }

    private static TerminalSavedTabsSnapshot CreateEmptySnapshot()
    {
        return new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, []);
    }

    private static bool IsPersistenceException(Exception exception)
    {
        return exception is IOException or
               InvalidDataException or
               TimeoutException or
               UnauthorizedAccessException or
               OperationCanceledException;
    }

    private static bool IsShellResolutionException(Exception exception)
    {
        return exception is ArgumentException or FileNotFoundException or NotSupportedException;
    }

    private static bool IsLaunchException(Exception exception)
    {
        return exception is ArgumentException or
               IOException or
               InvalidOperationException or
               NotSupportedException or
               UnauthorizedAccessException;
    }

    private static TimeSpan ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The timeout must be positive and finite.");
        }

        return value;
    }

    private sealed record MutationDecision(TerminalSavedTabOperationStatus Status,
                                           IReadOnlyList<TerminalSavedTab>? Tabs,
                                           string? FailureMessage)
    {
        internal static MutationDecision Succeeded(IReadOnlyList<TerminalSavedTab> tabs)
        {
            return new MutationDecision(TerminalSavedTabOperationStatus.Succeeded, tabs, null);
        }

        internal static MutationDecision Failed(TerminalSavedTabOperationStatus status, string message)
        {
            return new MutationDecision(status, null, message);
        }
    }
}
