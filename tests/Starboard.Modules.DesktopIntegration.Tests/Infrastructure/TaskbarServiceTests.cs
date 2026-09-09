using System.ComponentModel;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

[TestClass]
public sealed class TaskbarServiceTests
{
    [TestMethod]
    public void CaptureLatestAfterMonitorDisconnectUsesLivePrimaryMonitorInsteadOfStaleRectangle()
    {
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
                CreateMonitor(2, new PixelRect(1920, 0, 3840, 1080), false),
            ],
            Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Bottom, new PixelRect(1920, 1040, 3840, 1080), false),
            RectangleMonitorHandle = 2,
        };
        var service = new TaskbarService(nativeApi);

        var beforeDisconnect = service.CaptureLatest(new nint(71));
        nativeApi.ConnectedMonitors =
        [
            CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
        ];

        var afterDisconnect = service.CaptureLatest(new nint(71));

        Assert.AreEqual(new nint(2), beforeDisconnect.Monitor.Handle);
        Assert.AreEqual(new nint(1), afterDisconnect.Monitor.Handle);
        Assert.AreEqual(new PixelRect(0, 0, 1920, 1040), afterDisconnect.Taskbar.WorkArea);
        Assert.AreEqual(DisplayTrackingState.Fallback, afterDisconnect.TrackingState);
        Assert.AreEqual(TaskbarPresence.Unknown, afterDisconnect.TaskbarPresence);
        Assert.AreEqual(2, nativeApi.ConnectedMonitorCaptureCount);
    }

    [TestMethod]
    public void CaptureLatestAfterTaskbarMoveRequeriesMonitorGeometryAndDpi()
    {
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            ],
            Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080), false),
            RectangleMonitorHandle = 1,
        };
        var service = new TaskbarService(nativeApi);

        _ = service.CaptureLatest(new nint(73));
        nativeApi.ConnectedMonitors =
        [
            CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            new NativeMonitorSnapshot(new nint(3), new PixelRect(-2560, 0, 0, 1440), new PixelRect(-2500, 0, 0, 1440),
                                      false),
        ];
        nativeApi.Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Left, new PixelRect(-2560, 0, -2500, 1440), true);
        nativeApi.RectangleMonitorHandle = 3;
        nativeApi.MonitorDpi = new DisplayDpi(144, 144);

        var result = service.CaptureLatest(new nint(73));

        Assert.AreEqual(new nint(3), result.Monitor.Handle);
        Assert.AreEqual(new PixelRect(-2560, 0, 0, 1440), result.Monitor.Bounds);
        Assert.AreEqual(TaskbarEdge.Left, result.Taskbar.Edge);
        Assert.AreEqual(144u, result.Taskbar.Dpi);
        Assert.AreEqual(TaskbarPresence.Unknown, result.TaskbarPresence);
        Assert.AreEqual(DisplayTrackingState.Tracked, result.TrackingState);
    }

    [TestMethod]
    public void CaptureLatestWithRevealedAutoHideTaskbarReportsVisible()
    {
        var taskbarBounds = new PixelRect(0, 1040, 1920, 1080);
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            ],
            Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Bottom, taskbarBounds, true, taskbarBounds),
            RectangleMonitorHandle = 1,
        };
        var service = new TaskbarService(nativeApi);

        var result = service.CaptureLatest(new nint(75));

        Assert.AreEqual(TaskbarPresence.Visible, result.TaskbarPresence);
        Assert.AreEqual(DisplayTrackingState.Tracked, result.TrackingState);
    }

    [TestMethod]
    [DataRow(TaskbarEdge.Left)]
    [DataRow(TaskbarEdge.Top)]
    [DataRow(TaskbarEdge.Right)]
    [DataRow(TaskbarEdge.Bottom)]
    public void CaptureLatestWithConcealedAutoHideTaskbarOnEachEdgeReportsConcealed(TaskbarEdge edge)
    {
        var taskbarBounds = edge switch
        {
            TaskbarEdge.Left => new PixelRect(0, 0, 40, 1080),
            TaskbarEdge.Top => new PixelRect(0, 0, 1920, 40),
            TaskbarEdge.Right => new PixelRect(1880, 0, 1920, 1080),
            _ => new PixelRect(0, 1040, 1920, 1080),
        };
        var concealedBounds = edge switch
        {
            TaskbarEdge.Left => new PixelRect(-38, 0, 2, 1080),
            TaskbarEdge.Top => new PixelRect(0, -38, 1920, 2),
            TaskbarEdge.Right => new PixelRect(1918, 0, 1958, 1080),
            _ => new PixelRect(0, 1078, 1920, 1118),
        };
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            ],
            Taskbar = new NativeTaskbarSnapshot(edge, taskbarBounds, true, concealedBounds),
            RectangleMonitorHandle = 1,
        };
        var service = new TaskbarService(nativeApi);

        var result = service.CaptureLatest(new nint(77));

        Assert.AreEqual(TaskbarPresence.Concealed, result.TaskbarPresence);
        Assert.AreEqual(DisplayTrackingState.Tracked, result.TrackingState);
    }

    [TestMethod]
    public void CaptureLatestDuringAutoHideAnimationReportsUnknown()
    {
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            ],
            Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080), true,
                                                new PixelRect(0, 1060, 1920, 1100)),
            RectangleMonitorHandle = 1,
        };
        var service = new TaskbarService(nativeApi);

        var result = service.CaptureLatest(new nint(78));

        Assert.AreEqual(TaskbarPresence.Unknown, result.TaskbarPresence);
        Assert.AreEqual(DisplayTrackingState.Tracked, result.TrackingState);
    }

    [TestMethod]
    public void CaptureLatestWhenMonitorDpiFailsPreservesFailureAndUsesWindowDpiFallback()
    {
        var dpiFailure = new Win32Exception(87, "DPI query failed.");
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            ],
            Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080), false),
            RectangleMonitorHandle = 1,
            MonitorDpiException = dpiFailure,
            WindowDpi = 120,
        };
        var service = new TaskbarService(nativeApi);

        var result = service.CaptureLatest(new nint(79));

        Assert.AreEqual(new DisplayDpi(120, 120), result.Monitor.Dpi);
        Assert.AreSame(dpiFailure, result.DpiCaptureFailure);
        Assert.AreEqual(DisplayTrackingState.Fallback, result.TrackingState);
    }

    [TestMethod]
    public void CaptureLatestDuringExplorerRestartUsesOnlyLivePrimaryGeometry()
    {
        var taskbarFailure = new InvalidOperationException("Explorer is recreating the taskbar.");
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                new NativeMonitorSnapshot(new nint(5), new PixelRect(-1920, 0, 0, 1080),
                                          new PixelRect(-1920, 0, 0, 1040), true),
            ],
            TaskbarException = taskbarFailure,
        };
        var service = new TaskbarService(nativeApi);

        var result = service.CaptureLatest(new nint(83));

        Assert.AreEqual(new nint(5), result.Monitor.Handle);
        Assert.AreEqual(new PixelRect(-1920, 1040, 0, 1080), result.Taskbar.TaskbarBounds);
        Assert.AreEqual(new PixelRect(-1920, 0, 0, 1040), result.Taskbar.WorkArea);
        Assert.AreEqual(TaskbarPresence.Unknown, result.TaskbarPresence);
        Assert.AreEqual(DisplayTrackingState.Fallback, result.TrackingState);
        Assert.AreSame(taskbarFailure, result.GeometryCaptureFailure);
    }

    [TestMethod]
    public void CaptureLatestWhenBothDpiQueriesFailPreservesBothNativeFailures()
    {
        var monitorFailure = new Win32Exception(87, "Monitor DPI query failed.");
        var windowFailure = new Win32Exception(1400, "Window DPI query failed.");
        var nativeApi = new FakeDesktopNativeApi
        {
            ConnectedMonitors =
            [
                CreateMonitor(1, new PixelRect(0, 0, 1920, 1080), true),
            ],
            Taskbar = new NativeTaskbarSnapshot(TaskbarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080), false),
            RectangleMonitorHandle = 1,
            MonitorDpiException = monitorFailure,
            WindowDpiException = windowFailure,
        };
        var service = new TaskbarService(nativeApi);

        var result = service.CaptureLatest(new nint(89));

        Assert.AreEqual(new DisplayDpi(96, 96), result.Monitor.Dpi);
        var aggregateFailure = Assert.IsInstanceOfType<AggregateException>(result.DpiCaptureFailure);
        CollectionAssert.AreEqual(new Exception[] { monitorFailure, windowFailure },
                                  aggregateFailure.InnerExceptions.ToArray());
        Assert.AreEqual(DisplayTrackingState.Fallback, result.TrackingState);
    }

    private static NativeMonitorSnapshot CreateMonitor(int handle, PixelRect bounds, bool isPrimary)
    {
        return new NativeMonitorSnapshot(new nint(handle), bounds,
                                         new PixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - 40),
                                         isPrimary);
    }
}
