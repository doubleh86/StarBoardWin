using System.Diagnostics;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalWorkspaceRestoreTests
{
    private static readonly ShellLaunchSpec _testShell = new("pwsh.exe", "-NoLogo", "C:\\Work");
    private static readonly string[] _restoredTabNames = ["첫 탭", "실패 탭", "마지막 탭"];

    [TestMethod]
    public async Task RestoreCreatesFreshSessionsSequentiallyAndIsolatesShellFailure()
    {
        var sessionNumber = 0;
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog(),
                                                                      () => new TerminalSessionId(CreateGuid(10 + ++sessionNumber)));
        var firstId = new TerminalTabConfigurationId(CreateGuid(1));
        var secondId = new TerminalTabConfigurationId(CreateGuid(2));
        var thirdId = new TerminalTabConfigurationId(CreateGuid(3));
        var configuration = new TerminalWorkspaceConfiguration(1,
                                                               [
                                                                   new TerminalWorkspaceTabConfiguration(firstId, "첫 탭", 0,
                                                                                                         "C:\\Work", TerminalShellKind.Pwsh),
                                                                   new TerminalWorkspaceTabConfiguration(secondId, "실패 탭", 1,
                                                                                                         "C:\\Work", TerminalShellKind.PowerShell),
                                                                   new TerminalWorkspaceTabConfiguration(thirdId, "마지막 탭", 2,
                                                                                                         "C:\\Work", TerminalShellKind.Cmd),
                                                               ],
                                                               firstId);

        _ = await coordinator.StartAsync(_testShell, TerminalShellKind.Automatic, configuration,
                                         kind => kind == TerminalShellKind.PowerShell
                                             ? throw new FileNotFoundException("Simulated missing shell.")
                                             : new ShellLaunchSpec($"{kind}.exe", "", "C:\\Default"),
                                         80, 24, CancellationToken.None);

        var snapshot = coordinator.Snapshot;
        CollectionAssert.AreEqual(_restoredTabNames,
                                   snapshot.Tabs.Select(tab => tab.Name).ToArray());
        CollectionAssert.AreEqual(new[] { firstId, secondId, thirdId },
                                   snapshot.Tabs.Select(tab => tab.ConfigurationId).ToArray());
        Assert.AreEqual(TerminalSessionState.Running, snapshot.Tabs[0].State);
        Assert.AreEqual(TerminalSessionState.Failed, snapshot.Tabs[1].State);
        Assert.AreEqual(TerminalSessionState.Running, snapshot.Tabs[2].State);
        Assert.AreEqual(snapshot.Tabs[0].SessionId, snapshot.ActiveSessionId);
        Assert.AreEqual(2, factory.StartRequests.Count);
        Assert.IsTrue(snapshot.Tabs.All(tab => tab.SessionId.Value != tab.ConfigurationId.Value));
    }

    [TestMethod]
    public async Task RestoreMissingFolderFailsOnlyThatTabAndContinuesInOrder()
    {
        var existingDirectory = Path.Combine(Path.GetTempPath(), $"Starboard restore {Guid.NewGuid():N}");
        Directory.CreateDirectory(existingDirectory);
        try
        {
            var missingDirectory = Path.Combine(existingDirectory, "missing");
            var factory = new RecordingSessionFactory();
            var number = 0;
            await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog(),
                                                                          () => new TerminalSessionId(CreateGuid(20 + ++number)));
            var configurations = Enumerable.Range(1, 3)
                .Select(index => new TerminalWorkspaceTabConfiguration(
                    new TerminalTabConfigurationId(CreateGuid(index)), $"탭 {index}", index - 1,
                    index == 2 ? missingDirectory : existingDirectory, TerminalShellKind.Automatic))
                .ToArray();
            var workspace = new TerminalWorkspaceConfiguration(1, configurations,
                                                               configurations[2].ConfigurationId);

            _ = await coordinator.StartAsync(_testShell with { WorkingDirectory = existingDirectory },
                                             TerminalShellKind.Automatic, workspace, _ => _testShell,
                                             80, 24, CancellationToken.None);

            Assert.AreEqual(2, factory.StartRequests.Count);
            Assert.AreEqual(TerminalSessionState.Running, coordinator.Snapshot.Tabs[0].State);
            Assert.AreEqual(TerminalSessionState.Failed, coordinator.Snapshot.Tabs[1].State);
            Assert.AreEqual(TerminalSessionState.Running, coordinator.Snapshot.Tabs[2].State);
            Assert.AreEqual(coordinator.Snapshot.Tabs[2].SessionId, coordinator.Snapshot.ActiveSessionId);
        }
        finally
        {
            Directory.Delete(existingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CoordinatorShutdownDuringRestoreDoesNotStartLaterTabs()
    {
        using var factory = new BlockingStartSessionFactory();
        var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog(),
                                                         sessionCloseTimeout: TimeSpan.FromMilliseconds(40),
                                                         shutdownTimeout: TimeSpan.FromMilliseconds(40));
        var configurations = Enumerable.Range(1, 3)
            .Select(index => new TerminalWorkspaceTabConfiguration(
                new TerminalTabConfigurationId(CreateGuid(index)), $"탭 {index}", index - 1,
                "C:\\Work", TerminalShellKind.Automatic))
            .ToArray();
        var workspace = new TerminalWorkspaceConfiguration(1, configurations,
                                                           configurations[0].ConfigurationId);
        var startTask = Task.Run(async () => await coordinator.StartAsync(_testShell, TerminalShellKind.Automatic,
                                                                          workspace, _ => _testShell,
                                                                          80, 24, CancellationToken.None));
        await factory.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await coordinator.DisposeAsync();
        factory.ReleaseStart();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await startTask);
        Assert.AreEqual(1, factory.StartCount);
        Assert.AreEqual(1, coordinator.Snapshot.Tabs.Count);
    }

    [TestMethod]
    public async Task DebounceSerializesWritesAndPersistsLatestWorkspaceOnly()
    {
        var store = new RecordingWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(20),
                                                                       TimeSpan.FromSeconds(1));
        _ = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);

        Assert.IsTrue(coordinator.Rename(tab.SessionId, "첫 변경"));
        Assert.IsTrue(coordinator.Rename(tab.SessionId, "최신 변경"));
        await store.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, store.MaximumConcurrentSaves);
        Assert.AreEqual("최신 변경", store.SavedConfigurations.Last().Tabs[0].Name);
    }

    [TestMethod]
    public async Task LoadedWorkspaceIsNotReplacedByPartialSnapshotsDuringRestore()
    {
        var firstId = new TerminalTabConfigurationId(CreateGuid(1));
        var secondId = new TerminalTabConfigurationId(CreateGuid(2));
        var configuration = new TerminalWorkspaceConfiguration(1,
                                                               [
                                                                   new TerminalWorkspaceTabConfiguration(firstId, "첫 탭", 0,
                                                                                                         "C:\\Work", TerminalShellKind.Automatic),
                                                                   new TerminalWorkspaceTabConfiguration(secondId, "둘째 탭", 1,
                                                                                                         "C:\\Work", TerminalShellKind.Automatic),
                                                               ],
                                                               secondId);
        var store = new RecordingWorkspaceStore
        {
            LoadResult = new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.Loaded, configuration, null),
        };
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5),
                                                                       TimeSpan.FromSeconds(1));
        var load = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);

        _ = await coordinator.StartAsync(_testShell, TerminalShellKind.Automatic, load.Configuration,
                                         _ => _testShell, 80, 24, CancellationToken.None);
        await Task.Delay(30);
        persistence.CompleteRestore();

        Assert.AreEqual(0, store.SavedConfigurations.Count);
        Assert.IsTrue(coordinator.Rename(coordinator.Snapshot.ActiveSessionId!.Value, "복원 완료"));
        await store.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(2, store.SavedConfigurations[0].Tabs.Count);
    }

    [TestMethod]
    public async Task RuntimeStateChangesDoNotRewriteUnchangedWorkspaceConfiguration()
    {
        var store = new RecordingWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5),
                                                                       TimeSpan.FromSeconds(1));
        _ = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        await store.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var saveCount = store.SavedConfigurations.Count;

        await coordinator.RestartAsync(tab.SessionId, CancellationToken.None);
        await Task.Delay(30);

        Assert.AreEqual(saveCount, store.SavedConfigurations.Count);
    }

    [TestMethod]
    public async Task ShutdownFlushPersistsLatestChangeWithoutWaitingForDebounce()
    {
        var store = new RecordingWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                           TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        _ = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        Assert.IsTrue(coordinator.Rename(tab.SessionId, "종료 직전"));

        await persistence.DisposeAsync();

        Assert.AreEqual("종료 직전", store.SavedConfigurations.Last().Tabs[0].Name);
    }

    [TestMethod]
    public async Task FutureSchemaSuspendsRestoreAndAllAutomaticWrites()
    {
        var store = new RecordingWorkspaceStore
        {
            LoadResult = new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.FutureSchema, null,
                                                         "future schema"),
        };
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5),
                                                                       TimeSpan.FromMilliseconds(100));
        var load = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        _ = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        await Task.Delay(30);
        var enableResult = await persistence.SetEnabledAsync(enabled: true, CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.FutureSchema, load.Status);
        Assert.AreEqual(TerminalWorkspacePersistenceStatus.Skipped, enableResult.Status);
        Assert.AreEqual(0, store.SavedConfigurations.Count);
    }

    [TestMethod]
    public async Task EnablingAfterDisabledStartupInspectsAndPreservesFutureSchema()
    {
        var store = new RecordingWorkspaceStore
        {
            LoadResult = new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.FutureSchema, null,
                                                         "future schema"),
        };
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5),
                                                                       TimeSpan.FromMilliseconds(100));
        _ = await persistence.InitializeAsync(restoreEnabled: false, CancellationToken.None);
        _ = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);

        var enableResult = await persistence.SetEnabledAsync(enabled: true, CancellationToken.None);
        Assert.IsTrue(coordinator.Rename(coordinator.Snapshot.ActiveSessionId!.Value, "저장 중단"));
        await Task.Delay(30);

        Assert.AreEqual(TerminalWorkspacePersistenceStatus.Skipped, enableResult.Status);
        Assert.AreEqual(1, store.LoadCount);
        Assert.AreEqual(0, store.SavedConfigurations.Count);
    }

    [TestMethod]
    public async Task CancelledEnableLeavesAutomaticPersistenceDisabled()
    {
        var store = new RecordingWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5),
                                                                       TimeSpan.FromMilliseconds(100));
        _ = await persistence.InitializeAsync(restoreEnabled: false, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            persistence.SetEnabledAsync(enabled: true, cancellation.Token));
        Assert.IsTrue(coordinator.Rename(tab.SessionId, "저장 안 함"));
        await Task.Delay(30);

        Assert.AreEqual(0, store.SavedConfigurations.Count);
    }

    [TestMethod]
    public async Task FutureSchemaDetectedByWriterSuspendsLaterAutomaticWrites()
    {
        var store = new FutureOnSaveWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5),
                                                                       TimeSpan.FromMilliseconds(100));
        _ = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        await store.SaveAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(coordinator.Rename(tab.SessionId, "더 이상 저장 안 함"));
        await Task.Delay(30);

        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task DisableDeletesPrimaryBackupAndStopsLaterWrites()
    {
        var store = new RecordingWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(10),
                                                                       TimeSpan.FromSeconds(1));
        _ = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        var result = await persistence.SetEnabledAsync(enabled: false, CancellationToken.None);
        var saveCount = store.SavedConfigurations.Count;
        Assert.IsTrue(coordinator.Rename(tab.SessionId, "저장 안 함"));
        await Task.Delay(30);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, store.DeleteCount);
        Assert.AreEqual(saveCount, store.SavedConfigurations.Count);
    }

    [TestMethod]
    public async Task ShutdownTimeoutCancelsWriterAndIgnoresLaterWorkspaceCallbacks()
    {
        var store = new BlockingWorkspaceStore();
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                           TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(40));
        _ = await persistence.InitializeAsync(restoreEnabled: true, CancellationToken.None);
        var tab = await coordinator.StartAsync(_testShell, 80, 24, CancellationToken.None);
        Assert.IsTrue(coordinator.Rename(tab.SessionId, "종료 직전"));

        var stopwatch = Stopwatch.StartNew();
        await persistence.DisposeAsync();
        stopwatch.Stop();
        Assert.IsTrue(coordinator.Rename(tab.SessionId, "종료 이후"));
        await Task.Delay(30);

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, store.SaveCount);
        Assert.IsTrue(store.SaveCancellationObserved);
    }

    private static Guid CreateGuid(int value)
    {
        return Guid.Parse($"10000000-0000-0000-0000-{value:D12}");
    }

    private sealed class RecordingWorkspaceStore : ITerminalWorkspaceStore
    {
        private int concurrentSaves;

        internal TerminalWorkspaceLoadResult LoadResult { get; init; } =
            new(TerminalWorkspaceLoadStatus.NotFound, null, null);

        internal List<TerminalWorkspaceConfiguration> SavedConfigurations { get; } = [];

        internal TaskCompletionSource FirstSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int DeleteCount { get; private set; }

        internal int LoadCount { get; private set; }

        internal int MaximumConcurrentSaves { get; private set; }

        public Task<TerminalWorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(LoadResult);
        }

        public async Task SaveAsync(TerminalWorkspaceConfiguration configuration,
                                    CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref concurrentSaves);
            MaximumConcurrentSaves = Math.Max(MaximumConcurrentSaves, current);
            try
            {
                await Task.Delay(5, cancellationToken);
                SavedConfigurations.Add(configuration);
                FirstSave.TrySetResult();
            }
            finally
            {
                _ = Interlocked.Decrement(ref concurrentSaves);
            }
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingWorkspaceStore : ITerminalWorkspaceStore
    {
        internal int SaveCount { get; private set; }

        internal bool SaveCancellationObserved { get; private set; }

        public Task<TerminalWorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.NotFound,
                                                                   null, null));
        }

        public async Task SaveAsync(TerminalWorkspaceConfiguration configuration,
                                    CancellationToken cancellationToken)
        {
            _ = configuration;
            SaveCount++;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                SaveCancellationObserved = true;
                throw;
            }
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FutureOnSaveWorkspaceStore : ITerminalWorkspaceStore
    {
        internal TaskCompletionSource SaveAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int SaveCount { get; private set; }

        public Task<TerminalWorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.NotFound,
                                                                   null, null));
        }

        public Task SaveAsync(TerminalWorkspaceConfiguration configuration,
                              CancellationToken cancellationToken)
        {
            _ = configuration;
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            SaveAttempted.TrySetResult();
            throw new TerminalWorkspaceFutureSchemaException();
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSessionFactory : ITerminalSessionFactory
    {
        internal List<ShellLaunchSpec> StartRequests { get; } = [];

        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
        {
            _ = columns;
            _ = rows;
            StartRequests.Add(shell);
            return new RecordingSession();
        }
    }

    private sealed class BlockingStartSessionFactory : ITerminalSessionFactory, IDisposable
    {
        private readonly ManualResetEventSlim startGate = new(initialState: false);

        internal TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int StartCount { get; private set; }

        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
        {
            _ = shell;
            _ = columns;
            _ = rows;
            StartCount++;
            StartEntered.TrySetResult();
            startGate.Wait(TimeSpan.FromSeconds(5));
            return new RecordingSession();
        }

        internal void ReleaseStart()
        {
            startGate.Set();
        }

        public void Dispose()
        {
            startGate.Set();
            startGate.Dispose();
        }
    }

    private sealed class RecordingSession : ITerminalSession
    {
        public event Action<string>? OutputReceived;

        public event Action<uint>? Exited;

        public event Action<TerminalSessionCommandSignal>? CommandLifecycleChanged
        {
            add { }
            remove { }
        }

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
            return ValueTask.CompletedTask;
        }

        internal void KeepEventsReferenced()
        {
            _ = OutputReceived;
            _ = Exited;
        }
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
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
