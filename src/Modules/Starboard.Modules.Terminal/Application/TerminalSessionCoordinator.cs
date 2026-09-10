using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Application;

internal sealed class TerminalSessionCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DefaultSessionCloseTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(8);

    private readonly ITerminalSessionFactory sessionFactory;
    private readonly IDiagnosticLog diagnosticLog;
    private readonly TerminalTabRegistry tabRegistry;
    private readonly TimeSpan sessionCloseTimeout;
    private readonly TimeSpan shutdownTimeout;
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly Dictionary<TerminalSessionId, SessionEntry> sessions = [];
    private readonly Dictionary<TerminalSessionId, ShellLaunchSpec> tabShells = [];
    private readonly Dictionary<TerminalSessionId, long> sessionGenerations = [];
    private readonly Dictionary<TerminalSessionId, bool> newOutputStates = [];
    private readonly Func<TerminalConfirmationRequestId> confirmationRequestIdFactory;

    private ShellLaunchSpec? defaultShell;
    private TerminalShellKind? defaultShellKind = TerminalShellKind.Automatic;
    private PendingConfirmation? pendingConfirmation;
    private bool isStarted;
    private bool isDisposed;
    private int columns = 80;
    private int rows = 24;

    internal TerminalSessionCoordinator(ITerminalSessionFactory sessionFactory, IDiagnosticLog diagnosticLog,
                                        Func<TerminalSessionId>? sessionIdFactory = null,
                                        int maximumTabs = TerminalTabRegistry.DefaultMaximumTabs,
                                        TimeSpan? sessionCloseTimeout = null, TimeSpan? shutdownTimeout = null,
                                        Func<TerminalConfirmationRequestId>? confirmationRequestIdFactory = null)
    {
        this.sessionFactory = sessionFactory;
        this.diagnosticLog = diagnosticLog;
        tabRegistry = new TerminalTabRegistry(sessionIdFactory, maximumTabs);
        this.sessionCloseTimeout = ValidateTimeout(sessionCloseTimeout ?? DefaultSessionCloseTimeout,
                                                   nameof(sessionCloseTimeout));
        this.shutdownTimeout = ValidateTimeout(shutdownTimeout ?? DefaultShutdownTimeout, nameof(shutdownTimeout));
        this.confirmationRequestIdFactory = confirmationRequestIdFactory
            ?? TerminalConfirmationRequestId.CreateNew;
    }

    internal event Action<TerminalWorkspaceSnapshot>? WorkspaceChanged;

    internal event Action<TerminalSessionOutput>? OutputReceived;

    internal event Action<TerminalSessionExit>? SessionExited;

    internal event Action<TerminalNewOutputStateSnapshot>? NewOutputStateChanged;

    internal TerminalWorkspaceSnapshot Snapshot
    {
        get
        {
            lock (stateLock)
            {
                return tabRegistry.CreateSnapshot();
            }
        }
    }

    internal TerminalNewOutputStateSnapshot NewOutputState
    {
        get
        {
            lock (stateLock)
            {
                return CreateNewOutputSnapshotLocked();
            }
        }
    }

    internal async Task<TerminalTab> StartAsync(ShellLaunchSpec shell, int columns, int rows,
                                                CancellationToken cancellationToken)
    {
        return await StartAsync(shell, TerminalShellKind.Automatic, null, ShellResolver.Resolve,
                                columns, rows, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<TerminalTab> StartAsync(ShellLaunchSpec shell, TerminalShellKind? shellKind,
                                                TerminalWorkspaceConfiguration? workspaceConfiguration,
                                                Func<TerminalShellKind, ShellLaunchSpec> shellResolver,
                                                int columns, int rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(shellResolver);
        if (workspaceConfiguration is not null)
        {
            TerminalWorkspaceConfigurationValidator.Validate(workspaceConfiguration);
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (isStarted == true)
            {
                throw new InvalidOperationException("The terminal workspace has already started.");
            }

            ValidateSize(columns, rows);
            defaultShell = shell;
            defaultShellKind = shellKind;
            this.columns = columns;
            this.rows = rows;
            isStarted = true;

            if (workspaceConfiguration is null)
            {
                TerminalTab defaultTab;
                TerminalWorkspaceSnapshot defaultSnapshot;
                lock (stateLock)
                {
                    defaultTab = tabRegistry.Add(shell.WorkingDirectory, shellKind);
                    tabShells.Add(defaultTab.SessionId, shell);
                    InitializeSessionLifetimeLocked(defaultTab.SessionId);
                    defaultSnapshot = tabRegistry.CreateSnapshot();
                }

                WorkspaceChanged?.Invoke(defaultSnapshot);
                await StartSessionAsync(defaultTab.SessionId).ConfigureAwait(false);

                lock (stateLock)
                {
                    return tabRegistry.GetRequired(defaultTab.SessionId);
                }
            }

            foreach (var configuration in workspaceConfiguration.Tabs.OrderBy(tab => tab.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                var tab = AddRestoredTab(configuration, shell, shellResolver, out var resolutionFailure);
                if (resolutionFailure is not null)
                {
                    MarkRestoredTabFailed(tab.SessionId, resolutionFailure);
                    continue;
                }

                try
                {
                    await StartSessionAsync(tab.SessionId).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
                {
                    throw;
                }
                catch (Exception exception) when (IsRecoverableRestoreException(exception) == true)
                {
                    // StartSessionAsync already isolates the failure to this tab and records a safe diagnostic.
                }
            }

            TerminalWorkspaceSnapshot restoredSnapshot;
            lock (stateLock)
            {
                if (workspaceConfiguration.ActiveTabConfigurationId is { } activeConfigurationId)
                {
                    _ = tabRegistry.SelectConfiguration(activeConfigurationId);
                }

                if (tabRegistry.ActiveSessionId is { } restoredActiveSessionId)
                {
                    newOutputStates[restoredActiveSessionId] = false;
                }

                restoredSnapshot = tabRegistry.CreateSnapshot();
            }

            WorkspaceChanged?.Invoke(restoredSnapshot);
            var restoredSessionId = restoredSnapshot.ActiveSessionId
                ?? throw new InvalidOperationException("The restored terminal workspace has no active tab.");
            return restoredSnapshot.Tabs.Single(tab => tab.SessionId == restoredSessionId);
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async Task<TerminalTab> AddAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();

            TerminalTab tab;
            TerminalWorkspaceSnapshot snapshot;
            lock (stateLock)
            {
                var shell = defaultShell
                    ?? throw new InvalidOperationException("The terminal shell has not been configured.");
                tab = tabRegistry.Add(shell.WorkingDirectory, defaultShellKind);
                tabShells.Add(tab.SessionId, shell);
                InitializeSessionLifetimeLocked(tab.SessionId);
                CancelPendingPasteLocked();
                snapshot = tabRegistry.CreateSnapshot();
            }

            WorkspaceChanged?.Invoke(snapshot);
            await StartSessionAsync(tab.SessionId).ConfigureAwait(false);

            lock (stateLock)
            {
                return tabRegistry.GetRequired(tab.SessionId);
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async Task<TerminalSessionReference?> AddSavedTabAsync(TerminalSavedTab savedTab,
                                                                    ShellLaunchSpec shell,
                                                                    CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(savedTab);
        ArgumentNullException.ThrowIfNull(shell);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();

            TerminalTab tab;
            TerminalWorkspaceSnapshot snapshot;
            lock (stateLock)
            {
                if (tabRegistry.IsAtCapacity == true)
                {
                    return null;
                }

                tab = tabRegistry.Add(savedTab.StartingDirectory, savedTab.ShellKind, savedTab.Name);
                tabShells.Add(tab.SessionId, shell with { WorkingDirectory = savedTab.StartingDirectory });
                InitializeSessionLifetimeLocked(tab.SessionId);
                CancelPendingPasteLocked();
                snapshot = tabRegistry.CreateSnapshot();
            }

            WorkspaceChanged?.Invoke(snapshot);
            await StartSessionAsync(tab.SessionId).ConfigureAwait(false);

            lock (stateLock)
            {
                var generation = sessionGenerations[tab.SessionId];
                return new TerminalSessionReference(tab.SessionId.Value, generation);
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<bool> CloseCoreAsync(TerminalSessionId sessionId)
    {
        SessionEntry? closedSession;
        TerminalTabCloseResult? closeResult;
        TerminalWorkspaceSnapshot snapshot;
        TerminalNewOutputStateSnapshot outputSnapshot;
        lock (stateLock)
        {
            closeResult = tabRegistry.Close(sessionId);
            if (closeResult is null)
            {
                return false;
            }

            sessions.Remove(sessionId, out closedSession);
            tabShells.Remove(sessionId);
            sessionGenerations.Remove(sessionId);
            newOutputStates.Remove(sessionId);
            CancelPendingConfirmationForSessionLocked(sessionId);
            if (closeResult.ReplacementTab is not null)
            {
                var replacementShell = defaultShell
                    ?? throw new InvalidOperationException("The terminal shell has not been configured.");
                tabShells.Add(closeResult.ReplacementTab.SessionId, replacementShell);
                tabRegistry.SetStartingDirectory(closeResult.ReplacementTab.SessionId,
                                                 replacementShell.WorkingDirectory);
                tabRegistry.SetShellKind(closeResult.ReplacementTab.SessionId, defaultShellKind);
                InitializeSessionLifetimeLocked(closeResult.ReplacementTab.SessionId);
            }

            if (tabRegistry.ActiveSessionId is { } activeSessionId)
            {
                newOutputStates[activeSessionId] = false;
            }

            snapshot = tabRegistry.CreateSnapshot();
            outputSnapshot = CreateNewOutputSnapshotLocked();
        }

        Detach(closedSession);
        WorkspaceChanged?.Invoke(snapshot);
        NewOutputStateChanged?.Invoke(outputSnapshot);

        ExceptionDispatchInfo? replacementFailure = null;
        if (closeResult.ReplacementTab is not null)
        {
            try
            {
                await StartSessionAsync(closeResult.ReplacementTab.SessionId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                replacementFailure = ExceptionDispatchInfo.Capture(exception);
            }
        }

        await DisposeSessionAsync(closedSession, sessionCloseTimeout, "CloseSession").ConfigureAwait(false);

        replacementFailure?.Throw();

        return true;
    }

    internal bool Select(TerminalSessionId sessionId)
    {
        return SelectCore(() => tabRegistry.Select(sessionId));
    }

    internal bool SelectNext()
    {
        return SelectCore(tabRegistry.SelectNext);
    }

    internal bool SelectPrevious()
    {
        return SelectCore(tabRegistry.SelectPrevious);
    }

    internal bool Rename(TerminalSessionId sessionId, string? name)
    {
        return UpdateWorkspace(() => tabRegistry.Rename(sessionId, name));
    }

    internal bool MoveLeft(TerminalSessionId sessionId)
    {
        return UpdateWorkspace(() => tabRegistry.MoveLeft(sessionId));
    }

    internal bool MoveRight(TerminalSessionId sessionId)
    {
        return UpdateWorkspace(() => tabRegistry.MoveRight(sessionId));
    }

    internal TerminalStartingDirectoryUpdateResult UpdateStartingDirectory(TerminalSessionId sessionId,
                                                                           string? startingDirectory)
    {
        var validation = TerminalStartingDirectory.ValidateExisting(startingDirectory);
        if (validation.Succeeded == false || validation.NormalizedPath is null)
        {
            return validation;
        }

        TerminalWorkspaceSnapshot snapshot;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (tabRegistry.Contains(sessionId) == false || tabShells.TryGetValue(sessionId, out var shell) == false)
            {
                return new TerminalStartingDirectoryUpdateResult(false, null, "터미널 탭을 찾을 수 없습니다.");
            }

            tabShells[sessionId] = shell with { WorkingDirectory = validation.NormalizedPath };
            tabRegistry.SetStartingDirectory(sessionId, validation.NormalizedPath);
            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        return validation;
    }

    internal void UpdateDefaultShell(ShellLaunchSpec shell)
    {
        UpdateDefaultShell(shell, TerminalShellKind.Automatic);
    }

    internal void UpdateDefaultShell(ShellLaunchSpec shell, TerminalShellKind? shellKind)
    {
        ArgumentNullException.ThrowIfNull(shell);
        lock (stateLock)
        {
            ThrowIfUnavailable();
            defaultShell = shell;
            defaultShellKind = shellKind;
        }
    }

    internal TerminalCloseConfirmationRequest? RequestCloseConfirmation(TerminalSessionId sessionId)
    {
        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (tabRegistry.Contains(sessionId) == false)
            {
                return null;
            }

            var tab = tabRegistry.GetRequired(sessionId);
            if (tab.State == TerminalSessionState.Exited)
            {
                return null;
            }

            if (pendingConfirmation is not null)
            {
                return null;
            }

            var token = CreateConfirmationTokenLocked(sessionId);
            pendingConfirmation = new PendingConfirmation(token, ConfirmationKind.Close, null);

            return new TerminalCloseConfirmationRequest(token, tab.Name);
        }
    }

    internal TerminalPasteConfirmationRequest? RequestPasteConfirmation(TerminalSessionId sessionId,
                                                                        string clipboardText)
    {
        ArgumentNullException.ThrowIfNull(clipboardText);
        if (clipboardText.Contains('\r') == false && clipboardText.Contains('\n') == false)
        {
            return null;
        }

        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (tabRegistry.ActiveSessionId != sessionId || sessions.ContainsKey(sessionId) == false)
            {
                return null;
            }

            if (pendingConfirmation is not null)
            {
                return null;
            }

            var tab = tabRegistry.GetRequired(sessionId);
            var token = CreateConfirmationTokenLocked(sessionId);
            pendingConfirmation = new PendingConfirmation(token, ConfirmationKind.Paste, clipboardText);

            return new TerminalPasteConfirmationRequest(token, tab.Name, clipboardText);
        }
    }

    internal async Task<bool> ApplyCloseConfirmationAsync(TerminalConfirmationResponse response,
                                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                ThrowIfUnavailable();
                if (TryConsumeConfirmationLocked(response, ConfirmationKind.Close, out _) == false)
                {
                    return false;
                }
            }

            if (response.Result == TerminalConfirmationResult.Cancelled)
            {
                return false;
            }

            var sessionId = new TerminalSessionId(response.Token.Session.SessionId);
            return await CloseCoreAsync(sessionId).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async Task<bool> ApplyPasteConfirmationAsync(TerminalConfirmationResponse response,
                                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? clipboardText;
            lock (stateLock)
            {
                ThrowIfUnavailable();
                if (TryConsumeConfirmationLocked(response, ConfirmationKind.Paste, out clipboardText) == false)
                {
                    return false;
                }
            }

            if (response.Result == TerminalConfirmationResult.Cancelled)
            {
                return false;
            }

            var sessionId = new TerminalSessionId(response.Token.Session.SessionId);
            await WriteAsync(sessionId, clipboardText!, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal void CancelPendingConfirmation()
    {
        lock (stateLock)
        {
            pendingConfirmation = null;
        }
    }

    internal async Task<bool> CloseAsync(TerminalSessionId sessionId, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            lock (stateLock)
            {
                if (tabRegistry.Contains(sessionId) == false)
                {
                    return false;
                }

                if (tabRegistry.GetRequired(sessionId).State != TerminalSessionState.Exited)
                {
                    return false;
                }
            }

            return await CloseCoreAsync(sessionId).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async Task RestartAsync(TerminalSessionId sessionId, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();

            SessionEntry? previousSession;
            TerminalWorkspaceSnapshot restartingSnapshot;
            lock (stateLock)
            {
                if (tabRegistry.Contains(sessionId) == false)
                {
                    throw new InvalidOperationException("The terminal tab does not exist.");
                }

                tabRegistry.SetState(sessionId, TerminalSessionState.Restarting);
                sessions.Remove(sessionId, out previousSession);
                AdvanceSessionLifetimeLocked(sessionId);
                restartingSnapshot = tabRegistry.CreateSnapshot();
            }

            Detach(previousSession);
            WorkspaceChanged?.Invoke(restartingSnapshot);
            NewOutputStateChanged?.Invoke(NewOutputState);

            await DisposeSessionAsync(previousSession, sessionCloseTimeout, "RestartSession").ConfigureAwait(false);

            TerminalWorkspaceSnapshot startingSnapshot;
            lock (stateLock)
            {
                tabRegistry.SetState(sessionId, TerminalSessionState.Starting);
                startingSnapshot = tabRegistry.CreateSnapshot();
            }

            WorkspaceChanged?.Invoke(startingSnapshot);
            await StartSessionAsync(sessionId).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async ValueTask WriteActiveAsync(string data, CancellationToken cancellationToken)
    {
        TerminalSessionId activeSessionId;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            activeSessionId = tabRegistry.ActiveSessionId
                ?? throw new InvalidOperationException("No terminal tab is active.");
        }

        await WriteAsync(activeSessionId, data, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask WriteAsync(TerminalSessionId sessionId, string data, CancellationToken cancellationToken)
    {
        SessionEntry entry;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (tabRegistry.Contains(sessionId) == false || sessions.TryGetValue(sessionId, out entry!) == false)
            {
                throw new InvalidOperationException("The terminal session is unavailable.");
            }
        }

        try
        {
            await entry.Session.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            MarkFailed(entry.SessionId, entry);

            throw;
        }
    }

    internal bool Resize(TerminalSessionId sessionId, int columns, int rows)
    {
        ValidateSize(columns, rows);

        SessionEntry? entry;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (tabRegistry.Contains(sessionId) == false)
            {
                return false;
            }

            this.columns = columns;
            this.rows = rows;
            sessions.TryGetValue(sessionId, out entry);
        }

        ResizeEntry(entry, columns, rows);

        return true;
    }

    internal void ResizeAll(int columns, int rows)
    {
        ValidateSize(columns, rows);

        SessionEntry[] currentSessions;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            this.columns = columns;
            this.rows = rows;
            currentSessions = sessions.Values.ToArray();
        }

        foreach (var entry in currentSessions)
        {
            ResizeEntry(entry, columns, rows);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var shutdownStopwatch = Stopwatch.StartNew();
        var operationLockAcquired = await operationLock
            .WaitAsync(shutdownTimeout)
            .ConfigureAwait(false);
        SessionEntry[] ownedSessions;
        try
        {
            lock (stateLock)
            {
                if (isDisposed == true)
                {
                    return;
                }

                isDisposed = true;
                pendingConfirmation = null;
                ownedSessions = sessions.Values.ToArray();
                sessions.Clear();
                tabShells.Clear();
                sessionGenerations.Clear();
                newOutputStates.Clear();
            }

            foreach (var entry in ownedSessions)
            {
                Detach(entry);
            }
        }
        finally
        {
            if (operationLockAcquired == true)
            {
                operationLock.Release();
            }
        }

        if (operationLockAcquired == false)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Shutdown",
                                "Terminal workspace operations exceeded the shutdown deadline; cleanup was started without waiting for the operation gate.");
        }

        var cleanupTasks = ownedSessions
            .Select(entry => DisposeSessionAsync(entry, sessionCloseTimeout, "ShutdownSession"))
            .ToArray();
        var cleanup = Task.WhenAll(cleanupTasks);
        var remainingTimeout = shutdownTimeout - shutdownStopwatch.Elapsed;

        if (remainingTimeout <= TimeSpan.Zero)
        {
            ObserveLateCleanup(cleanup, "Shutdown");
            return;
        }

        try
        {
            await cleanup.WaitAsync(remainingTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Shutdown",
                                "Terminal session cleanup exceeded the workspace shutdown deadline.", exception);
            ObserveLateCleanup(cleanup, "Shutdown");
        }
    }

    private async Task StartSessionAsync(TerminalSessionId sessionId)
    {
        ShellLaunchSpec launchSpec;
        lock (stateLock)
        {
            if (tabShells.TryGetValue(sessionId, out launchSpec!) == false)
            {
                throw new InvalidOperationException("The terminal tab has no captured shell configuration.");
            }
        }

        ITerminalSession? session = null;
        SessionEntry? entry = null;
        var wasRegistered = false;

        try
        {
            var workingDirectory = TerminalStartingDirectory.GetRequiredExisting(launchSpec.WorkingDirectory);
            launchSpec = launchSpec with { WorkingDirectory = workingDirectory };
            session = sessionFactory.Start(launchSpec, columns, rows);
            entry = new SessionEntry(sessionId, session);
            entry.OutputHandler = data => OnOutputReceived(entry, data);
            entry.ExitHandler = exitCode => OnExited(entry, exitCode);
            session.OutputReceived += entry.OutputHandler;
            session.Exited += entry.ExitHandler;

            lock (stateLock)
            {
                ThrowIfDisposed();
                if (tabRegistry.Contains(sessionId) == false)
                {
                    throw new InvalidOperationException("The terminal tab was closed while starting.");
                }

                sessions.Add(sessionId, entry);
                wasRegistered = true;
            }

            session.BeginReading();

            TerminalWorkspaceSnapshot snapshot;
            lock (stateLock)
            {
                ThrowIfDisposed();
                if (sessions.TryGetValue(sessionId, out var currentEntry) == false ||
                    ReferenceEquals(currentEntry, entry) == false)
                {
                    throw new InvalidOperationException("The terminal session changed while it was starting.");
                }

                var tab = tabRegistry.GetRequired(sessionId);
                if (tab.State == TerminalSessionState.Starting)
                {
                    tabRegistry.SetState(sessionId, TerminalSessionState.Running);
                }

                snapshot = tabRegistry.CreateSnapshot();
            }

            WorkspaceChanged?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
            if (entry is not null)
            {
                var disposeEntry = wasRegistered == false;
                lock (stateLock)
                {
                    if (sessions.TryGetValue(sessionId, out var currentEntry) == true &&
                        ReferenceEquals(currentEntry, entry) == true)
                    {
                        sessions.Remove(sessionId);
                        disposeEntry = true;
                    }
                }

                Detach(entry);
                if (disposeEntry == true)
                {
                    await DisposeSessionAsync(entry, sessionCloseTimeout, "FailedSessionStart").ConfigureAwait(false);
                }
            }
            else if (session is not null)
            {
                await DisposeUnregisteredSessionAsync(session).ConfigureAwait(false);
            }

            TerminalWorkspaceSnapshot? failedSnapshot = null;
            lock (stateLock)
            {
                if (tabRegistry.Contains(sessionId) == true)
                {
                    tabRegistry.SetState(sessionId, TerminalSessionState.Failed);
                    failedSnapshot = tabRegistry.CreateSnapshot();
                }
            }

            if (failedSnapshot is not null)
            {
                WorkspaceChanged?.Invoke(failedSnapshot);
            }

            diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "StartSession",
                                "A terminal session could not be started.", exception);

            throw;
        }
    }

    private bool SelectCore(Func<bool> select)
    {
        SessionEntry? selectedSession = null;
        TerminalWorkspaceSnapshot snapshot;
        TerminalNewOutputStateSnapshot? outputSnapshot = null;
        int currentColumns;
        int currentRows;

        lock (stateLock)
        {
            ThrowIfUnavailable();
            var previousActiveSessionId = tabRegistry.ActiveSessionId;
            if (select() == false)
            {
                return false;
            }

            if (tabRegistry.ActiveSessionId is { } activeSessionId)
            {
                sessions.TryGetValue(activeSessionId, out selectedSession);
                if (newOutputStates.TryGetValue(activeSessionId, out var hasNewOutput) == true &&
                    hasNewOutput == true)
                {
                    newOutputStates[activeSessionId] = false;
                    outputSnapshot = CreateNewOutputSnapshotLocked();
                }
            }

            if (previousActiveSessionId != tabRegistry.ActiveSessionId)
            {
                CancelPendingPasteLocked();
            }

            currentColumns = columns;
            currentRows = rows;
            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        if (outputSnapshot is not null)
        {
            NewOutputStateChanged?.Invoke(outputSnapshot);
        }

        ResizeEntry(selectedSession, currentColumns, currentRows);

        return true;
    }

    private TerminalTab AddRestoredTab(TerminalWorkspaceTabConfiguration configuration, ShellLaunchSpec automaticShell,
                                       Func<TerminalShellKind, ShellLaunchSpec> shellResolver,
                                       out Exception? resolutionFailure)
    {
        ShellLaunchSpec? restoredShell = null;
        resolutionFailure = null;
        try
        {
            var resolved = configuration.ShellKind == TerminalShellKind.Automatic
                ? automaticShell
                : shellResolver(configuration.ShellKind);
            restoredShell = resolved with { WorkingDirectory = configuration.StartingDirectory };
        }
        catch (Exception exception) when (IsRecoverableRestoreException(exception) == true)
        {
            resolutionFailure = exception;
        }

        TerminalTab tab;
        TerminalWorkspaceSnapshot snapshot;
        lock (stateLock)
        {
            ThrowIfDisposed();
            tab = tabRegistry.AddRestored(configuration);
            InitializeSessionLifetimeLocked(tab.SessionId);
            CancelPendingPasteLocked();
            if (restoredShell is not null)
            {
                tabShells.Add(tab.SessionId, restoredShell);
            }

            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        return tab;
    }

    private void MarkRestoredTabFailed(TerminalSessionId sessionId, Exception exception)
    {
        TerminalWorkspaceSnapshot snapshot;
        lock (stateLock)
        {
            tabRegistry.SetState(sessionId, TerminalSessionState.Failed);
            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "RestoreSession",
                            "A restored terminal shell kind could not be resolved.", exception);
    }

    private bool UpdateWorkspace(Func<bool> update)
    {
        TerminalWorkspaceSnapshot snapshot;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (update() == false)
            {
                return false;
            }

            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        return true;
    }

    private void ResizeEntry(SessionEntry? entry, int columns, int rows)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            entry.Session.Resize(columns, rows);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
            MarkFailed(entry.SessionId, entry);
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "ResizeSession",
                                "A terminal session rejected its resize request.", exception);
        }
    }

    private void MarkFailed(TerminalSessionId sessionId, SessionEntry expectedEntry)
    {
        TerminalWorkspaceSnapshot? snapshot = null;
        lock (stateLock)
        {
            if (sessions.TryGetValue(sessionId, out var currentEntry) == true &&
                ReferenceEquals(currentEntry, expectedEntry) == true && tabRegistry.Contains(sessionId) == true)
            {
                tabRegistry.SetState(sessionId, TerminalSessionState.Failed);
                snapshot = tabRegistry.CreateSnapshot();
            }
        }

        if (snapshot is not null)
        {
            WorkspaceChanged?.Invoke(snapshot);
        }
    }

    private void OnOutputReceived(SessionEntry expectedEntry, string data)
    {
        TerminalNewOutputStateSnapshot? outputSnapshot = null;
        lock (stateLock)
        {
            if (sessions.TryGetValue(expectedEntry.SessionId, out var currentEntry) == false ||
                ReferenceEquals(currentEntry, expectedEntry) == false)
            {
                return;
            }

            if (data.Length > 0 && tabRegistry.ActiveSessionId != expectedEntry.SessionId &&
                newOutputStates[expectedEntry.SessionId] == false)
            {
                newOutputStates[expectedEntry.SessionId] = true;
                outputSnapshot = CreateNewOutputSnapshotLocked();
            }
        }

        OutputReceived?.Invoke(new TerminalSessionOutput(expectedEntry.SessionId, data));
        if (outputSnapshot is not null)
        {
            NewOutputStateChanged?.Invoke(outputSnapshot);
        }
    }

    private void OnExited(SessionEntry expectedEntry, uint exitCode)
    {
        TerminalWorkspaceSnapshot snapshot;
        lock (stateLock)
        {
            if (sessions.TryGetValue(expectedEntry.SessionId, out var currentEntry) == false ||
                ReferenceEquals(currentEntry, expectedEntry) == false ||
                tabRegistry.Contains(expectedEntry.SessionId) == false)
            {
                return;
            }

            tabRegistry.SetState(expectedEntry.SessionId, TerminalSessionState.Exited, exitCode);
            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        SessionExited?.Invoke(new TerminalSessionExit(expectedEntry.SessionId, exitCode));
    }

    private TerminalConfirmationToken CreateConfirmationTokenLocked(TerminalSessionId sessionId)
    {
        if (sessionGenerations.TryGetValue(sessionId, out var generation) == false)
        {
            throw new InvalidOperationException("The terminal session lifetime is unavailable.");
        }

        var requestId = confirmationRequestIdFactory();
        var session = new TerminalSessionReference(sessionId.Value, generation);
        return new TerminalConfirmationToken(requestId, session);
    }

    private bool TryConsumeConfirmationLocked(TerminalConfirmationResponse response, ConfirmationKind expectedKind,
                                              out string? clipboardText)
    {
        clipboardText = null;
        if (pendingConfirmation is null || pendingConfirmation.Kind != expectedKind)
        {
            return false;
        }

        var sessionId = new TerminalSessionId(response.Token.Session.SessionId);
        if (sessionGenerations.TryGetValue(sessionId, out var generation) == false)
        {
            return false;
        }

        var currentSession = new TerminalSessionReference(sessionId.Value, generation);
        if (response.CanApplyTo(pendingConfirmation.Token, currentSession) == false)
        {
            return false;
        }

        if (expectedKind == ConfirmationKind.Paste && tabRegistry.ActiveSessionId != sessionId)
        {
            pendingConfirmation = null;
            return false;
        }

        clipboardText = pendingConfirmation.ClipboardText;
        pendingConfirmation = null;
        return true;
    }

    private void InitializeSessionLifetimeLocked(TerminalSessionId sessionId)
    {
        sessionGenerations.Add(sessionId, 1);
        newOutputStates.Add(sessionId, false);
    }

    private void AdvanceSessionLifetimeLocked(TerminalSessionId sessionId)
    {
        if (sessionGenerations.TryGetValue(sessionId, out var generation) == false)
        {
            throw new InvalidOperationException("The terminal session lifetime is unavailable.");
        }

        sessionGenerations[sessionId] = checked(generation + 1);
        newOutputStates[sessionId] = false;
        CancelPendingConfirmationForSessionLocked(sessionId);
    }

    private void CancelPendingConfirmationForSessionLocked(TerminalSessionId sessionId)
    {
        if (pendingConfirmation?.Token.Session.SessionId == sessionId.Value)
        {
            pendingConfirmation = null;
        }
    }

    private void CancelPendingPasteLocked()
    {
        if (pendingConfirmation?.Kind == ConfirmationKind.Paste)
        {
            pendingConfirmation = null;
        }
    }

    private TerminalNewOutputStateSnapshot CreateNewOutputSnapshotLocked()
    {
        var states = sessionGenerations.Select(pair =>
            new TerminalSessionNewOutputState(new TerminalSessionReference(pair.Key.Value, pair.Value),
                                              newOutputStates[pair.Key]));
        return new TerminalNewOutputStateSnapshot(states);
    }

    private async Task DisposeSessionAsync(SessionEntry? entry, TimeSpan timeout, string operation)
    {
        if (entry is null)
        {
            return;
        }

        await DisposeSessionAsync(entry.Session, entry.SessionId, timeout, operation)
            .ConfigureAwait(false);
    }

    private async Task DisposeUnregisteredSessionAsync(ITerminalSession session)
    {
        await DisposeSessionAsync(session, null, sessionCloseTimeout, "FailedSessionStart").ConfigureAwait(false);
    }

    private async Task DisposeSessionAsync(ITerminalSession session, TerminalSessionId? sessionId, TimeSpan timeout,
                                           string operation)
    {
        var disposeTask = session.DisposeAsync().AsTask();
        try
        {
            await disposeTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation,
                                $"Terminal session {sessionId?.ToString() ?? "unregistered"} exceeded its cleanup deadline.",
                                exception);
            ObserveLateCleanup(disposeTask, operation);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation,
                                $"Terminal session {sessionId?.ToString() ?? "unregistered"} cleanup failed.",
                                exception);
        }
    }

    private void ObserveLateCleanup(Task cleanup, string operation)
    {
        _ = cleanup.ContinueWith(completedTask => diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation,
                                                                      "A terminal session cleanup task failed after its deadline.",
                                                                      completedTask.Exception),
                                 CancellationToken.None,
                                 TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                                 TaskScheduler.Default);
    }

    private static void Detach(SessionEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.Session.OutputReceived -= entry.OutputHandler;
        entry.Session.Exited -= entry.ExitHandler;
    }

    private void ThrowIfUnavailable()
    {
        ThrowIfDisposed();
        if (isStarted == false)
        {
            throw new InvalidOperationException("The terminal workspace has not started.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private static void ValidateSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return timeout;
    }

    private static bool IsRecoverableRestoreException(Exception exception)
    {
        return exception is ArgumentException or
               IOException or
               InvalidOperationException or
               NotSupportedException or
               UnauthorizedAccessException or
               System.ComponentModel.Win32Exception;
    }

    private sealed class SessionEntry
    {
        internal SessionEntry(TerminalSessionId sessionId, ITerminalSession session)
        {
            SessionId = sessionId;
            Session = session;
        }

        internal TerminalSessionId SessionId { get; }

        internal ITerminalSession Session { get; }

        internal Action<string>? OutputHandler { get; set; }

        internal Action<uint>? ExitHandler { get; set; }
    }

    private enum ConfirmationKind
    {
        Close,
        Paste,
    }

    private sealed record PendingConfirmation(TerminalConfirmationToken Token, ConfirmationKind Kind,
                                              string? ClipboardText);
}
