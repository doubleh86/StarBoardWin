using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
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

    private ShellLaunchSpec? defaultShell;
    private bool isStarted;
    private bool isDisposed;
    private int columns = 80;
    private int rows = 24;

    internal TerminalSessionCoordinator(
        ITerminalSessionFactory sessionFactory,
        IDiagnosticLog diagnosticLog,
        Func<TerminalSessionId>? sessionIdFactory = null,
        int maximumTabs = TerminalTabRegistry.DefaultMaximumTabs,
        TimeSpan? sessionCloseTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        this.sessionFactory = sessionFactory;
        this.diagnosticLog = diagnosticLog;
        tabRegistry = new TerminalTabRegistry(sessionIdFactory, maximumTabs);
        this.sessionCloseTimeout = ValidateTimeout(
            sessionCloseTimeout ?? DefaultSessionCloseTimeout,
            nameof(sessionCloseTimeout));
        this.shutdownTimeout = ValidateTimeout(
            shutdownTimeout ?? DefaultShutdownTimeout,
            nameof(shutdownTimeout));
    }

    internal event Action<TerminalWorkspaceSnapshot>? WorkspaceChanged;

    internal event Action<TerminalSessionOutput>? OutputReceived;

    internal event Action<TerminalSessionExit>? SessionExited;

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

    internal async Task<TerminalTab> StartAsync(
        ShellLaunchSpec shell,
        int columns,
        int rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shell);
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
            this.columns = columns;
            this.rows = rows;
            isStarted = true;

            TerminalTab tab;
            TerminalWorkspaceSnapshot snapshot;
            lock (stateLock)
            {
                tab = tabRegistry.Add();
                tabShells.Add(tab.SessionId, shell);
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
                tab = tabRegistry.Add();
                tabShells.Add(
                    tab.SessionId,
                    defaultShell ?? throw new InvalidOperationException(
                        "The terminal shell has not been configured."));
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

    internal void UpdateDefaultShell(ShellLaunchSpec shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        lock (stateLock)
        {
            ThrowIfUnavailable();
            defaultShell = shell;
        }
    }

    internal async Task<bool> CloseAsync(
        TerminalSessionId sessionId,
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();

            SessionEntry? closedSession;
            TerminalTabCloseResult? closeResult;
            TerminalWorkspaceSnapshot snapshot;
            lock (stateLock)
            {
                closeResult = tabRegistry.Close(sessionId);
                if (closeResult is null)
                {
                    return false;
                }

                sessions.Remove(sessionId, out closedSession);
                tabShells.Remove(sessionId);
                if (closeResult.ReplacementTab is not null)
                {
                    tabShells.Add(
                        closeResult.ReplacementTab.SessionId,
                        defaultShell ?? throw new InvalidOperationException(
                            "The terminal shell has not been configured."));
                }
                snapshot = tabRegistry.CreateSnapshot();
            }

            Detach(closedSession);
            WorkspaceChanged?.Invoke(snapshot);

            ExceptionDispatchInfo? replacementFailure = null;
            if (closeResult.ReplacementTab is not null)
            {
                try
                {
                    await StartSessionAsync(closeResult.ReplacementTab.SessionId)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    replacementFailure = ExceptionDispatchInfo.Capture(exception);
                }
            }

            await DisposeSessionAsync(
                closedSession,
                sessionCloseTimeout,
                "CloseSession").ConfigureAwait(false);

            replacementFailure?.Throw();
            return true;
        }
        finally
        {
            operationLock.Release();
        }
    }

    internal async Task RestartAsync(
        TerminalSessionId sessionId,
        CancellationToken cancellationToken)
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
                restartingSnapshot = tabRegistry.CreateSnapshot();
            }

            Detach(previousSession);
            WorkspaceChanged?.Invoke(restartingSnapshot);

            await DisposeSessionAsync(
                previousSession,
                sessionCloseTimeout,
                "RestartSession").ConfigureAwait(false);

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

    internal async ValueTask WriteActiveAsync(
        string data,
        CancellationToken cancellationToken)
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

    internal async ValueTask WriteAsync(
        TerminalSessionId sessionId,
        string data,
        CancellationToken cancellationToken)
    {
        SessionEntry entry;
        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (tabRegistry.Contains(sessionId) == false ||
                sessions.TryGetValue(sessionId, out entry!) == false)
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

    internal bool Resize(
        TerminalSessionId sessionId,
        int columns,
        int rows)
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
                ownedSessions = sessions.Values.ToArray();
                sessions.Clear();
                tabShells.Clear();
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
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "Shutdown",
                "Terminal workspace operations exceeded the shutdown deadline; cleanup was started without waiting for the operation gate.");
        }

        var cleanupTasks = ownedSessions
            .Select(entry => DisposeSessionAsync(
                entry,
                sessionCloseTimeout,
                "ShutdownSession"))
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
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "Shutdown",
                "Terminal session cleanup exceeded the workspace shutdown deadline.",
                exception);
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
                throw new InvalidOperationException(
                    "The terminal tab has no captured shell configuration.");
            }
        }

        ITerminalSession? session = null;
        SessionEntry? entry = null;
        var wasRegistered = false;

        try
        {
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
                    throw new InvalidOperationException(
                        "The terminal session changed while it was starting.");
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
                    await DisposeSessionAsync(
                        entry,
                        sessionCloseTimeout,
                        "FailedSessionStart").ConfigureAwait(false);
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

            diagnosticLog.Write(
                DiagnosticLevel.Error,
                "Terminal",
                "StartSession",
                "A terminal session could not be started.",
                exception);
            throw;
        }
    }

    private bool SelectCore(Func<bool> select)
    {
        SessionEntry? selectedSession = null;
        TerminalWorkspaceSnapshot snapshot;
        int currentColumns;
        int currentRows;

        lock (stateLock)
        {
            ThrowIfUnavailable();
            if (select() == false)
            {
                return false;
            }

            if (tabRegistry.ActiveSessionId is { } activeSessionId)
            {
                sessions.TryGetValue(activeSessionId, out selectedSession);
            }

            currentColumns = columns;
            currentRows = rows;
            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        ResizeEntry(selectedSession, currentColumns, currentRows);
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
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                "ResizeSession",
                "A terminal session rejected its resize request.",
                exception);
        }
    }

    private void MarkFailed(TerminalSessionId sessionId, SessionEntry expectedEntry)
    {
        TerminalWorkspaceSnapshot? snapshot = null;
        lock (stateLock)
        {
            if (sessions.TryGetValue(sessionId, out var currentEntry) == true &&
                ReferenceEquals(currentEntry, expectedEntry) == true &&
                tabRegistry.Contains(sessionId) == true)
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
        lock (stateLock)
        {
            if (sessions.TryGetValue(expectedEntry.SessionId, out var currentEntry) == false ||
                ReferenceEquals(currentEntry, expectedEntry) == false)
            {
                return;
            }
        }

        OutputReceived?.Invoke(new TerminalSessionOutput(expectedEntry.SessionId, data));
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

            tabRegistry.SetState(
                expectedEntry.SessionId,
                TerminalSessionState.Exited,
                exitCode);
            snapshot = tabRegistry.CreateSnapshot();
        }

        WorkspaceChanged?.Invoke(snapshot);
        SessionExited?.Invoke(new TerminalSessionExit(expectedEntry.SessionId, exitCode));
    }

    private async Task DisposeSessionAsync(
        SessionEntry? entry,
        TimeSpan timeout,
        string operation)
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
        await DisposeSessionAsync(
            session,
            null,
            sessionCloseTimeout,
            "FailedSessionStart").ConfigureAwait(false);
    }

    private async Task DisposeSessionAsync(
        ITerminalSession session,
        TerminalSessionId? sessionId,
        TimeSpan timeout,
        string operation)
    {
        var disposeTask = session.DisposeAsync().AsTask();
        try
        {
            await disposeTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                operation,
                $"Terminal session {sessionId?.ToString() ?? "unregistered"} exceeded its cleanup deadline.",
                exception);
            ObserveLateCleanup(disposeTask, operation);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                operation,
                $"Terminal session {sessionId?.ToString() ?? "unregistered"} cleanup failed.",
                exception);
        }
    }

    private void ObserveLateCleanup(Task cleanup, string operation)
    {
        _ = cleanup.ContinueWith(
            completedTask => diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Terminal",
                operation,
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

    private sealed class SessionEntry
    {
        internal SessionEntry(
            TerminalSessionId sessionId,
            ITerminalSession session)
        {
            SessionId = sessionId;
            Session = session;
        }

        internal TerminalSessionId SessionId { get; }

        internal ITerminalSession Session { get; }

        internal Action<string>? OutputHandler { get; set; }

        internal Action<uint>? ExitHandler { get; set; }
    }
}
