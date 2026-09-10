using System.Diagnostics;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalSavedTabServiceTests
{
    [TestMethod]
    public async Task CrudAsyncValidChangesPreservesRunningWorkspaceAndCommitsIndependentSnapshots()
    {
        var directory = CreateTestDirectory();
        try
        {
            var sessionFactory = new FakeTerminalSessionFactory();
            await using var coordinator = CreateCoordinator(sessionFactory);
            var originalTab = await coordinator.StartAsync(CreateShell(directory), 80, 24, CancellationToken.None);
            var originalSession = sessionFactory.Sessions[0];
            var store = new FakeSavedTabStore(CreateEmptyLoadResult());
            await using var service = CreateService(store, coordinator);
            await service.InitializeAsync(CancellationToken.None);

            var create = await service.CreateAsync(
                new TerminalSavedTabCreateRequest(CreateRequestId(1), "서버", directory, TerminalShellKind.Pwsh),
                CancellationToken.None);
            var savedTab = create.Snapshot!.Tabs.Single();
            var update = await service.UpdateAsync(
                new TerminalSavedTabUpdateRequest(CreateRequestId(2),
                                                  new TerminalSavedTab(savedTab.SavedTabId, "서버 수정", directory,
                                                                       TerminalShellKind.Cmd)),
                CancellationToken.None);
            var delete = await service.DeleteAsync(
                new TerminalSavedTabDeleteRequest(CreateRequestId(3), savedTab.SavedTabId),
                CancellationToken.None);

            Assert.AreEqual(TerminalSavedTabOperationStatus.Succeeded, create.Status);
            Assert.AreEqual(TerminalSavedTabOperationStatus.Succeeded, update.Status);
            Assert.AreEqual(TerminalSavedTabOperationStatus.Succeeded, delete.Status);
            Assert.IsEmpty(delete.Snapshot!.Tabs);
            Assert.HasCount(3, store.SavedSnapshots);
            Assert.HasCount(1, coordinator.Snapshot.Tabs);
            Assert.AreEqual(originalTab.SessionId, coordinator.Snapshot.Tabs[0].SessionId);
            Assert.AreSame(originalSession, sessionFactory.Sessions[0]);
            Assert.AreEqual(0, originalSession.DisposeCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LaunchAsyncSavedDefinitionStartsSelectedShellAndDirectoryWithoutReplacingExistingSession()
    {
        var directory = CreateTestDirectory();
        try
        {
            var sessionFactory = new FakeTerminalSessionFactory();
            await using var coordinator = CreateCoordinator(sessionFactory);
            var originalTab = await coordinator.StartAsync(CreateShell(directory), 91, 27, CancellationToken.None);
            var originalSession = sessionFactory.Sessions[0];
            var savedTabId = new TerminalSavedTabId(CreateGuid(1));
            var savedTab = new TerminalSavedTab(savedTabId, "프로젝트", directory, TerminalShellKind.Cmd);
            var storedSnapshot = new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion,
                                                               [savedTab]);
            var store = new FakeSavedTabStore(new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.Loaded,
                                                                              storedSnapshot, null));
            await using var service = CreateService(store, coordinator);
            await service.InitializeAsync(CancellationToken.None);

            var result = await service.LaunchAsync(
                new TerminalSavedTabLaunchRequest(CreateRequestId(1), savedTabId), CancellationToken.None);

            Assert.AreEqual(TerminalSavedTabLaunchStatus.Started, result.Status);
            Assert.IsNotNull(result.Session);
            var launchedSession = result.Session.Value;
            Assert.AreNotEqual(originalTab.SessionId.Value, launchedSession.SessionId);
            Assert.HasCount(2, sessionFactory.Sessions);
            Assert.AreSame(originalSession, sessionFactory.Sessions[0]);
            Assert.AreEqual(0, originalSession.DisposeCount);
            Assert.AreEqual("C:\\shells\\cmd.exe", sessionFactory.StartRequests[1].Shell.ExecutablePath);
            Assert.AreEqual(directory, sessionFactory.StartRequests[1].Shell.WorkingDirectory);
            Assert.AreEqual(91, sessionFactory.StartRequests[1].Columns);
            Assert.AreEqual(27, sessionFactory.StartRequests[1].Rows);
            Assert.AreEqual("프로젝트", coordinator.Snapshot.Tabs.Single(tab =>
                tab.SessionId.Value == launchedSession.SessionId).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InitializeAndMutationAsyncFutureSchemaDoesNotOverwriteStoredDocument()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new FakeSavedTabStore(new TerminalSavedTabsLoadResult(
                TerminalSavedTabsLoadStatus.FutureSchema, null, "Future schema."));
            await using var coordinator = CreateCoordinator(new FakeTerminalSessionFactory());
            await using var service = CreateService(store, coordinator);

            var load = await service.InitializeAsync(CancellationToken.None);
            var result = await service.CreateAsync(
                new TerminalSavedTabCreateRequest(CreateRequestId(1), "보존", directory, TerminalShellKind.Pwsh),
                CancellationToken.None);

            Assert.AreEqual(TerminalSavedTabsLoadStatus.FutureSchema, load.Status);
            Assert.AreEqual(TerminalSavedTabOperationStatus.Failed, result.Status);
            Assert.AreEqual(0, store.SaveCount);
            Assert.IsEmpty(service.Snapshot.Tabs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InitializeAndDisposeAsyncUnresponsiveStoreReturnWithinConfiguredDeadlines()
    {
        var store = new FakeSavedTabStore(CreateEmptyLoadResult())
        {
            LoadGate = new TaskCompletionSource<TerminalSavedTabsLoadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        await using var coordinator = CreateCoordinator(new FakeTerminalSessionFactory());
        var service = new TerminalSavedTabService(store, coordinator, new CapturingDiagnosticLog(),
                                                  ResolveShell, ioTimeout: TimeSpan.FromMilliseconds(40),
                                                  shutdownFlushTimeout: TimeSpan.FromMilliseconds(40));
        var stopwatch = Stopwatch.StartNew();

        var load = await service.InitializeAsync(CancellationToken.None);
        await service.DisposeAsync();

        Assert.AreEqual(TerminalSavedTabsLoadStatus.Invalid, load.Status);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task CreateAsyncWriteFailureDoesNotPublishCandidateOrLogSavedValues()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new FakeSavedTabStore(CreateEmptyLoadResult())
            {
                SaveException = new IOException($"Cannot write {directory} secret-name"),
            };
            var log = new CapturingDiagnosticLog();
            await using var coordinator = CreateCoordinator(new FakeTerminalSessionFactory());
            await using var service = new TerminalSavedTabService(store, coordinator, log, ResolveShell);
            await service.InitializeAsync(CancellationToken.None);

            var result = await service.CreateAsync(
                new TerminalSavedTabCreateRequest(CreateRequestId(1), "secret-name", directory,
                                                  TerminalShellKind.Pwsh), CancellationToken.None);

            Assert.AreEqual(TerminalSavedTabOperationStatus.Failed, result.Status);
            Assert.IsNull(result.Snapshot);
            Assert.IsEmpty(service.Snapshot.Tabs);
            Assert.IsFalse(log.Messages.Any(message =>
                message.Contains(directory, StringComparison.OrdinalIgnoreCase) ||
                message.Contains("secret-name", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CreateAndDisposeAsyncUnresponsiveWriterReturnWithinConfiguredDeadlines()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new FakeSavedTabStore(CreateEmptyLoadResult())
            {
                SaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            await using var coordinator = CreateCoordinator(new FakeTerminalSessionFactory());
            var service = new TerminalSavedTabService(store, coordinator, new CapturingDiagnosticLog(),
                                                      ResolveShell, ioTimeout: TimeSpan.FromMilliseconds(40),
                                                      shutdownFlushTimeout: TimeSpan.FromMilliseconds(40));
            await service.InitializeAsync(CancellationToken.None);
            var stopwatch = Stopwatch.StartNew();

            var result = await service.CreateAsync(
                new TerminalSavedTabCreateRequest(CreateRequestId(1), "bounded", directory,
                                                  TerminalShellKind.Pwsh), CancellationToken.None);
            await service.DisposeAsync();

            Assert.AreEqual(TerminalSavedTabOperationStatus.Failed, result.Status);
            Assert.IsEmpty(service.Snapshot.Tabs);
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TerminalSavedTabService CreateService(FakeSavedTabStore store,
                                                         TerminalSessionCoordinator coordinator)
    {
        return new TerminalSavedTabService(store, coordinator, new CapturingDiagnosticLog(), ResolveShell,
                                           () => new TerminalSavedTabId(CreateGuid(store.SaveCount + 1)));
    }

    private static TerminalSessionCoordinator CreateCoordinator(FakeTerminalSessionFactory sessionFactory)
    {
        return new TerminalSessionCoordinator(sessionFactory, new CapturingDiagnosticLog(),
                                              sessionIdFactory: CreateSessionId);
    }

    private static ShellLaunchSpec ResolveShell(TerminalShellKind shellKind)
    {
        var executable = shellKind switch
        {
            TerminalShellKind.Automatic => "C:\\shells\\pwsh.exe",
            TerminalShellKind.Pwsh => "C:\\shells\\pwsh.exe",
            TerminalShellKind.PowerShell => "C:\\shells\\powershell.exe",
            TerminalShellKind.Cmd => "C:\\shells\\cmd.exe",
            _ => throw new FileNotFoundException(),
        };
        return new ShellLaunchSpec(executable, string.Empty, "C:\\");
    }

    private static ShellLaunchSpec CreateShell(string directory)
    {
        return new ShellLaunchSpec("C:\\shells\\pwsh.exe", "-NoLogo", directory);
    }

    private static TerminalSavedTabsLoadResult CreateEmptyLoadResult()
    {
        var snapshot = new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, []);
        return new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.NotFound, snapshot, null);
    }

    private static TerminalSavedTabRequestId CreateRequestId(int suffix)
    {
        return new TerminalSavedTabRequestId(Guid.Parse($"60000000-0000-0000-0000-{suffix:D12}"));
    }

    private static TerminalSessionId CreateSessionId()
    {
        return new TerminalSessionId(Guid.NewGuid());
    }

    private static Guid CreateGuid(int suffix)
    {
        return Guid.Parse($"50000000-0000-0000-0000-{suffix:D12}");
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starboard-saved-tab-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeSavedTabStore : ITerminalSavedTabStore
    {
        private readonly TerminalSavedTabsLoadResult loadResult;

        internal FakeSavedTabStore(TerminalSavedTabsLoadResult loadResult)
        {
            this.loadResult = loadResult;
        }

        internal TaskCompletionSource<TerminalSavedTabsLoadResult>? LoadGate { get; init; }

        internal Exception? SaveException { get; init; }

        internal TaskCompletionSource? SaveGate { get; init; }

        internal int SaveCount { get; private set; }

        internal List<TerminalSavedTabsSnapshot> SavedSnapshots { get; } = [];

        public Task<TerminalSavedTabsLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return LoadGate?.Task ?? Task.FromResult(loadResult);
        }

        public Task SaveAsync(TerminalSavedTabsSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            if (SaveGate is not null)
            {
                return SaveGate.Task;
            }

            SavedSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTerminalSessionFactory : ITerminalSessionFactory
    {
        internal List<FakeTerminalSession> Sessions { get; } = [];

        internal List<StartRequest> StartRequests { get; } = [];

        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
        {
            var session = new FakeTerminalSession();
            Sessions.Add(session);
            StartRequests.Add(new StartRequest(shell, columns, rows));
            return session;
        }
    }

    private sealed class FakeTerminalSession : ITerminalSession
    {
        public event Action<string>? OutputReceived
        {
            add { }
            remove { }
        }

        public event Action<uint>? Exited
        {
            add { }
            remove { }
        }

        internal int DisposeCount { get; private set; }

        public void BeginReading()
        {
        }

        public ValueTask WriteAsync(string data, CancellationToken cancellationToken)
        {
            _ = data;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void Resize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record StartRequest(ShellLaunchSpec Shell, int Columns, int Rows);

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        internal List<string> Messages { get; } = [];

        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
            _ = level;
            _ = subsystem;
            _ = operation;
            _ = exception;
            Messages.Add(message);
        }
    }
}
