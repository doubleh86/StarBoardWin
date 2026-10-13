using System.IO;
using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalTabReorderTests
{
    private static readonly ShellLaunchSpec _shell = new("pwsh.exe", ["-NoLogo"], Path.GetTempPath());

    [TestMethod]
    public async Task ReorderSharedCasesPreserveSessionsAndPersistOnlyAppliedOptInOrder()
    {
        using var fixture = ReadFixture();
        var root = fixture.RootElement;
        foreach (var testCase in root.GetProperty("cases").EnumerateArray())
        {
            foreach (var mode in root.GetProperty("persistenceModes").EnumerateArray())
            {
                var label = testCase.GetProperty("id").GetString() + "/restore=" + mode.GetBoolean();
                var state = root.GetProperty("states").GetProperty(testCase.GetProperty("hostState").GetString()!);
                var ids = state.GetProperty("sessions").EnumerateArray().Select(ReadId).ToArray();
                var identifiers = new Queue<TerminalSessionId>(ids);
                var factory = new RecordingSessionFactory();
                await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog(),
                                                                             () => identifiers.Dequeue());
                var currentSessions = new Dictionary<TerminalSessionId, RecordingSession>();
                foreach (var entry in state.GetProperty("sessions").EnumerateArray())
                {
                    var id = ReadId(entry);
                    if (currentSessions.Count == 0)
                    {
                        _ = await coordinator.StartAsync(_shell, 80, 24, CancellationToken.None);
                    }
                    else
                    {
                        _ = await coordinator.AddAsync(CancellationToken.None);
                    }

                    for (var generation = 1; generation < entry.GetProperty("sessionGeneration").GetInt64(); generation++)
                    {
                        await coordinator.RestartAsync(id, CancellationToken.None);
                    }

                    currentSessions[id] = factory.Sessions.Last();
                }

                Assert.IsTrue(coordinator.Select(ReadId(state, "activeSessionId")), label);
                var before = coordinator.RendererWorkspace;
                var beforeSessions = factory.Sessions.ToArray();
                var beforeResizes = beforeSessions.Select(session => session.ResizeCount).ToArray();
                var beforeDisposals = beforeSessions.Select(session => session.DisposeCount).ToArray();
                var receivedOutput = new List<TerminalSessionOutput>();
                coordinator.OutputReceived += receivedOutput.Add;
                var eventCount = 0;
                coordinator.WorkspaceChanged += _ => eventCount++;
                var store = new RecordingWorkspaceStore();
                await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                               TimeSpan.FromMilliseconds(5));
                _ = await persistence.InitializeAsync(mode.GetBoolean(), CancellationToken.None);
                var expected = testCase.GetProperty("expected");
                var wire = testCase.GetProperty("message");
                if (wire.ValueKind != JsonValueKind.Null)
                {
                    var parsed = RendererProtocol.TryParse(wire.GetRawText(), out var message, out var reorderEnvelope);
                    Assert.AreEqual(expected.GetProperty("parseAccepted").GetBoolean(), parsed, label);
                    var instanceId = state.GetProperty("rendererInstanceId").GetString();
                    var context = new RendererReorderContext(state.GetProperty("rendererReady").GetBoolean(), 7,
                                                             instanceId is null ? Guid.Empty : Guid.ParseExact(instanceId, "N"));
                    var resync = reorderEnvelope == true && context.IsAvailable == true ? "current" : "none";
                    if (parsed == true)
                    {
                        Assert.IsNotNull(message?.ReorderRequest, label);
                        var request = message.ReorderRequest;
                        string reason;
                        if (context.CanApply(request, 7) == false)
                        {
                            reason = context.IsAvailable == false ? "renderer-unavailable" : "stale-renderer";
                        }
                        else
                        {
                            reason = FormatReason(coordinator.Reorder(request));
                        }

                        Assert.AreEqual(expected.GetProperty("reason").GetString(), reason, label);
                    }

                    Assert.AreEqual(expected.GetProperty("resync").GetString(), resync, label);
                }

                var after = coordinator.RendererWorkspace;
                CollectionAssert.AreEqual(expected.GetProperty("order").EnumerateArray().Select(value => value.GetString()).ToArray(),
                                          after.Workspace.Tabs.Select(tab => tab.SessionId.ToString()).ToArray(), label);
                Assert.AreEqual(expected.GetProperty("workspaceChangedCount").GetInt32(), eventCount, label);
                Assert.AreEqual(before.Workspace.ActiveSessionId, after.Workspace.ActiveSessionId, label);
                CollectionAssert.AreEqual(beforeSessions, factory.Sessions.ToArray(), label);
                CollectionAssert.AreEqual(beforeResizes, beforeSessions.Select(session => session.ResizeCount).ToArray(), label);
                CollectionAssert.AreEqual(beforeDisposals, beforeSessions.Select(session => session.DisposeCount).ToArray(), label);
                foreach (var tab in before.Workspace.Tabs)
                {
                    Assert.AreSame(tab, after.Workspace.Tabs.Single(candidate => candidate.SessionId == tab.SessionId), label);
                    var lifetime = before.Order.Single(session => session.SessionId == tab.SessionId.Value);
                    Assert.IsTrue(after.Order.Contains(lifetime), label);
                    await coordinator.WriteAsync(tab.SessionId, "한글 preserved", CancellationToken.None);
                    Assert.AreEqual("한글 preserved", currentSessions[tab.SessionId].Writes.Last(), label);
                    currentSessions[tab.SessionId].RaiseOutput("preserved output");
                    Assert.AreEqual(tab.SessionId, receivedOutput.Last().SessionId, label);
                }

                if (mode.GetBoolean() == true && eventCount == 1)
                {
                    await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.IsNotNull(store.Configuration, label);
                    CollectionAssert.AreEqual(after.Workspace.Tabs.Select(tab => tab.ConfigurationId).ToArray(),
                                              store.Configuration.Tabs.Select(tab => tab.ConfigurationId).ToArray(), label);
                    CollectionAssert.AreEqual(Enumerable.Range(0, ids.Length).ToArray(),
                                              store.Configuration.Tabs.Select(tab => tab.Order).ToArray(), label);
                    Assert.AreEqual(after.Workspace.Tabs.Single(tab => tab.SessionId == after.Workspace.ActiveSessionId).ConfigurationId,
                                    store.Configuration.ActiveTabConfigurationId, label);
                    Assert.AreEqual(1, store.Configuration.SchemaVersion, label);
                }
                else
                {
                    await Task.Delay(20);
                    Assert.AreEqual(0, store.SaveCount, label);
                }
            }
        }
    }

    [TestMethod]
    public void ReorderSharedRegistryAndLegacyCasesPreserveActiveIdAndTabObjects()
    {
        using var fixture = ReadFixture();
        var root = fixture.RootElement;
        foreach (var testCase in root.GetProperty("cases").EnumerateArray())
        {
            var wire = testCase.GetProperty("message");
            if (wire.ValueKind == JsonValueKind.Null || RendererProtocol.TryParse(wire.GetRawText(), out var message) == false)
            {
                continue;
            }

            var reason = testCase.GetProperty("expected").GetProperty("reason").GetString();
            if (reason != "moved" && reason != "same-position" && reason != "missing-session" &&
                reason != "missing-target" && reason != "self-target")
            {
                continue;
            }

            var registry = CreateRegistry(root.GetProperty("states").GetProperty(testCase.GetProperty("hostState").GetString()!));
            var before = registry.CreateSnapshot();
            Assert.IsNotNull(message?.ReorderRequest);
            var request = message.ReorderRequest;
            var result = registry.Reorder(new TerminalSessionId(request.Session.SessionId), request.TargetSessionId, request.Position);
            Assert.AreEqual(reason, FormatReason(result), testCase.GetProperty("id").GetString());
            Assert.AreEqual(before.ActiveSessionId, registry.ActiveSessionId);
            foreach (var tab in before.Tabs)
            {
                Assert.AreSame(tab, registry.GetRequired(tab.SessionId));
            }
        }

        foreach (var testCase in root.GetProperty("legacyMoves").EnumerateArray())
        {
            var registry = CreateRegistry(root.GetProperty("states").GetProperty(testCase.GetProperty("hostState").GetString()!));
            var activeId = registry.ActiveSessionId;
            Assert.IsTrue(RendererProtocol.TryParse(testCase.GetProperty("message").GetRawText(), out var message));
            Assert.IsNotNull(message?.SessionId);
            var moved = message.Data == "left" ? registry.MoveLeft(message.SessionId.Value)
                                               : registry.MoveRight(message.SessionId.Value);
            var expected = testCase.GetProperty("expected");
            Assert.AreEqual(expected.GetProperty("workspaceChangedCount").GetInt32() == 1, moved);
            CollectionAssert.AreEqual(expected.GetProperty("order").EnumerateArray().Select(value => value.GetString()).ToArray(),
                                      registry.CreateSnapshot().Tabs.Select(tab => tab.SessionId.ToString()).ToArray());
            Assert.AreEqual(activeId, registry.ActiveSessionId);
        }
    }

    [TestMethod]
    public async Task ReorderConcurrentRequestsApplyOnlyOneAndMetadataChangesDoNotInvalidateOrder()
    {
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        var first = await coordinator.StartAsync(_shell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var third = await coordinator.AddAsync(CancellationToken.None);
        var start = coordinator.RendererWorkspace;
        var instance = Guid.NewGuid();
        var firstRequest = new TerminalTabReorderRequest(instance, start.Order[0], third.SessionId,
                                                         TerminalTabInsertionPosition.After, start.Order);
        var secondRequest = new TerminalTabReorderRequest(instance, start.Order[1], third.SessionId,
                                                          TerminalTabInsertionPosition.After, start.Order);
        Assert.IsTrue(coordinator.Rename(first.SessionId, "이름 변경"));
        factory.Sessions[1].RaiseOutput("background output");
        var eventCount = 0;
        coordinator.WorkspaceChanged += _ => Interlocked.Increment(ref eventCount);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await release.Task;
            return coordinator.Reorder(firstRequest);
        });
        var secondTask = Task.Run(async () =>
        {
            await release.Task;
            return coordinator.Reorder(secondRequest);
        });
        release.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.AreEqual(1, results.Count(result => result == TerminalTabReorderResult.Moved));
        Assert.AreEqual(1, results.Count(result => result == TerminalTabReorderResult.StaleOrder));
        Assert.AreEqual(1, eventCount);
        Assert.AreEqual(start.Workspace.ActiveSessionId, coordinator.Snapshot.ActiveSessionId);
        Assert.AreEqual("이름 변경", coordinator.Snapshot.Tabs.Single(tab => tab.SessionId == first.SessionId).Name);
        Assert.AreEqual(3, factory.Sessions.Count);
        Assert.IsTrue(factory.Sessions.All(session => session.DisposeCount == 0 && session.ResizeCount == 0));
        await coordinator.WriteAsync(second.SessionId, "still second", CancellationToken.None);
        Assert.AreEqual("still second", factory.Sessions[1].Writes.Last());
    }

    [TestMethod]
    public void TryParseReorderInvalidEnvelopeDoesNotRequestSynchronization()
    {
        string[] invalidEnvelopes =
        [
            "null",
            "[]",
            "{",
            "{\"version\":\"2\",\"type\":\"reorder-session\"}",
            "{\"version\":2,\"type\":null}",
            "{\"version\":2,\"type\":\"unknown\"}",
        ];
        foreach (var json in invalidEnvelopes)
        {
            Assert.IsFalse(RendererProtocol.TryParse(json, out _, out var isReorderEnvelope));
            Assert.IsFalse(isReorderEnvelope);
        }
    }

    [TestMethod]
    public async Task ReorderSaveFailureReportsFailureAndKeepsShellUsableForRetry()
    {
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        var first = await coordinator.StartAsync(_shell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var third = await coordinator.AddAsync(CancellationToken.None);
        var store = new RecordingWorkspaceStore { FailSave = true };
        await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                       TimeSpan.FromMilliseconds(5));
        _ = await persistence.InitializeAsync(true, CancellationToken.None);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.StatusChanged += status =>
        {
            if (status.State == TerminalWorkspaceSaveState.Failed)
            {
                failed.TrySetResult();
            }
            if (status.State == TerminalWorkspaceSaveState.Saved)
            {
                saved.TrySetResult();
            }
        };

        var context = new RendererReorderContext(true, 5, Guid.NewGuid());
        var request = new TerminalTabReorderRequest(context.InstanceId, coordinator.RendererWorkspace.Order[0],
                                                    third.SessionId, TerminalTabInsertionPosition.After,
                                                    coordinator.RendererWorkspace.Order);
        Assert.IsFalse(context.CanApply(request, 4));
        Assert.IsFalse(context.CanApply(request, 5, isCurrentSender: false));
        Assert.IsFalse((context with { IsAvailable = false }).CanApply(request, 5));
        Assert.AreEqual(TerminalTabReorderResult.Moved, coordinator.Reorder(request));
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.WriteAsync(first.SessionId, "still running", CancellationToken.None);
        Assert.AreEqual("still running", factory.Sessions[0].Writes.Last());
        Assert.IsTrue(factory.Sessions.All(session => session.DisposeCount == 0));

        store.FailSave = false;
        Assert.IsTrue(coordinator.MoveLeft(first.SessionId));
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        CollectionAssert.AreEqual(new[] { second.ConfigurationId, first.ConfigurationId, third.ConfigurationId },
                                  store.Configuration!.Tabs.Select(tab => tab.ConfigurationId).ToArray());
        Assert.AreEqual(third.SessionId, coordinator.Snapshot.ActiveSessionId);
        Assert.IsTrue(coordinator.Select(first.SessionId));
        factory.Sessions[0].RaiseExit();
        Assert.IsTrue(await coordinator.CloseAsync(first.SessionId, CancellationToken.None));
        Assert.AreEqual(third.SessionId, coordinator.Snapshot.ActiveSessionId);
    }

    private static TerminalTabRegistry CreateRegistry(JsonElement state)
    {
        var ids = new Queue<TerminalSessionId>(state.GetProperty("sessions").EnumerateArray().Select(ReadId));
        var registry = new TerminalTabRegistry(() => ids.Dequeue());
        foreach (var entry in state.GetProperty("sessions").EnumerateArray())
        {
            _ = entry;
            registry.Add(Path.GetTempPath());
        }
        registry.Select(ReadId(state, "activeSessionId"));

        return registry;
    }

    private static TerminalSessionId ReadId(JsonElement entry)
    {
        return ReadId(entry, "sessionId");
    }

    private static TerminalSessionId ReadId(JsonElement entry, string property)
    {
        return new TerminalSessionId(Guid.ParseExact(entry.GetProperty(property).GetString()!, "N"));
    }

    private static string FormatReason(TerminalTabReorderResult result)
    {
        return result switch
        {
            TerminalTabReorderResult.Moved => "moved",
            TerminalTabReorderResult.SamePosition => "same-position",
            TerminalTabReorderResult.MissingSession => "missing-session",
            TerminalTabReorderResult.StaleGeneration => "stale-generation",
            TerminalTabReorderResult.MissingTarget => "missing-target",
            TerminalTabReorderResult.SelfTarget => "self-target",
            TerminalTabReorderResult.StaleOrder => "stale-order",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static JsonDocument ReadFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Starboard.Windows.sln")) == true)
            {
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "tests",
                                                                        "Starboard.Modules.Terminal.Tests", "Fixtures",
                                                                        "tab-drag-reorder", "cases.json")));
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the solution root.");
    }

    private sealed class RecordingSessionFactory : ITerminalSessionFactory
    {
        internal List<RecordingSession> Sessions { get; } = [];

        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
        {
            var session = new RecordingSession(shell);
            Sessions.Add(session);

            return session;
        }
    }

    private sealed class RecordingSession(ShellLaunchSpec shell) : ITerminalSession
    {
        public event Action<string>? OutputReceived;
        public event Action<uint>? Exited;
        public event Action<TerminalSessionCommandSignal>? CommandLifecycleChanged
        {
            add { }
            remove { }
        }

        internal ShellLaunchSpec Shell { get; } = shell;
        internal List<string> Writes { get; } = [];
        internal int ResizeCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public void BeginReading() { }
        public ValueTask WriteAsync(string data, CancellationToken cancellationToken)
        {
            Writes.Add(data);
            return ValueTask.CompletedTask;
        }
        public void Resize(int columns, int rows) { ResizeCount++; }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
        internal void RaiseOutput(string data) { OutputReceived?.Invoke(data); }
        internal void RaiseExit() { Exited?.Invoke(0); }
    }

    private sealed class RecordingWorkspaceStore : ITerminalWorkspaceStore
    {
        internal TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TerminalWorkspaceConfiguration? Configuration { get; private set; }
        internal int SaveCount { get; private set; }
        internal bool FailSave { get; set; }

        public Task<TerminalWorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.NotFound, null, null));
        }
        public Task SaveAsync(TerminalWorkspaceConfiguration configuration, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (FailSave == true)
            {
                throw new IOException("Simulated workspace save failure.");
            }
            Configuration = configuration;
            Saved.TrySetResult();

            return Task.CompletedTask;
        }
        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            Assert.Fail("Reordering must not delete the workspace.");
            return Task.CompletedTask;
        }
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null) { }
    }
}
