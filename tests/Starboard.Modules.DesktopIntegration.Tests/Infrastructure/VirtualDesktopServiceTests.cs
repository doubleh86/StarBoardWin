using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

#pragma warning disable CA1707 // Test names follow the repository's Scenario_ExpectedResult convention.

[TestClass]
public sealed class VirtualDesktopServiceTests
{
    [TestMethod]
    public void CaptureWindowState_SupportedApi_ReturnsCurrentDesktopAndIdentifier()
    {
        var desktopId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var manager = new FakeVirtualDesktopManagerApi
        {
            IsOnCurrentDesktop = true,
            DesktopId = desktopId,
        };
        using var service = new SupportedVirtualDesktopService(manager);

        var state = service.CaptureWindowState(new nint(42));

        Assert.AreEqual(VirtualDesktopWindowStateStatus.Available, state.Status);
        Assert.AreEqual(true, state.IsOnCurrentDesktop);
        Assert.AreEqual(desktopId, state.DesktopId);
        Assert.AreEqual(new nint(42), manager.LastStateWindowHandle);
    }

    [TestMethod]
    public void MoveWindowToDesktop_SupportedApi_MovesRequestedWindow()
    {
        var desktopId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var manager = new FakeVirtualDesktopManagerApi();
        using var service = new SupportedVirtualDesktopService(manager);

        var result = service.MoveWindowToDesktop(new nint(84), desktopId);

        Assert.AreEqual(VirtualDesktopMoveStatus.Moved, result);
        Assert.AreEqual(new nint(84), manager.LastMovedWindowHandle);
        Assert.AreEqual(desktopId, manager.LastMovedDesktopId);
    }

    [TestMethod]
    public void Create_ApiInitializationFails_ReturnsNoOpFallback()
    {
        var diagnosticLog = new RecordingDiagnosticLog();

        using var service = VirtualDesktopServiceFactory.Create(
            diagnosticLog, () => throw new InvalidOperationException("VirtualDesktopManager is unavailable."));

        Assert.IsFalse(service.Capabilities.CanQueryWindowState);
        Assert.IsFalse(service.Capabilities.CanMoveWindow);
        Assert.IsFalse(service.Capabilities.CanPinWindowToAllDesktops);
        Assert.AreEqual(VirtualDesktopWindowStateStatus.Unavailable,
                        service.CaptureWindowState(new nint(1)).Status);
        Assert.AreEqual(VirtualDesktopMoveStatus.Unsupported,
                        service.MoveWindowToDesktop(new nint(1), Guid.NewGuid()));
        Assert.AreEqual("InitializeVirtualDesktop", diagnosticLog.Entries.Single().Operation);
    }

    [TestMethod]
    public void CaptureWindowState_ApiCallFails_DisposesAdapterAndPermanentlyFallsBack()
    {
        var manager = new FakeVirtualDesktopManagerApi
        {
            CaptureException = new InvalidOperationException("Shell disconnected."),
        };
        var diagnosticLog = new RecordingDiagnosticLog();
        using var service = new FailoverVirtualDesktopService(new SupportedVirtualDesktopService(manager),
                                                              diagnosticLog);

        var firstState = service.CaptureWindowState(new nint(21));
        var secondState = service.CaptureWindowState(new nint(21));

        Assert.AreEqual(VirtualDesktopWindowStateStatus.Unavailable, firstState.Status);
        Assert.AreEqual(VirtualDesktopWindowStateStatus.Unavailable, secondState.Status);
        Assert.IsFalse(service.Capabilities.CanQueryWindowState);
        Assert.AreEqual(1, manager.CaptureCallCount);
        Assert.AreEqual(1, manager.DisposeCallCount);
        Assert.AreEqual("CaptureVirtualDesktopState", diagnosticLog.Entries.Single().Operation);
    }

    [TestMethod]
    public void MoveWindowToDesktop_ApiCallFails_ReportsFailureThenUnsupportedFallback()
    {
        var manager = new FakeVirtualDesktopManagerApi
        {
            MoveException = new InvalidOperationException("Shell disconnected."),
        };
        var diagnosticLog = new RecordingDiagnosticLog();
        using var service = new FailoverVirtualDesktopService(new SupportedVirtualDesktopService(manager),
                                                              diagnosticLog);
        var desktopId = Guid.Parse("30000000-0000-0000-0000-000000000003");

        var firstResult = service.MoveWindowToDesktop(new nint(63), desktopId);
        var secondResult = service.MoveWindowToDesktop(new nint(63), desktopId);

        Assert.AreEqual(VirtualDesktopMoveStatus.Failed, firstResult);
        Assert.AreEqual(VirtualDesktopMoveStatus.Unsupported, secondResult);
        Assert.IsFalse(service.Capabilities.CanMoveWindow);
        Assert.AreEqual(1, manager.MoveCallCount);
        Assert.AreEqual(1, manager.DisposeCallCount);
        Assert.AreEqual("MovePanelToVirtualDesktop", diagnosticLog.Entries.Single().Operation);
    }

    [TestMethod]
    public void Dispose_SupportedAdapter_ReleasesManagerExactlyOnce()
    {
        var manager = new FakeVirtualDesktopManagerApi();
        var service = new SupportedVirtualDesktopService(manager);

        service.Dispose();
        service.Dispose();

        Assert.AreEqual(1, manager.DisposeCallCount);
    }

    private sealed class FakeVirtualDesktopManagerApi : IVirtualDesktopManagerApi
    {
        public bool IsOnCurrentDesktop { get; init; }

        public Guid DesktopId { get; init; }

        public Exception? CaptureException { get; init; }

        public Exception? MoveException { get; init; }

        public nint LastStateWindowHandle { get; private set; }

        public nint LastMovedWindowHandle { get; private set; }

        public Guid LastMovedDesktopId { get; private set; }

        public int CaptureCallCount { get; private set; }

        public int MoveCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public bool IsWindowOnCurrentVirtualDesktop(nint windowHandle)
        {
            LastStateWindowHandle = windowHandle;
            CaptureCallCount++;
            if (CaptureException is not null)
            {
                throw CaptureException;
            }

            return IsOnCurrentDesktop;
        }

        public Guid GetWindowDesktopId(nint windowHandle)
        {
            LastStateWindowHandle = windowHandle;

            return DesktopId;
        }

        public void MoveWindowToDesktop(nint windowHandle, Guid desktopId)
        {
            LastMovedWindowHandle = windowHandle;
            LastMovedDesktopId = desktopId;
            MoveCallCount++;
            if (MoveException is not null)
            {
                throw MoveException;
            }
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class RecordingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticLogEntry> Entries { get; } = [];

        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
            Entries.Add(new DiagnosticLogEntry(level, subsystem, operation, message, exception));
        }
    }

    private sealed record DiagnosticLogEntry(DiagnosticLevel Level, string Subsystem, string Operation, string Message,
                                             Exception? Exception);
}

#pragma warning restore CA1707
