using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalTabReorderTests
{
    private static readonly ShellLaunchSpec _shell = new("pwsh.exe", ["-NoLogo"], Path.GetTempPath());

    [TestMethod]
    public async Task RendererDragRoundTripConfirmsOrderRejectsReplayAndRestoresSavedConfiguration()
    {
        using var fixture = ReadFixture();
        var state = fixture.RootElement.GetProperty("states").GetProperty("base");
        var identifiers = new Queue<TerminalSessionId>(state.GetProperty("sessions").EnumerateArray().Select(ReadId));
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog(),
                                                                     () => identifiers.Dequeue());
        foreach (var entry in state.GetProperty("sessions").EnumerateArray())
        {
            var tab = factory.Sessions.Count == 0
                ? await coordinator.StartAsync(_shell, 80, 24, CancellationToken.None)
                : await coordinator.AddAsync(CancellationToken.None);
            for (var generation = 1; generation < entry.GetProperty("sessionGeneration").GetInt64(); generation++)
            {
                await coordinator.RestartAsync(tab.SessionId, CancellationToken.None);
            }
            Assert.IsTrue(coordinator.Rename(tab.SessionId, $"긴 이름 한글 terminal {factory.Sessions.Count}"));
        }
        Assert.IsTrue(coordinator.Select(ReadId(state, "activeSessionId")));
        var before = coordinator.RendererWorkspace;
        var eventCount = 0;
        coordinator.WorkspaceChanged += _ => eventCount++;
        var directory = Path.Combine(AppContext.BaseDirectory, "reorder-round-trip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var startInfo = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(FindRendererBridge());
        using var renderer = Process.Start(startInfo);
        Assert.IsNotNull(renderer);
        var errors = renderer.StandardError.ReadToEndAsync();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            await using var persistence = new TerminalWorkspacePersistence(store, coordinator, new NullDiagnosticLog(),
                                                                           TimeSpan.FromMilliseconds(5));
            _ = await persistence.InitializeAsync(true, CancellationToken.None);
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            persistence.StatusChanged += status =>
            {
                if (status.State == TerminalWorkspaceSaveState.Saved)
                {
                    saved.TrySetResult();
                }
            };
            // The bridge cancels one gesture before returning a valid drop. No host mutation or save occurred.
            var wire = await ExchangeRendererAsync(renderer, before, "start");
            Assert.AreEqual(0, eventCount);
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(RendererProtocol.TryParse(wire, out var message));
            Assert.IsNotNull(message?.ReorderRequest);
            var request = message.ReorderRequest;
            var context = new RendererReorderContext(true, 1, Guid.ParseExact(state.GetProperty("rendererInstanceId").GetString()!, "N"));
            Assert.IsTrue(context.CanApply(request, 1));
            Assert.AreEqual(TerminalTabReorderResult.Moved, coordinator.Reorder(request));
            var confirmed = coordinator.RendererWorkspace;
            CollectionAssert.AreEqual(new[] { before.Order[1], before.Order[2], before.Order[0], before.Order[3] },
                                      confirmed.Order.ToArray());
            var nextWire = await ExchangeRendererAsync(renderer, confirmed, "next");
            Assert.IsTrue(RendererProtocol.TryParse(nextWire, out var nextMessage));
            Assert.IsNotNull(nextMessage?.ReorderRequest);
            CollectionAssert.AreEqual(confirmed.Order.ToArray(), nextMessage.ReorderRequest.StartOrder.ToArray());
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var savedBytes = await File.ReadAllBytesAsync(path);
            var loaded = await store.LoadAsync(CancellationToken.None);
            var configuration = loaded.Configuration;
            Assert.IsNotNull(configuration);
            Assert.AreEqual(TerminalTabReorderResult.StaleOrder, coordinator.Reorder(request));
            Assert.AreEqual(1, eventCount);
            var rejectionWire = await ExchangeRendererAsync(renderer, coordinator.RendererWorkspace, "check");
            using var rejection = JsonDocument.Parse(rejectionWire);
            Assert.IsTrue(rejection.RootElement.GetProperty("preserved").GetBoolean());
            var bytesAfterRejection = await File.ReadAllBytesAsync(path);
            CollectionAssert.AreEqual(savedBytes, bytesAfterRejection);

            var restoredFactory = new RecordingSessionFactory();
            await using var restored = new TerminalSessionCoordinator(restoredFactory, new NullDiagnosticLog());
            _ = await restored.StartAsync(_shell, TerminalShellKind.Automatic, configuration, _ => _shell,
                                          80, 24, CancellationToken.None);
            CollectionAssert.AreEqual(confirmed.Workspace.Tabs.Select(tab => tab.ConfigurationId).ToArray(),
                                      restored.Snapshot.Tabs.Select(tab => tab.ConfigurationId).ToArray());
            Assert.AreEqual(configuration.ActiveTabConfigurationId,
                            restored.Snapshot.Tabs.Single(tab => tab.SessionId == restored.Snapshot.ActiveSessionId).ConfigurationId);
            Assert.IsFalse(restored.RendererWorkspace.Order.Any(session => confirmed.Order.Contains(session)));
            var restoredWire = await ExchangeRendererAsync(renderer, restored.RendererWorkspace, "restored", fresh: true);
            using var restoredReply = JsonDocument.Parse(restoredWire);
            CollectionAssert.AreEqual(confirmed.Workspace.Tabs.Select(tab => tab.Name).ToArray(),
                                      restoredReply.RootElement.GetProperty("names").EnumerateArray().Select(name => name.GetString()).ToArray());
            renderer.StandardInput.Close();
            await renderer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var rendererErrors = await errors;
            Assert.AreEqual(0, renderer.ExitCode, rendererErrors);
        }
        catch (Exception exception) when (renderer.HasExited == true)
        {
            throw new InvalidOperationException("Renderer bridge failed: " + await errors, exception);
        }
        finally
        {
            if (renderer.HasExited == false)
            {
                renderer.Kill(entireProcessTree: true);
                await renderer.WaitForExitAsync();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> ExchangeRendererAsync(Process renderer, TerminalRendererWorkspaceSnapshot snapshot,
                                                            string action, bool fresh = false)
    {
        var messages = new List<string> { RendererProtocol.SerializeWorkspaceSyncMessage("workspace-sync-begin", snapshot) };
        for (var index = 0; index < snapshot.Workspace.Tabs.Count; index++)
        {
            var tab = snapshot.Workspace.Tabs[index];
            messages.Add(RendererProtocol.SerializeSessionMessage("session-upsert", tab.SessionId,
                                                                  new
                                                                  {
                                                                      tab.Name,
                                                                      state = "running",
                                                                      exitCode = (int?)null,
                                                                      order = index,
                                                                      canAddSession = true,
                                                                      sessionGeneration = snapshot.Order[index].Generation,
                                                                      tab.StartingDirectory,
                                                                      homeDirectory = tab.StartingDirectory,
                                                                      shellKind = "automatic",
                                                                  }));
        }
        messages.Add(RendererProtocol.SerializeSessionMessage("activate-session", snapshot.Workspace.ActiveSessionId!.Value, new { }));
        messages.Add(RendererProtocol.SerializeWorkspaceSyncMessage("workspace-sync-complete", snapshot));
        var command = JsonSerializer.Serialize(new { action, fresh, messages = messages.Select(json => JsonSerializer.Deserialize<JsonElement>(json)) });
        await renderer.StandardInput.WriteLineAsync(command);
        await renderer.StandardInput.FlushAsync();
        var reply = await renderer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsNotNull(reply, "Renderer bridge terminated before confirming the host snapshot.");

        return reply;
    }

    private static string FindRendererBridge()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Starboard.Windows.sln")) == true)
            {
                return Path.Combine(directory.FullName, "src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                    "Renderer", "test", "reorderRoundTrip.mjs");
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the renderer bridge.");
    }

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
                    if (resync == "current")
                    {
                        AssertSynchronization(coordinator.RendererWorkspace, label);
                    }
                }

                var after = coordinator.RendererWorkspace;
                CollectionAssert.AreEqual(expected.GetProperty("order").EnumerateArray().Select(value => value.GetString()).ToArray(),
                                          after.Workspace.Tabs.Select(tab => tab.SessionId.ToString()).ToArray(), label);
                Assert.AreEqual(expected.GetProperty("workspaceChangedCount").GetInt32(), eventCount, label);
                Assert.AreEqual(before.Workspace.ActiveSessionId, after.Workspace.ActiveSessionId, label);
                CollectionAssert.AreEqual(beforeSessions, factory.Sessions.ToArray(), label);
                CollectionAssert.AreEqual(beforeResizes, beforeSessions.Select(session => session.ResizeCount).ToArray(), label);
                CollectionAssert.AreEqual(beforeDisposals, beforeSessions.Select(session => session.DisposeCount).ToArray(), label);
                foreach (var session in beforeSessions)
                {
                    Assert.AreEqual(_shell, session.Shell, label);
                }
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
                    Assert.AreEqual(2, store.Configuration.SchemaVersion, label);
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

    [TestMethod]
    [DataRow("add", "stale-order")]
    [DataRow("close-target", "missing-target")]
    [DataRow("restart-source", "stale-generation")]
    [DataRow("restart-target", "stale-order")]
    public async Task ReorderAfterLifecycleChangeRejectsOldRequestAndSynchronizesCurrentSessions(string change,
                                                                                                 string expectedReason)
    {
        var factory = new RecordingSessionFactory();
        await using var coordinator = new TerminalSessionCoordinator(factory, new NullDiagnosticLog());
        var first = await coordinator.StartAsync(_shell, 80, 24, CancellationToken.None);
        _ = await coordinator.AddAsync(CancellationToken.None);
        var third = await coordinator.AddAsync(CancellationToken.None);
        var start = coordinator.RendererWorkspace;
        var request = new TerminalTabReorderRequest(Guid.NewGuid(), start.Order[0], third.SessionId,
                                                    TerminalTabInsertionPosition.After, start.Order);
        switch (change)
        {
            case "add":
                _ = await coordinator.AddAsync(CancellationToken.None);
                break;
            case "close-target":
                factory.Sessions[2].RaiseExit();
                Assert.IsTrue(await coordinator.CloseAsync(third.SessionId, CancellationToken.None));
                break;
            case "restart-source":
                await coordinator.RestartAsync(first.SessionId, CancellationToken.None);
                break;
            case "restart-target":
                await coordinator.RestartAsync(third.SessionId, CancellationToken.None);
                break;
        }

        var current = coordinator.RendererWorkspace;
        var sessions = factory.Sessions.ToArray();
        var resizes = sessions.Select(session => session.ResizeCount).ToArray();
        var disposals = sessions.Select(session => session.DisposeCount).ToArray();
        var eventCount = 0;
        coordinator.WorkspaceChanged += _ => eventCount++;

        Assert.AreEqual(expectedReason, FormatReason(coordinator.Reorder(request)));
        var synchronized = coordinator.RendererWorkspace;
        AssertSynchronization(synchronized, change);
        CollectionAssert.AreEqual(current.Order.ToArray(), synchronized.Order.ToArray());
        Assert.AreEqual(current.Workspace.ActiveSessionId, synchronized.Workspace.ActiveSessionId);
        Assert.AreEqual(0, eventCount);
        foreach (var tab in current.Workspace.Tabs)
        {
            Assert.AreSame(tab, synchronized.Workspace.Tabs.Single(candidate => candidate.SessionId == tab.SessionId));
        }

        var freshRequest = request with
        {
            Session = synchronized.Order[0],
            TargetSessionId = synchronized.Workspace.Tabs[synchronized.Workspace.Tabs.Count - 1].SessionId,
            StartOrder = synchronized.Order,
        };
        Assert.AreEqual(TerminalTabReorderResult.Moved, coordinator.Reorder(freshRequest));
        Assert.AreEqual(1, eventCount);
        AssertSynchronization(coordinator.RendererWorkspace, change);
        Assert.AreEqual(current.Workspace.ActiveSessionId, coordinator.Snapshot.ActiveSessionId);
        CollectionAssert.AreEqual(sessions, factory.Sessions.ToArray());
        CollectionAssert.AreEqual(resizes, sessions.Select(session => session.ResizeCount).ToArray());
        CollectionAssert.AreEqual(disposals, sessions.Select(session => session.DisposeCount).ToArray());
        CollectionAssert.AreEquivalent(current.Order.ToArray(), coordinator.RendererWorkspace.Order.ToArray());
        await coordinator.WriteAsync(first.SessionId, "after resync", CancellationToken.None);
        var sourceSession = change == "restart-source" ? sessions[sessions.Length - 1] : sessions[0];
        Assert.AreEqual("after resync", sourceSession.Writes.Last());
        Assert.IsTrue(sessions.All(session => session.Shell == _shell));
    }

    private static void AssertSynchronization(TerminalRendererWorkspaceSnapshot snapshot, string label)
    {
        var begin = RendererProtocol.SerializeWorkspaceSyncMessage("workspace-sync-begin", snapshot);
        var complete = RendererProtocol.SerializeWorkspaceSyncMessage("workspace-sync-complete", snapshot);
        using var beginDocument = JsonDocument.Parse(begin);
        using var completeDocument = JsonDocument.Parse(complete);
        var payload = completeDocument.RootElement.GetProperty("payload");
        Assert.AreEqual(beginDocument.RootElement.GetProperty("payload").GetRawText(), payload.GetRawText(), label);
        Assert.AreEqual(snapshot.Workspace.ActiveSessionId?.ToString(),
                        payload.GetProperty("activeSessionId").GetString(), label);
        var order = payload.GetProperty("order");
        Assert.AreEqual(snapshot.Workspace.Tabs.Count, order.GetArrayLength(), label);
        for (var index = 0; index < order.GetArrayLength(); index++)
        {
            Assert.AreEqual(snapshot.Workspace.Tabs[index].SessionId.ToString(),
                            order[index].GetProperty("sessionId").GetString(), label);
            Assert.AreEqual(snapshot.Order[index].Generation,
                            order[index].GetProperty("sessionGeneration").GetInt64(), label);
        }
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

        public void SetOutputPaused(bool paused) { }

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
