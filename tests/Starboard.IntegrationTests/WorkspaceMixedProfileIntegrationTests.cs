using System.IO;
using System.Text;
using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.Windows.Infrastructure;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class WorkspaceMixedProfileIntegrationTests
{
    private static readonly string[] _workspacePropertyNames = ["schemaVersion", "tabs", "activeTabConfigurationId"];
    private static readonly string[] _tabPropertyNames = ["configurationId", "name", "order", "startingDirectory", "launchProfileId"];
    private static readonly string[] _wslArguments = ["--distribution", "Ubuntu", "--cd", "~"];

    [TestMethod]
    public async Task MixedWorkspaceFlushReloadCreatesIndependentSessionsAndPreservesRestoreFiles()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var log = new FileDiagnosticLog(Path.Combine(directory, "logs"));
            var store = new FileTerminalWorkspaceStore(path);
            var runner = new FakeWslRunner { HoldDiscovery = true };
            var catalog = CreateCatalog(runner, log);
            var sourceFactory = new FakeFactory();
            await using var source = new TerminalSessionCoordinator(sourceFactory, log, launchProfileCatalog: catalog);
            await using (var persistence = new TerminalWorkspacePersistence(store, source, log, TimeSpan.FromMinutes(1)))
            {
                _ = await persistence.InitializeAsync(true, CancellationToken.None);
                var shell = new ShellLaunchSpec("pwsh.exe", [], directory);
                var first = await source.StartAsync(shell, TerminalShellKind.Pwsh, null, _ => shell,
                                                    80, 24, CancellationToken.None);
                source.UpdateDefaultShell(new ShellLaunchSpec("cmd.exe", [], directory), TerminalShellKind.Cmd);
                var second = await source.AddAsync(CancellationToken.None);
                var save = await persistence.SetEnabledAsync(true, CancellationToken.None);
                Assert.IsTrue(save.Succeeded);
                var profile = TerminalLaunchProfile.CreateWsl("Ubuntu");
                var request = new TerminalNewTabRequest(TerminalTabRequestId.CreateNew(),
                                                        source.RendererWorkspace.Order[1], profile.ProfileId);
                var launched = await source.LaunchProfileAsync(request, profile, catalog.Resolve, CancellationToken.None);
                Assert.AreEqual(TerminalTabLaunchStatus.Started, launched.Status);
                var ubuntu = launched.Tab!;
                Assert.IsTrue(source.Rename(first.SessionId, "Windows 개발"));
                Assert.IsTrue(source.Rename(second.SessionId, "명령 프롬프트"));
                Assert.IsTrue(source.Rename(ubuntu.SessionId, "Ubuntu 작업"));
                Assert.IsTrue(source.MoveLeft(ubuntu.SessionId));
                Assert.IsTrue(source.Select(first.SessionId));
                var beforeFlush = await store.LoadAsync(CancellationToken.None);
                Assert.HasCount(2, beforeFlush.Configuration!.Tabs);
                await sourceFactory.Sessions[0].WriteAsync("private-command", CancellationToken.None);
                sourceFactory.Sessions[0].EmitOutput("private-output");
            }

            // The latest mixed changes were still debounced when disposal flushed them.
            var loaded = await store.LoadAsync(CancellationToken.None);
            var expected = loaded.Configuration!;
            Assert.AreEqual(2, expected.SchemaVersion);
            Assert.HasCount(3, expected.Tabs);
            AssertSavedShape(path);
            var primary = await File.ReadAllTextAsync(path);
            var backup = await File.ReadAllTextAsync(path + ".bak");
            Assert.IsFalse(primary.Contains("private-", StringComparison.Ordinal));
            var restoredFactory = new FakeFactory();
            await using var restored = new TerminalSessionCoordinator(restoredFactory, log, launchProfileCatalog: catalog);
            await using (var persistence = new TerminalWorkspacePersistence(store, restored, log, TimeSpan.FromMilliseconds(1)))
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var startup = new TerminalWorkspaceStartup(persistence, restored, log);
                var shell = new ShellLaunchSpec("default.exe", [], directory);
                var start = startup.StartAsync(true, shell, TerminalShellKind.Automatic,
                                              kind => new ShellLaunchSpec($"{kind}.exe", [], directory),
                                              token => ready.Task.WaitAsync(token), 80, 24, CancellationToken.None);
                Assert.IsEmpty(restored.Snapshot.Tabs);
                Assert.AreEqual(0, runner.CallCount);
                ready.SetResult();
                await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.HasCount(1, restored.Snapshot.Tabs);
                await Task.Delay(30);
                await AssertStoredFilesAsync(path, primary, backup);
                runner.Release.TrySetResult();
                await start.WaitAsync(TimeSpan.FromSeconds(5));
                var snapshot = restored.Snapshot;
                CollectionAssert.AreEqual(expected.Tabs.Select(tab => tab.ConfigurationId).ToArray(),
                                          snapshot.Tabs.Select(tab => tab.ConfigurationId).ToArray());
                CollectionAssert.AreEqual(expected.Tabs.Select(tab => tab.Name).ToArray(),
                                          snapshot.Tabs.Select(tab => tab.Name).ToArray());
                CollectionAssert.AreEqual(expected.Tabs.Select(tab => tab.LaunchProfileId).ToArray(),
                                          snapshot.Tabs.Select(tab => tab.LaunchProfile!.ProfileId).ToArray());
                Assert.AreEqual(snapshot.Tabs[0].SessionId, snapshot.ActiveSessionId);
                Assert.IsTrue(snapshot.Tabs.All(tab => tab.State == TerminalSessionState.Running));
                Assert.HasCount(3, restoredFactory.Sessions);
                Assert.AreEqual(3, restoredFactory.Sessions.Distinct().Count());
                Assert.IsFalse(snapshot.Tabs.Any(tab => source.Snapshot.Tabs.Any(old => old.SessionId == tab.SessionId)));
                Assert.IsFalse(restoredFactory.Sessions.Any(session => sourceFactory.Sessions.Contains(session)));
                Assert.AreEqual(1, runner.CallCount);
                var wsl = restoredFactory.Requests[1];
                CollectionAssert.AreEqual(_wslArguments, wsl.Arguments.ToArray());
                Assert.AreEqual(catalog.Resolve(TerminalLaunchProfile.CreateWsl("Ubuntu")).WorkingDirectory,
                                wsl.WorkingDirectory);
                Assert.AreNotEqual(directory, wsl.WorkingDirectory);
                Assert.AreEqual(directory, restoredFactory.Requests[0].WorkingDirectory);
                Assert.AreEqual(directory, restoredFactory.Requests[2].WorkingDirectory);
            }

            await AssertStoredFilesAsync(path, primary, backup);
            var diagnostics = await File.ReadAllTextAsync(Path.Combine(directory, "logs", "starboard.log"));
            Assert.IsFalse(diagnostics.Contains("private-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("timeout")]
    [DataRow("unavailable")]
    [DataRow("launch")]
    [DataRow("none")]
    public async Task WslRestoreIsolatesFailuresAndPreservesPrimaryAndBackup(string failure)
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var expected = CreateMixedConfiguration(directory);
            await store.SaveAsync(expected with { ActiveTabConfigurationId = expected.Tabs[0].ConfigurationId }, CancellationToken.None);
            await store.SaveAsync(expected, CancellationToken.None);
            var primary = await File.ReadAllTextAsync(path);
            var backup = await File.ReadAllTextAsync(path + ".bak");
            var log = new FileDiagnosticLog(Path.Combine(directory, "logs"));
            var runner = new FakeWslRunner { Failure = failure };
            var catalog = CreateCatalog(runner, log, failure == "unavailable");
            var factory = new FakeFactory { FailWsl = failure == "launch" };
            await using var sessions = new TerminalSessionCoordinator(factory, log, launchProfileCatalog: catalog);
            await using (var persistence = new TerminalWorkspacePersistence(store, sessions, log, TimeSpan.FromMilliseconds(1)))
            {
                var startup = new TerminalWorkspaceStartup(persistence, sessions, log);
                var shell = new ShellLaunchSpec("fake.exe", [], directory);
                await startup.StartAsync(true, shell, TerminalShellKind.Automatic, _ => shell,
                                         _ => Task.CompletedTask, 80, 24, CancellationToken.None);
                Assert.HasCount(3, sessions.Snapshot.Tabs);
                Assert.AreEqual(TerminalSessionState.Running, sessions.Snapshot.Tabs[0].State);
                Assert.AreEqual(failure == "none" ? TerminalSessionState.Running : TerminalSessionState.Failed,
                                sessions.Snapshot.Tabs[1].State);
                Assert.AreEqual(TerminalSessionState.Running, sessions.Snapshot.Tabs[2].State);
                Assert.AreEqual(sessions.Snapshot.Tabs[1].SessionId, sessions.Snapshot.ActiveSessionId);
                Assert.AreEqual(expected.Tabs[1].LaunchProfileId, sessions.Snapshot.Tabs[1].LaunchProfile!.ProfileId);
                Assert.IsNull(sessions.Snapshot.Tabs[1].ShellKind);
                Assert.HasCount(failure == "none" ? 3 : 2, factory.Sessions);
                Assert.AreEqual(failure == "unavailable" ? 0 : 1, runner.CallCount);
            }

            await AssertStoredFilesAsync(path, primary, backup);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UnsupportedProfileRejectsWholeSnapshotAndCancelsPendingFlushInRealStore()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var log = new FileDiagnosticLog(Path.Combine(directory, "logs"));
            await using var sessions = new TerminalSessionCoordinator(new FakeFactory(), log);
            await using (var persistence = new TerminalWorkspacePersistence(store, sessions, log, TimeSpan.FromMinutes(1)))
            {
                _ = await persistence.InitializeAsync(true, CancellationToken.None);
                _ = await sessions.StartAsync(new ShellLaunchSpec("fake.exe", [], directory), 80, 24, CancellationToken.None);
                var save = await persistence.SetEnabledAsync(true, CancellationToken.None);
                Assert.IsTrue(save.Succeeded);
                save = await persistence.SetEnabledAsync(true, CancellationToken.None);
                Assert.IsTrue(save.Succeeded);
                var primary = await File.ReadAllTextAsync(path);
                var backup = await File.ReadAllTextAsync(path + ".bak");
                _ = await sessions.AddAsync(CancellationToken.None);
                sessions.UpdateDefaultShell(new ShellLaunchSpec("custom.exe", [], directory), shellKind: null);
                _ = await sessions.AddAsync(CancellationToken.None);
                var result = await persistence.SetEnabledAsync(true, CancellationToken.None);
                Assert.AreEqual(TerminalWorkspacePersistenceStatus.Failed, result.Status);
                await persistence.DisposeAsync();
                await AssertStoredFilesAsync(path, primary, backup);
                Assert.HasCount(3, sessions.Snapshot.Tabs);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacySchemaMigratesToMixedSchemaAndFutureSchemaPreservesBothFiles()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var first = CreateMixedConfiguration(directory).Tabs[0];
            var legacy = new TerminalWorkspaceConfiguration(1, [first], first.ConfigurationId);
            await store.SaveAsync(legacy, CancellationToken.None);
            var legacyJson = await File.ReadAllTextAsync(path);
            var log = new FileDiagnosticLog(Path.Combine(directory, "logs"));
            await using (var sessions = new TerminalSessionCoordinator(new FakeFactory(), log))
            {
                await using var persistence = new TerminalWorkspacePersistence(store, sessions, log, TimeSpan.FromMinutes(1));
                var startup = new TerminalWorkspaceStartup(persistence, sessions, log);
                var shell = new ShellLaunchSpec("fake.exe", [], directory);
                await startup.StartAsync(true, shell, TerminalShellKind.Automatic, _ => shell,
                                         _ => Task.CompletedTask, 80, 24, CancellationToken.None);
                Assert.IsTrue(sessions.Rename(sessions.Snapshot.Tabs[0].SessionId, "마이그레이션"));
            }

            var migrated = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(2, migrated.Configuration!.SchemaVersion);
            var migratedBackup = await File.ReadAllTextAsync(path + ".bak");
            Assert.AreEqual(legacyJson, migratedBackup);
            const string future = "{\"schemaVersion\":999,\"private-future\":true}";
            await File.WriteAllTextAsync(path, future);
            await using (var sessions = new TerminalSessionCoordinator(new FakeFactory(), log))
            {
                await using var persistence = new TerminalWorkspacePersistence(store, sessions, log, TimeSpan.FromMilliseconds(1));
                var startup = new TerminalWorkspaceStartup(persistence, sessions, log);
                var shell = new ShellLaunchSpec("fake.exe", [], directory);
                await startup.StartAsync(true, shell, TerminalShellKind.Automatic, _ => shell,
                                         _ => Task.CompletedTask, 80, 24, CancellationToken.None);
                Assert.IsTrue(sessions.Rename(sessions.Snapshot.Tabs[0].SessionId, "저장 중단"));
                var save = await persistence.SetEnabledAsync(true, CancellationToken.None);
                Assert.AreEqual(TerminalWorkspacePersistenceStatus.Skipped, save.Status);
            }

            await AssertStoredFilesAsync(path, future, legacyJson);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Starboard-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task AssertStoredFilesAsync(string path, string primary, string backup)
    {
        var currentPrimary = await File.ReadAllTextAsync(path);
        var currentBackup = await File.ReadAllTextAsync(path + ".bak");
        Assert.AreEqual(primary, currentPrimary);
        Assert.AreEqual(backup, currentBackup);
    }

    private static TerminalWorkspaceConfiguration CreateMixedConfiguration(string directory)
    {
        var first = new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "Windows", 0,
                                                          directory, TerminalShellKind.Pwsh);
        // Nonexistent Windows metadata must not prevent WSL's Linux-home start.
        var wsl = new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "Ubuntu", 1,
                                                        Path.Combine(directory, "missing"), TerminalLaunchProfileId.FromWslDistribution("Ubuntu"));
        var last = new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "CMD", 2,
                                                         directory, TerminalShellKind.Cmd);
        return new TerminalWorkspaceConfiguration(2, [first, wsl, last], wsl.ConfigurationId);
    }

    private static TerminalLaunchProfileCatalog CreateCatalog(FakeWslRunner runner, FileDiagnosticLog log,
                                                              bool unavailable = false)
    {
        return new TerminalLaunchProfileCatalog(runner, log, executable =>
            unavailable == true && executable == "wsl.exe" ? null : executable);
    }

    private static void AssertSavedShape(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        CollectionAssert.AreEquivalent(_workspacePropertyNames,
                                      document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        foreach (var tab in document.RootElement.GetProperty("tabs").EnumerateArray())
        {
            CollectionAssert.AreEquivalent(_tabPropertyNames,
                                          tab.EnumerateObject().Select(property => property.Name).ToArray());
        }
    }

    private sealed class FakeWslRunner : IWslProcessRunner
    {
        internal string? Failure { get; init; }
        internal int CallCount { get; private set; }
        internal bool HoldDiscovery { get; init; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WslProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments,
                                                     TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Entered.TrySetResult();
            if (HoldDiscovery == true)
            {
                await Release.Task.WaitAsync(cancellationToken);
            }

            if (Failure == "timeout")
            {
                throw new TimeoutException("simulated discovery timeout");
            }

            return new WslProcessResult(0, Encoding.Unicode.GetBytes(Failure == "missing" ? "Debian\r\n" : "Ubuntu\r\n"));
        }
    }

    private sealed class FakeFactory : ITerminalSessionFactory
    {
        internal List<ShellLaunchSpec> Requests { get; } = [];
        internal List<FakeSession> Sessions { get; } = [];
        internal bool FailWsl { get; init; }

        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
        {
            Requests.Add(shell);
            if (FailWsl == true && shell.ExecutablePath == "wsl.exe")
            {
                throw new IOException("simulated WSL launch failure");
            }

            var session = new FakeSession();
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class FakeSession : ITerminalSession
    {
        public event Action<string>? OutputReceived;
        public event Action<uint>? Exited { add { } remove { } }
        public event Action<TerminalSessionCommandSignal>? CommandLifecycleChanged { add { } remove { } }
        public void SetOutputPaused(bool paused) { }

        public void BeginReading() { }
        public ValueTask WriteAsync(string data, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Resize(int columns, int rows) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        internal void EmitOutput(string data) => OutputReceived?.Invoke(data);
    }
}
