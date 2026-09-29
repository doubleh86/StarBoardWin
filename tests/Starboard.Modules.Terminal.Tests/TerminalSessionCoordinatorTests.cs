using System.Diagnostics;
using System.IO;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalSessionCoordinatorTests
{
    private static readonly ShellLaunchSpec TestShell = new("pwsh.exe", ["-NoLogo"], Path.GetTempPath());

    private static readonly ShellLaunchSpec UpdatedShell = new("powershell.exe", ["-NoLogo"], Path.GetTempPath());

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
    public async Task CurrentDirectoryIsIsolatedValidatedAndClearedAcrossCommandsAndRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Starboard 위치 {Guid.NewGuid():N}");
        var firstDirectory = Directory.CreateDirectory(Path.Combine(root, "한글 폴더")).FullName;
        var secondDirectory = Directory.CreateDirectory(Path.Combine(root, "second folder")).FullName;
        try
        {
            var factory = new FakeTerminalSessionFactory();
            await using var coordinator = CreateCoordinator(factory);
            var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
            var second = await coordinator.AddAsync(CancellationToken.None);
            var firstSession = FindOutputState(coordinator.NewOutputState, first.SessionId).Session;
            var secondSession = FindOutputState(coordinator.NewOutputState, second.SessionId).Session;

            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Ready(firstDirectory));
            factory.Sessions[1].RaiseCommandSignal(TerminalSessionCommandSignal.Ready(secondDirectory));
            Assert.AreEqual(firstDirectory, coordinator.GetCurrentDirectory(firstSession));
            Assert.AreEqual(secondDirectory, coordinator.GetCurrentDirectory(secondSession));
            Assert.IsNull(coordinator.GetCurrentDirectory(new TerminalSessionReference(firstSession.SessionId,
                                                                                       firstSession.Generation + 1)));

            var executionId = TerminalCommandExecutionId.CreateNew();
            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Started(executionId));
            Assert.IsNull(coordinator.GetCurrentDirectory(firstSession));
            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Finished(executionId,
                                                                                         0, firstDirectory));
            Assert.AreEqual(firstDirectory, coordinator.GetCurrentDirectory(firstSession));
            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Ready("/tmp/linux"));
            Assert.IsNull(coordinator.GetCurrentDirectory(firstSession));
            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Ready(firstDirectory + " missing"));
            Assert.IsNull(coordinator.GetCurrentDirectory(firstSession));

            await coordinator.RestartAsync(first.SessionId, CancellationToken.None);
            Assert.IsNull(coordinator.GetCurrentDirectory(firstSession));
            Assert.IsNull(coordinator.GetCurrentDirectory(FindOutputState(coordinator.NewOutputState,
                                                                          first.SessionId).Session));
            Assert.AreEqual(secondDirectory, coordinator.GetCurrentDirectory(secondSession));
            Assert.IsTrue(factory.Sessions.All(session => session.Writes.Count == 0));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task CurrentDirectoryExpiresAndCmdCannotClaimPowerShellLocation()
    {
        var currentTime = DateTimeOffset.UtcNow;
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory, currentTime: () => currentTime);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var session = FindOutputState(coordinator.NewOutputState, tab.SessionId).Session;
        factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Ready(Path.GetTempPath()));
        Assert.IsNotNull(coordinator.GetCurrentDirectory(session));

        currentTime += TimeSpan.FromSeconds(61);
        Assert.IsNull(coordinator.GetCurrentDirectory(session));

        var cmdFactory = new FakeTerminalSessionFactory();
        await using var cmdCoordinator = CreateCoordinator(cmdFactory);
        var cmd = await cmdCoordinator.StartAsync(new ShellLaunchSpec("cmd.exe", ["/Q"], Path.GetTempPath()),
                                                  80, 24, CancellationToken.None);
        var cmdSession = FindOutputState(cmdCoordinator.NewOutputState, cmd.SessionId).Session;
        cmdFactory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Ready(Path.GetTempPath()));
        Assert.IsNull(cmdCoordinator.GetCurrentDirectory(cmdSession));
    }

    [TestMethod]
    public async Task WriteActiveAsyncAfterTabSelectionRoutesOnlyToSelectedSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var third = await coordinator.AddAsync(CancellationToken.None);

        await coordinator.WriteActiveAsync("third", CancellationToken.None);
        Assert.IsTrue(coordinator.Select(second.SessionId));
        await coordinator.WriteActiveAsync("second", CancellationToken.None);
        Assert.IsTrue(coordinator.Select(first.SessionId));
        await coordinator.WriteActiveAsync("first", CancellationToken.None);

        CollectionAssert.AreEqual(new List<string> { "first" }, factory.Sessions[0].Writes);
        CollectionAssert.AreEqual(new List<string> { "second" }, factory.Sessions[1].Writes);
        CollectionAssert.AreEqual(new List<string> { "third" }, factory.Sessions[2].Writes);
        Assert.AreEqual(second.SessionId, coordinator.Snapshot.Tabs[1].SessionId);
        Assert.AreEqual(third.SessionId, coordinator.Snapshot.Tabs[2].SessionId);
    }

    [TestMethod]
    public async Task WriteAsyncWithSessionIdentifierRoutesWithoutChangingSelection()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);

        await coordinator.WriteAsync(first.SessionId, "first", CancellationToken.None);

        CollectionAssert.AreEqual(new List<string> { "first" }, factory.Sessions[0].Writes);
        Assert.AreEqual(0, factory.Sessions[1].Writes.Count);
        Assert.AreEqual(second.SessionId, coordinator.Snapshot.ActiveSessionId);
    }

    [TestMethod]
    public async Task RenameAndMoveDoNotRecreateExistingSessions()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);

        Assert.IsTrue(coordinator.Rename(first.SessionId, "서버"));
        Assert.IsTrue(coordinator.MoveRight(first.SessionId));

        var snapshot = coordinator.Snapshot;
        Assert.AreEqual(2, factory.Sessions.Count);
        Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(first.SessionId, snapshot.Tabs[1].SessionId);
        Assert.AreEqual("서버", snapshot.Tabs[1].Name);
        Assert.AreEqual(second.SessionId, snapshot.ActiveSessionId);
    }

    [TestMethod]
    public async Task UpdateStartingDirectoryKeepsLiveSessionAndAppliesPathOnExplicitRestart()
    {
        var startingDirectory = Path.Combine(Path.GetTempPath(), $"Starboard 시작 폴더 {Guid.NewGuid():N}");
        Directory.CreateDirectory(startingDirectory);
        try
        {
            var factory = new FakeTerminalSessionFactory();
            await using var coordinator = CreateCoordinator(factory);
            var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
            _ = await coordinator.AddAsync(CancellationToken.None);

            var result = coordinator.UpdateStartingDirectory(first.SessionId, startingDirectory);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, factory.Sessions.Count);
            Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
            Assert.AreEqual(0, factory.Sessions[0].Writes.Count);
            Assert.AreEqual(Path.GetFullPath(startingDirectory), FindTab(coordinator.Snapshot, first.SessionId).StartingDirectory);

            await coordinator.RestartAsync(first.SessionId, CancellationToken.None);

            Assert.AreEqual(3, factory.StartRequests.Count);
            Assert.AreEqual(Path.GetFullPath(startingDirectory), factory.StartRequests[2].Shell.WorkingDirectory);
            Assert.AreEqual(TestShell.ExecutablePath, factory.StartRequests[2].Shell.ExecutablePath);
            CollectionAssert.AreEqual(TestShell.Arguments.ToArray(),
                                      factory.StartRequests[2].Shell.Arguments.ToArray());
        }
        finally
        {
            Directory.Delete(startingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RestartAsyncWhenStartingDirectoryWasDeletedFailsOnlyRequestedTab()
    {
        var startingDirectory = Path.Combine(Path.GetTempPath(), $"Starboard deleted {Guid.NewGuid():N}");
        Directory.CreateDirectory(startingDirectory);
        try
        {
            var factory = new FakeTerminalSessionFactory();
            await using var coordinator = CreateCoordinator(factory);
            var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
            var second = await coordinator.AddAsync(CancellationToken.None);
            Assert.IsTrue(coordinator.UpdateStartingDirectory(first.SessionId, startingDirectory).Succeeded);
            Directory.Delete(startingDirectory, recursive: true);

            await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
                coordinator.RestartAsync(first.SessionId, CancellationToken.None));

            Assert.AreEqual(TerminalSessionState.Failed, FindTab(coordinator.Snapshot, first.SessionId).State);
            Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, second.SessionId).State);
            Assert.AreEqual(2, factory.StartRequests.Count);
            Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        }
        finally
        {
            if (Directory.Exists(startingDirectory) == true)
            {
                Directory.Delete(startingDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ResizeWithSessionIdentifierRoutesOnlyToRequestedSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);

        var resized = coordinator.Resize(first.SessionId, 120, 40);

        Assert.IsTrue(resized);
        Assert.AreEqual(1, factory.Sessions[0].ResizeCount);
        Assert.AreEqual(0, factory.Sessions[1].ResizeCount);
        Assert.AreEqual(second.SessionId, coordinator.Snapshot.ActiveSessionId);
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
    public async Task OutputAndExitCallbacksCarryGenerationAndRejectPreviousLifetimeAfterRestart()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var outputs = new List<TerminalSessionOutput>();
        var exits = new List<TerminalSessionExit>();
        coordinator.OutputReceived += outputs.Add;
        coordinator.SessionExited += exits.Add;

        factory.Sessions[0].RaiseOutput("current-generation-output");
        await coordinator.RestartAsync(tab.SessionId, CancellationToken.None);
        factory.Sessions[0].RaiseOutput("late-previous-generation-output");
        factory.Sessions[0].RaiseExit(91);
        factory.Sessions[1].RaiseOutput("replacement-generation-output");

        Assert.HasCount(2, outputs);
        Assert.AreEqual(1, outputs[0].Session.Generation);
        Assert.AreEqual(2, outputs[1].Session.Generation);
        Assert.AreEqual(tab.SessionId, outputs[0].SessionId);
        Assert.AreEqual(tab.SessionId, outputs[1].SessionId);
        Assert.IsEmpty(exits);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, tab.SessionId).State);
    }

    [TestMethod]
    public async Task InitialConPtyCreationFailureLeavesTabRestartableWithoutReplacingWorkspace()
    {
        var factory = new FakeTerminalSessionFactory
        {
            FailureStartNumber = 1,
        };
        await using var coordinator = CreateCoordinator(factory);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None));
        var failedTab = coordinator.Snapshot.Tabs.Single();
        Assert.AreEqual(TerminalSessionState.Failed, failedTab.State);

        factory.FailureStartNumber = null;
        await coordinator.RestartAsync(failedTab.SessionId, CancellationToken.None);

        Assert.AreEqual(1, coordinator.Snapshot.Tabs.Count);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, failedTab.SessionId).State);
        Assert.AreEqual(2, factory.StartRequests.Count);
        Assert.AreEqual(1, factory.Sessions.Count);
    }

    [TestMethod]
    public async Task RemovedAndDisposedSessionsRejectLateOutputAndExitCallbacks()
    {
        var factory = new FakeTerminalSessionFactory();
        var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var outputs = new List<TerminalSessionOutput>();
        var exits = new List<TerminalSessionExit>();
        coordinator.OutputReceived += outputs.Add;
        coordinator.SessionExited += exits.Add;

        factory.Sessions[0].RaiseExit(0);
        Assert.IsTrue(await coordinator.CloseAsync(tab.SessionId, CancellationToken.None));
        factory.Sessions[0].RaiseOutput("late-removed-output");
        factory.Sessions[0].RaiseExit(92);

        Assert.IsEmpty(outputs);
        Assert.HasCount(1, exits);

        await coordinator.DisposeAsync();
        factory.Sessions[1].RaiseOutput("late-shutdown-output");
        factory.Sessions[1].RaiseExit(93);

        Assert.IsEmpty(outputs);
        Assert.HasCount(1, exits);
    }

    [TestMethod]
    public async Task UpdateDefaultShellPreservesExistingSessionsAndTheirRestartShell()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        _ = await coordinator.AddAsync(CancellationToken.None);

        coordinator.UpdateDefaultShell(UpdatedShell);

        Assert.AreEqual(2, factory.Sessions.Count);
        Assert.IsTrue(factory.Sessions.All(session => session.DisposeCount == 0));

        _ = await coordinator.AddAsync(CancellationToken.None);
        await coordinator.RestartAsync(first.SessionId, CancellationToken.None);

        CollectionAssert.AreEqual(new List<ShellLaunchSpec>
                                  {
                                      TestShell,
                                      TestShell,
                                      UpdatedShell,
                                      TestShell,
                                  },
                                  factory.StartRequests.Select(request => request.Shell).ToList());
        Assert.AreEqual(1, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[2].DisposeCount);
    }

    [TestMethod]
    public async Task CloseAsyncLastTabUsesLatestDefaultShellForReplacement()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        coordinator.UpdateDefaultShell(UpdatedShell);
        factory.Sessions[0].RaiseExit(0);

        var closed = await coordinator.CloseAsync(first.SessionId, CancellationToken.None);
        Assert.IsTrue(closed);

        Assert.AreEqual(2, factory.StartRequests.Count);
        Assert.AreEqual(TestShell, factory.StartRequests[0].Shell);
        Assert.AreEqual(UpdatedShell, factory.StartRequests[1].Shell);
    }

    [TestMethod]
    public async Task CloseAsyncInactiveTabDisposesOnlyClosedSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        factory.Sessions[0].RaiseExit(0);

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
        var cleanupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeTerminalSessionFactory(index => new FakeTerminalSession(index == 0 ? cleanupGate : null));
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        factory.Sessions[0].RaiseExit(0);

        var closeTask = coordinator.CloseAsync(first.SessionId, CancellationToken.None);
        var snapshotDuringCleanup = coordinator.Snapshot;

        Assert.IsFalse(closeTask.IsCompleted);
        Assert.AreEqual(2, factory.Sessions.Count);
        Assert.AreEqual(1, snapshotDuringCleanup.Tabs.Count);
        Assert.AreEqual("PowerShell 2", snapshotDuringCleanup.Tabs[0].Name);
        Assert.AreEqual(snapshotDuringCleanup.Tabs[0].SessionId, snapshotDuringCleanup.ActiveSessionId);

        cleanupGate.TrySetResult();
        var closed = await closeTask;
        Assert.IsTrue(closed);
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

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.AddAsync(CancellationToken.None));
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

        await Assert.ThrowsExactlyAsync<IOException>(async () => await coordinator.WriteActiveAsync("input", CancellationToken.None));
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
        factory.Sessions[0].ResizeException = new InvalidOperationException("Simulated resize failure.");

        coordinator.ResizeAll(120, 40);
        var snapshot = coordinator.Snapshot;

        Assert.AreEqual(TerminalSessionState.Failed, FindTab(snapshot, first.SessionId).State);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(snapshot, second.SessionId).State);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(1, factory.Sessions[1].ResizeCount);
    }

    [TestMethod]
    public async Task CloseConfirmationCancelledPreservesLiveSessionAndRejectsDuplicateRequest()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var originalSession = factory.Sessions[0];

        var request = coordinator.RequestCloseConfirmation(tab.SessionId);
        var duplicateRequest = coordinator.RequestCloseConfirmation(tab.SessionId);

        Assert.IsNotNull(request);
        Assert.IsNull(duplicateRequest);
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Cancelled);
        var applied = await coordinator.ApplyCloseConfirmationAsync(response, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreSame(originalSession, factory.Sessions[0]);
        Assert.AreEqual(0, originalSession.DisposeCount);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, tab.SessionId).State);
        Assert.AreEqual(tab.SessionId, coordinator.Snapshot.ActiveSessionId);
    }

    [TestMethod]
    public async Task CloseAsyncWithoutConfirmationDoesNotCloseLiveSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);

        var closed = await coordinator.CloseAsync(tab.SessionId, CancellationToken.None);

        Assert.IsFalse(closed);
        Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, tab.SessionId).State);
    }

    [TestMethod]
    public async Task CloseConfirmationAfterRestartDoesNotCloseReplacementSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var request = coordinator.RequestCloseConfirmation(tab.SessionId);
        Assert.IsNotNull(request);

        await coordinator.RestartAsync(tab.SessionId, CancellationToken.None);
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Confirmed);
        var applied = await coordinator.ApplyCloseConfirmationAsync(response, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(1, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, tab.SessionId).State);
    }

    [TestMethod]
    public async Task CloseConfirmationConfirmedClosesExactlyOnce()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var request = coordinator.RequestCloseConfirmation(first.SessionId);
        Assert.IsNotNull(request);
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Confirmed);

        var firstApplied = await coordinator.ApplyCloseConfirmationAsync(response, CancellationToken.None);
        var secondApplied = await coordinator.ApplyCloseConfirmationAsync(response, CancellationToken.None);

        Assert.IsTrue(firstApplied);
        Assert.IsFalse(secondApplied);
        Assert.AreEqual(1, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(second.SessionId, coordinator.Snapshot.ActiveSessionId);
    }

    [TestMethod]
    public async Task PasteConfirmationConfirmedWritesCapturedSnapshotExactlyOnce()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        const string clipboardSnapshot = "Set-Location C:\\temp\r\nGet-Location\r\n";

        var request = coordinator.RequestPasteConfirmation(tab.SessionId, clipboardSnapshot);
        var duplicateRequest = coordinator.RequestPasteConfirmation(tab.SessionId, "ignored\n");

        Assert.IsNotNull(request);
        Assert.IsNull(duplicateRequest);
        Assert.AreEqual(clipboardSnapshot, request.ClipboardText);
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Confirmed);
        var firstApplied = await coordinator.ApplyPasteConfirmationAsync(response, CancellationToken.None);
        var secondApplied = await coordinator.ApplyPasteConfirmationAsync(response, CancellationToken.None);

        Assert.IsTrue(firstApplied);
        Assert.IsFalse(secondApplied);
        CollectionAssert.AreEqual(new[] { clipboardSnapshot }, factory.Sessions[0].Writes);
    }

    [TestMethod]
    public async Task PasteConfirmationAfterTargetSelectionChangesIsDiscarded()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var request = coordinator.RequestPasteConfirmation(second.SessionId, "Get-Location\n");
        Assert.IsNotNull(request);

        Assert.IsTrue(coordinator.Select(first.SessionId));
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Confirmed);
        var applied = await coordinator.ApplyPasteConfirmationAsync(response, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(0, factory.Sessions[0].Writes.Count);
        Assert.AreEqual(0, factory.Sessions[1].Writes.Count);
    }

    [TestMethod]
    public async Task PasteConfirmationAfterRestartIsDiscarded()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var request = coordinator.RequestPasteConfirmation(tab.SessionId, "Get-Location\r");
        Assert.IsNotNull(request);

        await coordinator.RestartAsync(tab.SessionId, CancellationToken.None);
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Confirmed);
        var applied = await coordinator.ApplyPasteConfirmationAsync(response, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(0, factory.Sessions[0].Writes.Count);
        Assert.AreEqual(0, factory.Sessions[1].Writes.Count);
    }

    [TestMethod]
    public async Task PasteConfirmationCancelledOrResetNeverWritesClipboardSnapshot()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var cancelledRequest = coordinator.RequestPasteConfirmation(tab.SessionId, "cancelled\n");
        Assert.IsNotNull(cancelledRequest);

        var cancelledResponse = new TerminalConfirmationResponse(cancelledRequest.Token,
                                                                 TerminalConfirmationResult.Cancelled);
        var cancelledApplied = await coordinator.ApplyPasteConfirmationAsync(cancelledResponse,
                                                                             CancellationToken.None);
        var resetRequest = coordinator.RequestPasteConfirmation(tab.SessionId, "reset\n");
        Assert.IsNotNull(resetRequest);
        coordinator.CancelPendingConfirmation();
        var resetResponse = new TerminalConfirmationResponse(resetRequest.Token, TerminalConfirmationResult.Confirmed);
        var resetApplied = await coordinator.ApplyPasteConfirmationAsync(resetResponse, CancellationToken.None);

        Assert.IsFalse(cancelledApplied);
        Assert.IsFalse(resetApplied);
        Assert.AreEqual(0, factory.Sessions[0].Writes.Count);
    }

    [TestMethod]
    public async Task PasteConfirmationAfterTargetRemovalIsDiscarded()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        _ = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var target = await coordinator.AddAsync(CancellationToken.None);
        var request = coordinator.RequestPasteConfirmation(target.SessionId, "Get-Location\n");
        Assert.IsNotNull(request);

        factory.Sessions[1].RaiseExit(0);
        Assert.IsTrue(await coordinator.CloseAsync(target.SessionId, CancellationToken.None));
        var response = new TerminalConfirmationResponse(request.Token, TerminalConfirmationResult.Confirmed);
        var applied = await coordinator.ApplyPasteConfirmationAsync(response, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(0, factory.Sessions[1].Writes.Count);
    }

    [TestMethod]
    public async Task PathDropConfirmationConfirmedWritesQuotedInputWithoutExecutingCommand()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var session = FindOutputState(coordinator.NewOutputState, tab.SessionId).Session;
        var preparation = coordinator.RequestPathDropConfirmation(
            session, ["C:\\한글 폴더\\a'b&$(draft).txt", "D:\\work\\file.txt"]);
        Assert.IsTrue(preparation.Succeeded);
        Assert.IsNotNull(preparation.ConfirmationRequest);

        var response = new TerminalConfirmationResponse(preparation.ConfirmationRequest.Token,
                                                        TerminalConfirmationResult.Confirmed);
        var applied = await coordinator.ApplyPathDropConfirmationAsync(response, CancellationToken.None);

        Assert.IsTrue(applied);
        CollectionAssert.AreEqual(
            new List<string> { "'C:\\한글 폴더\\a''b&$(draft).txt' 'D:\\work\\file.txt'" },
            factory.Sessions[0].Writes);
        Assert.IsFalse(factory.Sessions[0].Writes[0].Contains('\r'));
        Assert.IsFalse(factory.Sessions[0].Writes[0].Contains('\n'));
    }

    [TestMethod]
    public async Task PathDropConfirmationAfterTabSwitchIsDiscarded()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var target = await coordinator.AddAsync(CancellationToken.None);
        var session = FindOutputState(coordinator.NewOutputState, target.SessionId).Session;
        var preparation = coordinator.RequestPathDropConfirmation(session, ["C:\\work\\file.txt"]);
        Assert.IsNotNull(preparation.ConfirmationRequest);

        Assert.IsTrue(coordinator.Select(first.SessionId));
        var response = new TerminalConfirmationResponse(preparation.ConfirmationRequest.Token,
                                                        TerminalConfirmationResult.Confirmed);
        var applied = await coordinator.ApplyPathDropConfirmationAsync(response, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(0, factory.Sessions[0].Writes.Count);
        Assert.AreEqual(0, factory.Sessions[1].Writes.Count);
    }

    [TestMethod]
    public async Task PathDropConfirmationAfterRestartOrRemovalIsDiscarded()
    {
        var restartFactory = new FakeTerminalSessionFactory();
        await using var restartCoordinator = CreateCoordinator(restartFactory);
        var restartTab = await restartCoordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var restartSession = FindOutputState(restartCoordinator.NewOutputState, restartTab.SessionId).Session;
        var restartPreparation = restartCoordinator.RequestPathDropConfirmation(
            restartSession, ["C:\\work\\restart.txt"]);
        Assert.IsNotNull(restartPreparation.ConfirmationRequest);
        await restartCoordinator.RestartAsync(restartTab.SessionId, CancellationToken.None);

        var restartResponse = new TerminalConfirmationResponse(restartPreparation.ConfirmationRequest.Token,
                                                               TerminalConfirmationResult.Confirmed);
        var restartApplied = await restartCoordinator.ApplyPathDropConfirmationAsync(
            restartResponse, CancellationToken.None);

        var removalFactory = new FakeTerminalSessionFactory();
        await using var removalCoordinator = CreateCoordinator(removalFactory);
        var removalTab = await removalCoordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var removalSession = FindOutputState(removalCoordinator.NewOutputState, removalTab.SessionId).Session;
        var removalPreparation = removalCoordinator.RequestPathDropConfirmation(
            removalSession, ["C:\\work\\removed.txt"]);
        Assert.IsNotNull(removalPreparation.ConfirmationRequest);
        removalFactory.Sessions[0].RaiseExit(0);
        Assert.IsTrue(await removalCoordinator.CloseAsync(removalTab.SessionId, CancellationToken.None));

        var removalResponse = new TerminalConfirmationResponse(removalPreparation.ConfirmationRequest.Token,
                                                               TerminalConfirmationResult.Confirmed);
        var removalApplied = await removalCoordinator.ApplyPathDropConfirmationAsync(
            removalResponse, CancellationToken.None);

        Assert.IsFalse(restartApplied);
        Assert.IsFalse(removalApplied);
        Assert.IsTrue(restartFactory.Sessions.All(session => session.Writes.Count == 0));
        Assert.AreEqual(0, removalFactory.Sessions[0].Writes.Count);
    }

    [TestMethod]
    public async Task RequestPathDropConfirmationStaleGenerationOrCustomShellReturnsSafeFailure()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var currentSession = FindOutputState(coordinator.NewOutputState, tab.SessionId).Session;
        var staleSession = new TerminalSessionReference(currentSession.SessionId, currentSession.Generation + 1);

        var stale = coordinator.RequestPathDropConfirmation(staleSession, ["C:\\work\\file.txt"]);

        Assert.IsFalse(stale.Succeeded);
        Assert.IsNull(stale.ConfirmationRequest);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stale.ErrorMessage));
        Assert.AreEqual(0, factory.Sessions[0].Writes.Count);

        var customFactory = new FakeTerminalSessionFactory();
        await using var customCoordinator = CreateCoordinator(customFactory);
        var customShell = new ShellLaunchSpec("custom-shell.exe", [], Path.GetTempPath());
        var customTab = await customCoordinator.StartAsync(customShell, null, null, ShellResolver.Resolve,
                                                           80, 24, CancellationToken.None);
        var customSession = FindOutputState(customCoordinator.NewOutputState, customTab.SessionId).Session;

        var unsupported = customCoordinator.RequestPathDropConfirmation(customSession, ["C:\\work\\file.txt"]);

        Assert.IsFalse(unsupported.Succeeded);
        Assert.IsNull(unsupported.ConfirmationRequest);
        Assert.IsFalse(string.IsNullOrWhiteSpace(unsupported.ErrorMessage));
        Assert.AreEqual(0, customFactory.Sessions[0].Writes.Count);
    }

    [TestMethod]
    public async Task NewOutputStateTracksInactiveSessionAndResetsBySelectionRestartAndRemoval()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var third = await coordinator.AddAsync(CancellationToken.None);

        factory.Sessions[0].RaiseOutput("first output");
        factory.Sessions[0].RaiseOutput("more output");
        factory.Sessions[1].RaiseOutput(string.Empty);
        factory.Sessions[2].RaiseOutput("active output");

        Assert.IsTrue(FindOutputState(coordinator.NewOutputState, first.SessionId).HasNewOutput);
        Assert.IsFalse(FindOutputState(coordinator.NewOutputState, second.SessionId).HasNewOutput);
        Assert.IsFalse(FindOutputState(coordinator.NewOutputState, third.SessionId).HasNewOutput);

        Assert.IsTrue(coordinator.Select(first.SessionId));
        Assert.IsFalse(FindOutputState(coordinator.NewOutputState, first.SessionId).HasNewOutput);

        factory.Sessions[1].RaiseOutput("second output");
        var generationBeforeRestart = FindOutputState(coordinator.NewOutputState, second.SessionId).Session.Generation;
        Assert.IsTrue(FindOutputState(coordinator.NewOutputState, second.SessionId).HasNewOutput);

        await coordinator.RestartAsync(second.SessionId, CancellationToken.None);
        factory.Sessions[1].RaiseOutput("late output");
        var restartedState = FindOutputState(coordinator.NewOutputState, second.SessionId);
        Assert.AreEqual(generationBeforeRestart + 1, restartedState.Session.Generation);
        Assert.IsFalse(restartedState.HasNewOutput);

        factory.Sessions[3].RaiseExit(0);
        Assert.IsTrue(await coordinator.CloseAsync(second.SessionId, CancellationToken.None));
        Assert.IsFalse(coordinator.NewOutputState.States.Any(
            state => state.Session.SessionId == second.SessionId.Value));
    }

    [TestMethod]
    public async Task ClosingActiveExitedTabClearsNewOutputOnAutomaticallySelectedTab()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        factory.Sessions[0].RaiseOutput("first output");
        Assert.IsTrue(FindOutputState(coordinator.NewOutputState, first.SessionId).HasNewOutput);
        factory.Sessions[1].RaiseExit(0);

        Assert.IsTrue(await coordinator.CloseAsync(second.SessionId, CancellationToken.None));

        Assert.AreEqual(first.SessionId, coordinator.Snapshot.ActiveSessionId);
        Assert.IsFalse(FindOutputState(coordinator.NewOutputState, first.SessionId).HasNewOutput);
    }

    [TestMethod]
    public async Task AddAsyncAtMaximumTabsDoesNotCreateAnotherShellProcess()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory, maximumTabs: 2);
        _ = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        _ = await coordinator.AddAsync(CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.AddAsync(CancellationToken.None));

        Assert.AreEqual(2, factory.StartRequests.Count);
        Assert.AreEqual(2, factory.Sessions.Count);
    }

    [TestMethod]
    public async Task LaunchProfileAsyncWslCreatesIndependentSessionAndIsolatesLaunchFailure()
    {
        var factory = new FakeTerminalSessionFactory
        {
            FailureStartNumber = 3,
        };
        await using var coordinator = CreateCoordinator(factory);
        var source = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var sourceReference = FindOutputState(coordinator.NewOutputState, source.SessionId).Session;
        var profile = TerminalLaunchProfile.CreateWsl("개발 Ubuntu");
        var wslShell = new ShellLaunchSpec("C:\\Windows\\wsl.exe",
                                           ["--distribution", "개발 Ubuntu", "--cd", "~"],
                                           Path.GetTempPath());
        var firstRequest = new TerminalNewTabRequest(
            new TerminalTabRequestId(CreateGuid(501)), sourceReference, profile.ProfileId);

        var launched = await coordinator.LaunchProfileAsync(firstRequest, profile, _ => wslShell,
                                                            CancellationToken.None);
        var failingRequest = new TerminalNewTabRequest(
            new TerminalTabRequestId(CreateGuid(502)), sourceReference, profile.ProfileId);
        var failed = await coordinator.LaunchProfileAsync(failingRequest, profile, _ => wslShell,
                                                          CancellationToken.None);

        Assert.AreEqual(TerminalTabLaunchStatus.Started, launched.Status);
        Assert.IsNotNull(launched.Tab);
        Assert.AreNotEqual(source.SessionId, launched.Tab.SessionId);
        Assert.AreEqual(profile.ProfileId, launched.Tab.LaunchProfile?.ProfileId);
        Assert.AreEqual(TerminalTabLaunchStatus.Failed, failed.Status);
        Assert.AreEqual(TerminalSessionState.Running, FindTab(coordinator.Snapshot, source.SessionId).State);
        Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
        CollectionAssert.AreEqual(wslShell.Arguments.ToArray(), factory.StartRequests[1].Shell.Arguments.ToArray());
    }

    [TestMethod]
    public async Task DuplicateAsyncCopiesOnlyConfigurationAndRejectsRepeatedRequest()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var source = await coordinator.StartAsync(TestShell, TerminalShellKind.Pwsh, null, ShellResolver.Resolve,
                                                  80, 24, CancellationToken.None);
        Assert.IsTrue(coordinator.Rename(source.SessionId, "서버"));
        var sourceReference = FindOutputState(coordinator.NewOutputState, source.SessionId).Session;
        var request = new TerminalTabDuplicateRequest(new TerminalTabRequestId(CreateGuid(601)), sourceReference);

        var duplicated = await coordinator.DuplicateAsync(request, _ => TestShell, CancellationToken.None);
        var repeated = await coordinator.DuplicateAsync(request, _ => TestShell, CancellationToken.None);

        Assert.AreEqual(TerminalTabLaunchStatus.Started, duplicated.Status);
        Assert.IsNotNull(duplicated.Tab);
        Assert.AreEqual("서버 (2)", duplicated.Tab.Name);
        Assert.AreEqual(source.StartingDirectory, duplicated.Tab.StartingDirectory);
        Assert.AreEqual(source.LaunchProfile?.ProfileId, duplicated.Tab.LaunchProfile?.ProfileId);
        Assert.AreNotEqual(source.SessionId, duplicated.Tab.SessionId);
        Assert.AreNotEqual(source.ConfigurationId, duplicated.Tab.ConfigurationId);
        Assert.AreEqual(2, factory.Sessions.Count);
        Assert.AreEqual(TerminalTabLaunchStatus.DuplicateRequest, repeated.Status);
    }

    [TestMethod]
    public async Task ProfileRequestsAtCapacityAndAfterRestartRemovalOrDisposeNeverStartSession()
    {
        var profile = TerminalLaunchProfile.CreateBuiltIn(TerminalShellKind.Pwsh, "PowerShell 7");

        var capacityFactory = new FakeTerminalSessionFactory();
        await using var capacityCoordinator = CreateCoordinator(capacityFactory, maximumTabs: 1);
        var capacitySource = await capacityCoordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var capacityReference = FindOutputState(capacityCoordinator.NewOutputState, capacitySource.SessionId).Session;
        var capacityRequest = new TerminalNewTabRequest(new TerminalTabRequestId(CreateGuid(701)),
                                                        capacityReference, profile.ProfileId);
        var capacity = await capacityCoordinator.LaunchProfileAsync(capacityRequest, profile, _ => TestShell,
                                                                    CancellationToken.None);
        Assert.AreEqual(TerminalTabLaunchStatus.TabLimitReached, capacity.Status);
        Assert.AreEqual(1, capacityFactory.StartRequests.Count);

        var staleFactory = new FakeTerminalSessionFactory();
        var staleCoordinator = CreateCoordinator(staleFactory);
        var staleSource = await staleCoordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var staleReference = FindOutputState(staleCoordinator.NewOutputState, staleSource.SessionId).Session;
        await staleCoordinator.RestartAsync(staleSource.SessionId, CancellationToken.None);
        var restartedRequest = new TerminalTabDuplicateRequest(new TerminalTabRequestId(CreateGuid(702)),
                                                               staleReference);
        var restarted = await staleCoordinator.DuplicateAsync(restartedRequest, _ => TestShell,
                                                               CancellationToken.None);
        Assert.AreEqual(TerminalTabLaunchStatus.StaleSource, restarted.Status);

        var removable = await staleCoordinator.AddAsync(CancellationToken.None);
        var removedReference = FindOutputState(staleCoordinator.NewOutputState, removable.SessionId).Session;
        staleFactory.Sessions[2].RaiseExit(0);
        Assert.IsTrue(await staleCoordinator.CloseAsync(removable.SessionId, CancellationToken.None));
        var removedRequest = new TerminalTabDuplicateRequest(new TerminalTabRequestId(CreateGuid(703)),
                                                             removedReference);
        var removed = await staleCoordinator.DuplicateAsync(removedRequest, _ => TestShell, CancellationToken.None);
        Assert.AreEqual(TerminalTabLaunchStatus.StaleSource, removed.Status);

        var currentReference = FindOutputState(staleCoordinator.NewOutputState, staleSource.SessionId).Session;
        await staleCoordinator.DisposeAsync();
        var disposedRequest = new TerminalTabDuplicateRequest(new TerminalTabRequestId(CreateGuid(704)),
                                                              currentReference);
        var disposed = await staleCoordinator.DuplicateAsync(disposedRequest, _ => TestShell,
                                                              CancellationToken.None);
        Assert.AreEqual(TerminalTabLaunchStatus.Unavailable, disposed.Status);
        Assert.AreEqual(3, staleFactory.StartRequests.Count);
    }

    [TestMethod]
    public async Task DuplicateAsyncAtDefaultEightTabLimitDoesNotCreateNinthSession()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var source = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var sourceReference = FindOutputState(coordinator.NewOutputState, source.SessionId).Session;
        for (var index = 1; index < 8; index++)
        {
            _ = await coordinator.AddAsync(CancellationToken.None);
        }

        var request = new TerminalTabDuplicateRequest(new TerminalTabRequestId(CreateGuid(705)), sourceReference);
        var result = await coordinator.DuplicateAsync(request, _ => TestShell, CancellationToken.None);

        Assert.AreEqual(TerminalTabLaunchStatus.TabLimitReached, result.Status);
        Assert.HasCount(8, coordinator.Snapshot.Tabs);
        Assert.HasCount(8, factory.Sessions);
    }

    [TestMethod]
    public async Task DisposeAsyncMultipleBlockedSessionsStartsCleanupInParallelAndReturnsByDeadline()
    {
        var cleanupGates = new List<TaskCompletionSource>();
        var factory = new FakeTerminalSessionFactory(_ =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cleanupGates.Add(gate);

            return new FakeTerminalSession(gate);
        });
        var coordinator = CreateCoordinator(factory, sessionCloseTimeout: TimeSpan.FromMilliseconds(40),
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
        var cleanupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeTerminalSessionFactory(index => new FakeTerminalSession(index == 0 ? cleanupGate : null));
        var coordinator = CreateCoordinator(factory, sessionCloseTimeout: TimeSpan.FromMilliseconds(300),
                                            shutdownTimeout: TimeSpan.FromMilliseconds(40));
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        factory.Sessions[0].RaiseExit(0);
        var closeTask = coordinator.CloseAsync(first.SessionId, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await coordinator.DisposeAsync();
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.IsFalse(closeTask.IsCompleted);
        Assert.AreEqual(1, factory.Sessions[1].DisposeCount);

        cleanupGate.TrySetResult();
        var closed = await closeTask;
        Assert.IsTrue(closed);
    }

    [TestMethod]
    public async Task CommandSignalsProduceCorrelatedStartFinishAndCompletionForCurrentGeneration()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var expectedSession = FindOutputState(coordinator.NewOutputState, tab.SessionId).Session;
        var executionId = new TerminalCommandExecutionId(
            Guid.Parse("20000000-0000-0000-0000-000000000001"));
        var starts = new List<TerminalSessionCommandStarted>();
        var finishes = new List<TerminalSessionCommandFinished>();
        var completions = new List<TerminalCommandCompletion>();
        coordinator.CommandStarted += starts.Add;
        coordinator.CommandFinished += finishes.Add;
        coordinator.CommandCompleted += completions.Add;

        factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Started(executionId));
        factory.Sessions[0].RaiseOutput("prompt-like output must remain ordinary output");
        factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Finished(executionId, 7));
        factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Finished(executionId, 7));

        Assert.HasCount(1, starts);
        Assert.AreEqual(expectedSession, starts[0].Session);
        Assert.AreEqual(executionId, starts[0].ExecutionId);
        Assert.HasCount(1, finishes);
        Assert.AreEqual(expectedSession, finishes[0].Session);
        Assert.AreEqual(7, finishes[0].ExitCode);
        Assert.HasCount(1, completions);
        Assert.AreEqual(expectedSession, completions[0].Session);
        Assert.AreEqual(executionId, completions[0].ExecutionId);
        Assert.AreEqual(7, completions[0].ExitResult.ExitCode);
    }

    [TestMethod]
    public async Task IsCurrentSessionRejectsRestartedRemovedAndDisposedLifetimes()
    {
        var factory = new FakeTerminalSessionFactory();
        var coordinator = CreateCoordinator(factory);
        var first = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var second = await coordinator.AddAsync(CancellationToken.None);
        var firstLifetime = FindOutputState(coordinator.NewOutputState, first.SessionId).Session;
        var secondLifetime = FindOutputState(coordinator.NewOutputState, second.SessionId).Session;

        Assert.IsTrue(coordinator.IsCurrentSession(firstLifetime));
        Assert.IsTrue(coordinator.IsCurrentSession(secondLifetime));

        await coordinator.RestartAsync(first.SessionId, CancellationToken.None);
        var restartedLifetime = FindOutputState(coordinator.NewOutputState, first.SessionId).Session;

        Assert.IsFalse(coordinator.IsCurrentSession(firstLifetime));
        Assert.IsTrue(coordinator.IsCurrentSession(restartedLifetime));

        factory.Sessions[1].RaiseExit(0);
        Assert.IsTrue(await coordinator.CloseAsync(second.SessionId, CancellationToken.None));
        Assert.IsFalse(coordinator.IsCurrentSession(secondLifetime));

        await coordinator.DisposeAsync();
        Assert.IsFalse(coordinator.IsCurrentSession(restartedLifetime));
    }

    [TestMethod]
    public async Task CmdUnknownExitCodeRemainsObservableWithoutInventingCompletion()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        _ = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var finishes = new List<TerminalSessionCommandFinished>();
        var completions = new List<TerminalCommandCompletion>();
        coordinator.CommandFinished += finishes.Add;
        coordinator.CommandCompleted += completions.Add;

        for (var index = 1; index <= 2; index++)
        {
            var executionId = new TerminalCommandExecutionId(CreateGuid(100 + index));
            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Started(executionId));
            factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Finished(executionId, null));
        }

        Assert.HasCount(2, finishes);
        Assert.IsTrue(finishes.All(finish => finish.ExitCode is null));
        Assert.IsEmpty(completions);
    }

    [TestMethod]
    public async Task RestartAndShellExitRejectLateCommandSignalsFromPreviousLifetime()
    {
        var factory = new FakeTerminalSessionFactory();
        await using var coordinator = CreateCoordinator(factory);
        var tab = await coordinator.StartAsync(TestShell, 80, 24, CancellationToken.None);
        var oldExecutionId = new TerminalCommandExecutionId(CreateGuid(201));
        var newExecutionId = new TerminalCommandExecutionId(CreateGuid(202));
        var finishes = new List<TerminalSessionCommandFinished>();
        coordinator.CommandFinished += finishes.Add;

        factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Started(oldExecutionId));
        await coordinator.RestartAsync(tab.SessionId, CancellationToken.None);
        factory.Sessions[0].RaiseCommandSignal(TerminalSessionCommandSignal.Finished(oldExecutionId, 0));
        factory.Sessions[1].RaiseCommandSignal(TerminalSessionCommandSignal.Started(newExecutionId));
        factory.Sessions[1].RaiseExit(1);
        factory.Sessions[1].RaiseCommandSignal(TerminalSessionCommandSignal.Finished(newExecutionId, 0));

        Assert.IsEmpty(finishes);
    }

    private static TerminalSessionCoordinator CreateCoordinator(FakeTerminalSessionFactory factory,
                                                                int maximumTabs = TerminalTabRegistry.DefaultMaximumTabs,
                                                                TimeSpan? sessionCloseTimeout = null,
                                                                TimeSpan? shutdownTimeout = null,
                                                                Func<DateTimeOffset>? currentTime = null)
    {
        var nextIdentifier = 0;
        return new TerminalSessionCoordinator(factory, new NullDiagnosticLog(),
                                              () => new TerminalSessionId(CreateGuid(++nextIdentifier)), maximumTabs,
                                              sessionCloseTimeout, shutdownTimeout,
                                              currentTime: currentTime);
    }

    private static TerminalTab FindTab(TerminalWorkspaceSnapshot snapshot, TerminalSessionId sessionId)
    {
        return snapshot.Tabs.Single(tab => tab.SessionId == sessionId);
    }

    private static TerminalSessionNewOutputState FindOutputState(TerminalNewOutputStateSnapshot snapshot,
                                                                 TerminalSessionId sessionId)
    {
        return snapshot.States.Single(state => state.Session.SessionId == sessionId.Value);
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

        internal int? FailureStartNumber { get; set; }

        internal List<FakeTerminalSession> Sessions { get; } = [];

        internal List<StartRequest> StartRequests { get; } = [];

        public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
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

        public event Action<TerminalSessionCommandSignal>? CommandLifecycleChanged;

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

        internal void RaiseCommandSignal(TerminalSessionCommandSignal signal)
        {
            CommandLifecycleChanged?.Invoke(signal);
        }
    }

    private sealed record StartRequest(ShellLaunchSpec Shell, int Columns, int Rows);

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
