using System.IO;
using System.Text.Json;
using Starboard.Modules.Preferences;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.Windows.Composition;
using Starboard.Windows.Infrastructure;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class WorkspaceStartupIntegrationTests
{
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public async Task StartupWithDelayedRendererAndShellFailurePreservesWorkspace(bool enabled, bool failMiddle)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Starboard-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var log = new FileDiagnosticLog(Path.Combine(directory, "logs"));
            var preferences = new PreferencesModule(log, Path.Combine(directory, "settings.json"));
            await preferences.SaveAsync(new AppSettings { RestoreWorkspaceOnLaunch = enabled }, CancellationToken.None);
            var loadedSettings = await preferences.LoadAsync(CancellationToken.None);
            var options = SettingsApplicationService.ToTerminalOptions(loadedSettings.Settings);
            Assert.AreEqual(enabled, options.RestoreWorkspaceOnLaunch);
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var tabs = new[]
            {
                new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "private-first", 0, directory, TerminalShellKind.Pwsh),
                new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "private-middle", 1, directory, TerminalShellKind.Cmd),
                new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "private-last", 2, directory, TerminalShellKind.PowerShell),
            };
            var configuration = new TerminalWorkspaceConfiguration(1, tabs, tabs[2].ConfigurationId);
            await store.SaveAsync(configuration, CancellationToken.None);
            await store.SaveAsync(configuration, CancellationToken.None);
            var primary = await File.ReadAllTextAsync(path);
            var backup = await File.ReadAllTextAsync(path + ".bak");
            var factory = new FakeFactory();
            await using var sessions = new TerminalSessionCoordinator(factory, log);
            var persistence = new TerminalWorkspacePersistence(store, sessions, log);
            await using (persistence)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var startup = new TerminalWorkspaceStartup(persistence, sessions, log);
                var shell = new ShellLaunchSpec("fake.exe", [], directory);
                var start = startup.StartAsync(options.RestoreWorkspaceOnLaunch, shell, TerminalShellKind.Automatic,
                                              kind => failMiddle == true && kind == TerminalShellKind.Cmd
                                                  ? throw new FileNotFoundException("private-shell-command")
                                                  : shell,
                                              token => ready.Task.WaitAsync(token), 80, 24, CancellationToken.None);
                Assert.IsFalse(start.IsCompleted);
                Assert.IsEmpty(sessions.Snapshot.Tabs);
                Assert.AreEqual(primary, await File.ReadAllTextAsync(path));
                ready.SetResult();
                await start.WaitAsync(TimeSpan.FromSeconds(5));
                var snapshot = sessions.Snapshot;
                Assert.HasCount(enabled ? 3 : 1, snapshot.Tabs);
                if (enabled == true)
                {
                    CollectionAssert.AreEqual(tabs.Select(tab => tab.Name).ToArray(), snapshot.Tabs.Select(tab => tab.Name).ToArray());
                    CollectionAssert.AreEqual(tabs.Select(tab => tab.StartingDirectory).ToArray(), snapshot.Tabs.Select(tab => tab.StartingDirectory).ToArray());
                    Assert.AreEqual(snapshot.Tabs[2].SessionId, snapshot.ActiveSessionId);
                    Assert.AreEqual(failMiddle ? TerminalSessionState.Failed : TerminalSessionState.Running, snapshot.Tabs[1].State);
                    Assert.AreEqual(TerminalSessionState.Running, snapshot.Tabs[0].State);
                    Assert.AreEqual(TerminalSessionState.Running, snapshot.Tabs[2].State);
                }

                // The same production snapshot/protocol is used when a renderer is ready or recreated.
                foreach (var type in new[] { "workspace-sync-begin", "workspace-sync-complete" })
                {
                    using var message = JsonDocument.Parse(RendererProtocol.SerializeWorkspaceSyncMessage(type, sessions.RendererWorkspace));
                    var payload = message.RootElement.GetProperty("payload");
                    Assert.AreEqual(snapshot.Tabs.Count, payload.GetProperty("order").GetArrayLength());
                    Assert.AreEqual(snapshot.ActiveSessionId!.Value.ToString(), payload.GetProperty("activeSessionId").GetString());
                    CollectionAssert.AreEqual(snapshot.Tabs.Select(tab => tab.SessionId.ToString()).ToArray(),
                                               payload.GetProperty("order").EnumerateArray().Select(item => item.GetProperty("sessionId").GetString()).ToArray());
                }
            }

            Assert.AreEqual(primary, await File.ReadAllTextAsync(path));
            Assert.AreEqual(backup, await File.ReadAllTextAsync(path + ".bak"));
            var diagnostics = await File.ReadAllTextAsync(Path.Combine(directory, "logs", "starboard.log"));
            StringAssert.Contains(diagnostics, "Startup.RendererWaiting");
            StringAssert.Contains(diagnostics, "Startup.RendererReady");
            StringAssert.Contains(diagnostics, $"Startup.Restored.N{(enabled ? 3 : 1)}.F{(enabled && failMiddle ? 1 : 0)}");
            Assert.IsFalse(diagnostics.Contains("private-", StringComparison.Ordinal));
            Assert.IsFalse(diagnostics.Contains(directory, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeFactory : ITerminalSessionFactory
    {
        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows) => new FakeSession();
    }

    private sealed class FakeSession : ITerminalSession
    {
        public event Action<string>? OutputReceived { add { } remove { } }
        public event Action<uint>? Exited { add { } remove { } }
        public event Action<TerminalSessionCommandSignal>? CommandLifecycleChanged { add { } remove { } }
        public void SetOutputPaused(bool paused) { }

        public void BeginReading() { }
        public ValueTask WriteAsync(string data, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Resize(int columns, int rows) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
