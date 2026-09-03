using System.Diagnostics;
using System.IO;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalSessionCoordinatorTests
{
    private static readonly ShellLaunchSpec TestShell = new(
        "pwsh.exe",
        "-NoLogo",
        "C:\\Test");

    [TestMethod]
    public async Task StartAsyncFirstTabStartsAndActivatesDedicatedSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);

        var tab = await coordinator.StartAsync(TestShell, 100, 30, CancellationToken.None);
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual("PowerShell 1", tab.Name);
        Assert.AreEqual(TerminalSessionState.Running, tab.State);
        Assert.AreEqual(tab.SessionId, snapshot.ActiveSessionId);
        Assert.AreEqual(1, snapshot.Tabs.Count);
        Assert.AreEqual(1, factory.Sessions.Count);
        Assert.AreEqual(1, factory.Sessions[0].BeginReadingCount);
        Assert.AreEqual(100, factory.StartRequests[0].Columns);
        Assert.AreEqual(30, factory.StartRequests[0].Rows);
    }

    [TestMethod]
    public async Task WriteActiveAsyncAfterTabSelectionRoutesOnlyToSelectedSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);

        await coordinator.WriteActiveAsync("second", CancellationToken.None);
        Assert.IsTrue(coordinator.Select(first.SessionId));
        await coordinator.WriteActiveAsync("first", CancellationToken.None);

        CollectionAssert.AreEqual(new List<string> { "first" }, factory.Sessions[0].Writes);
        CollectionAssert.AreEqual(new List<string> { "second" }, factory.Sessions[1].Writes);
        Assert.AreEqual(second.SessionId, coordinator.Snapshot.Tabs[1].SessionId);
    }

    [TestMethod]
    public async Task SessionExitOnOneTabMarksOnlyThatTabExited()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);

        factory.Sessions[0].RaiseExit(23);
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual(TerminalSessionState.Exited, FindTab(snapshot, first.SessionId).State);
        Assert.AreEqual((uint)23, FindTab(snapshot, first.SessionId).ExitCode);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, second.SessionId).State);
        Assert.AreEqual(second.SessionId, snapshot.ActiveSessionId);
    }

    [TestMethod]
    public async Task RestartAsyncOneTabReplacesOnlyItsSessionAndKeepsIdentity()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var originalFirstSession = factory.Sessions[0];
        var originalSecondSession = factory.Sessions[1];

        await coordinator.RestartAsync(first.SessionId, CancellationToken.None);
        originalFirstSession.RaiseExit(99);
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual(1, originalFirstSession.DisposeCount);
        Assert.AreEqual(0, originalSecondSession.DisposeCount);
        Assert.AreEqual(3, factory.Sessions.Count);
        Assert.AreEqual(first.SessionId, FindTab(snapshot, first.SessionId).SessionId);
        Assert.AreEqual("PowerShell 1", FindTab(snapshot, first.SessionId).Name);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, first.SessionId).State);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, second.SessionId).State);
    }

    [TestMethod]
    public async Task CloseAsyncInactiveTabDisposesOnlyClosedSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);

        var closed = await coordinator.CloseAsync(first.SessionId, CancellationToken.None);

        Assert.IsTrue(closed);
        Assert.AreEqual(1, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(second.SessionId, coordinator.Snapshot.ActiveSessionId);
        Assert.AreEqual(1, coordinator.Snapshot.Tabs.Count);
    }

    [TestMethod]
    public async Task CloseAsyncLastTabStartsReplacementBeforeClosedSessionFinishesCleanup()
    {
        var cleanupGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeTerminalSessionFactory(index => new FakeTerminalSession(
            index == 0 ? cleanupGate : null));
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);

        var closeTask = coordinator.CloseAsync(first.SessionId, CancellationToken.None);
        var snapshotDuringCleanup = coordinator.Snapshot;

        Assert.IsFalse(closeTask.IsCompleted);
        Assert.AreEqual(2, factory.Sessions.Count);
        Assert.AreEqual(1, snapshotDuringCleanup.Tabs.Count);
        Assert.AreEqual("PowerShell 2", snapshotDuringCleanup.Tabs[0].Name);
        Assert.AreEqual(snapshotDuringCleanup.Tabs[0].SessionId, snapshotDuringCleanup.ActiveSessionId);

        cleanupGate.TrySetResult();
        Assert.IsTrue(await closeTask);
    }

    [TestMethod]
    public async Task AddAsyncWhenNewSessionFailsKeepsExistingSessionRunning()
    {
        var factory = new FakeTerminalSessionFactory
        {
            FailureStartNumber = 2,
        };
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.AddAsync(CancellationToken.None));
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, first.SessionId).State);
        Assert.AreEqual(TerminalSessionState.Failed, snapshot.Tabs[1].State);
        Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
    }

    [TestMethod]
    public async Task WriteActiveAsyncWhenOneSessionFailsMarksOnlyThatTabFailed()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        factory.Sessions[1].WriteException = new IOException("Simulated input failure.");

        await Assert.ThrowsExactlyAsync<IOException>(
            async () => await coordinator.WriteActiveAsync("input", CancellationToken.None));
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, first.SessionId).State);
        Assert.AreEqual(TerminalSessionState.Failed, FindTab(snapshot, second.SessionId).State);
        Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
    }

    [TestMethod]
    public async Task ResizeAllWhenOneSessionRejectsResizeContinuesOtherSessions()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        factory.Sessions[0].ResizeException = new InvalidOperationException(
            "Simulated resize failure.");

        coordinator.ResizeAll(120, 40);
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual(TerminalSessionState.Failed, FindTab(snapshot, first.SessionId).State);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, second.SessionId).State);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(1, factory.Sessions[1].ResizeCount);
    }

    [TestMethod]
    public async Task AddAsyncAtMaximumTabsDoesNotCreateAnotherShellProcess()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory, maximumTabs: 2);
        _ = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        _ = await coordinator.AddAsync(CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.AddAsync(CancellationToken.None));

        Assert.AreEqual(2, factory.StartRequests.Count);
        Assert.AreEqual(2, factory.Sessions.Count);
    }

    [TestMethod]
    public async Task DisposeAsyncMultipleBlockedSessionsStartsCleanupInParallelAndReturnsByDeadline()
    {
        var cleanupGates = new List<TaskCompletionSource>();
        var factory = new FakeTerminalSessionFactory(_ =>
        {
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cleanupGates.Add(gate);
            return new FakeTerminalSession(gate);
        });
        var coordinator = CreateCoordinator(
            factory,
            sessionCloseTimeout: TimeSpan.FromMilliseconds(40),
            shutdownTimeout: TimeSpan.FromMilliseconds(200));
        _ = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        _ = await coordinator.AddAsync(CancellationToken.None);
        _ = await coordinator.AddAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await coordinator.DisposeAsync();
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.IsTrue(factory.Sessions.All(session => session.DisposeCount == 1));

        foreach (var gate in cleanupGates)
        {
            gate.TrySetResult();
        }
    }

    [TestMethod]
    public async Task DisposeAsyncDuringBlockedCloseUsesOneWorkspaceDeadline()
    {
        var cleanupGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeTerminalSessionFactory(index => new FakeTerminalSession(
            index == 0 ? cleanupGate : null));
        var coordinator = CreateCoordinator(
            factory,
            sessionCloseTimeout: TimeSpan.FromMilliseconds(300),
            shutdownTimeout: TimeSpan.FromMilliseconds(40));
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var closeTask = coordinator.CloseAsync(first.SessionId, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await coordinator.DisposeAsync();
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.IsFalse(closeTask.IsCompleted);
        Assert.AreEqual(1, factory.Sessions[1].DisposeCount);

        cleanupGate.TrySetResult();
        Assert.IsTrue(await closeTask);
    }

    private static TerminalSessionCoordinator CreateCoordinator(
        FakeTerminalSessionFactory factory,
        int maximumTabs = TerminalTabRegistry.DefaultMaximumTabs,
        TimeSpan? sessionCloseTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        var nextIdentifier = 0;
        return new TerminalSessionCoordinator(
            factory,
            new NullDiagnosticLog(),
            () => new TerminalSessionId(CreateGuid(++nextIdentifier)),
            maximumTabs,
            sessionCloseTimeout,
            shutdownTimeout);
    }

    private static TerminalTab FindTab(
        TerminalWorkspaceSnapshot snapshot,
        TerminalSessionId sessionId)
    {
        return snapshot.Tabs.Single(tab => tab.SessionId == sessionId);
    }

    private static Guid CreateGuid(int value)
    {
        return Guid.Parse($"10000000-0000-0000-0000-{value:D12}");
    }

    private sealed class FakeTerminalSessionFactory : ITerminalSessionFactory
    {
        private readonly Func<int, FakeTerminalSession> createSession;

        internal FakeTerminalSessionFactory(Func<int, FakeTerminalSession>? createSession = null)
        {
            this.createSession = createSession ?? (_ => new FakeTerminalSession());
        }

        internal int? FailureStartNumber { get; init; }

        internal List<FakeTerminalSession> Sessions { get; } = [];

        internal List<StartRequest> StartRequests { get; } = [];

        public ITerminalSession Start(
            ShellLaunchSpec shell,
            int columns,
            int rows)
        {
            var startNumber = StartRequests.Count + 1;
            StartRequests.Add(new StartRequest(shell, columns, rows));
            if (FailureStartNumber == startNumber)
            {
                throw new InvalidOperationException("Simulated session start failure.");
            }

            var session = createSession(Sessions.Count);
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class FakeTerminalSession : ITerminalSession
    {
        private readonly TaskCompletionSource? cleanupGate;

        internal FakeTerminalSession(TaskCompletionSource? cleanupGate = null)
        {
            this.cleanupGate = cleanupGate;
        }

        public event Action<string>? OutputReceived;

        public event Action<uint>? Exited;

        internal int BeginReadingCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal int ResizeCount { get; private set; }

        internal List<string> Writes { get; } = [];

        internal Exception? ResizeException { get; set; }

        internal Exception? WriteException { get; set; }

        public void BeginReading()
        {
            BeginReadingCount++;
        }

        public ValueTask WriteAsync(string data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WriteException is not null)
            {
                throw WriteException;
            }

            Writes.Add(data);
            return ValueTask.CompletedTask;
        }

        public void Resize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
            ResizeCount++;
            if (ResizeException is not null)
            {
                throw ResizeException;
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return cleanupGate is null
                ? ValueTask.CompletedTask
                : new ValueTask(cleanupGate.Task);
        }

        internal void RaiseExit(uint exitCode)
        {
            Exited?.Invoke(exitCode);
        }

        internal void RaiseOutput(string data)
        {
            OutputReceived?.Invoke(data);
        }
    }

    private sealed record StartRequest(
        ShellLaunchSpec Shell,
        int Columns,
        int Rows);

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(
            DiagnosticLevel level,
            string subsystem,
            string operation,
            string message,
            Exception? exception = null)
        {
            _ = level;
            _ = subsystem;
            _ = operation;
            _ = message;
            _ = exception;
        }
    }
}
