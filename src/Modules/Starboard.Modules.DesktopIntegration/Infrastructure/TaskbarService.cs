using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed record DesktopGeometrySnapshot(
    TaskbarSnapshot Taskbar,
    MonitorSnapshot Monitor,
    TaskbarPresence TaskbarPresence,
    DisplayTrackingState TrackingState,
    Exception? GeometryCaptureFailure,
    Exception? DpiCaptureFailure);

internal sealed class TaskbarService
{
    private const int _MaximumConcealedThickness = 2;

    private readonly IDesktopNativeApi _nativeApi;
    private readonly MonitorService _monitorService;

    internal TaskbarService(IDesktopNativeApi nativeApi)
    {
        _nativeApi = nativeApi;
        _monitorService = new MonitorService(nativeApi);
    }

    internal static TaskbarSnapshot Capture(nint windowHandle)
    {
        var service = new TaskbarService(new DesktopNativeApi());
        return service.CaptureLatest(windowHandle).Taskbar;
    }

    internal DesktopGeometrySnapshot CaptureLatest(nint panelWindowHandle)
    {
        // Both queries are intentionally repeated for every reconciliation. A display
        // handle and its rectangles stop being valid when that display is disconnected.
        var connectedMonitors = _monitorService.CaptureConnected();
        NativeTaskbarSnapshot nativeTaskbar;
        NativeMonitorSnapshot monitor;
        Exception? geometryCaptureFailure = null;
        var usedMonitorFallback = false;

        try
        {
            nativeTaskbar = _nativeApi.GetTaskbar();
            monitor = _monitorService.SelectCurrentTaskbarMonitor(
                nativeTaskbar.Bounds,
                connectedMonitors,
                out usedMonitorFallback);
        }
        catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
        {
            // Explorer can temporarily remove the taskbar while restarting. Derive a
            // conservative snapshot only from the current monitor enumeration; never
            // reuse a rectangle or HMONITOR captured by an earlier reconciliation.
            geometryCaptureFailure = exception;
            usedMonitorFallback = true;
            monitor = MonitorService.SelectFallbackMonitor(connectedMonitors);
            var fallbackEdge = InferTaskbarEdge(monitor);
            nativeTaskbar = new NativeTaskbarSnapshot(
                fallbackEdge,
                CalculateTaskbarBounds(monitor, fallbackEdge),
                false);
        }

        var dpi = _monitorService.CaptureDpi(
            monitor,
            panelWindowHandle,
            out var dpiCaptureFailure);
        var edge = usedMonitorFallback == true
            ? InferTaskbarEdge(monitor)
            : nativeTaskbar.Edge;
        var taskbarBounds = usedMonitorFallback == true
            ? CalculateTaskbarBounds(monitor, edge)
            : nativeTaskbar.Bounds;
        var snapshot = new TaskbarSnapshot(
            edge,
            taskbarBounds,
            monitor.Bounds,
            monitor.WorkArea,
            nativeTaskbar.IsAutoHideEnabled,
            edge is TaskbarEdge.Left or TaskbarEdge.Right ? dpi.X : dpi.Y);
        var monitorSnapshot = new MonitorSnapshot(
            monitor.Handle,
            monitor.Bounds,
            monitor.WorkArea,
            dpi);
        var isFallback = usedMonitorFallback == true ||
            geometryCaptureFailure is not null ||
            dpiCaptureFailure is not null;
        var taskbarPresence = CaptureTaskbarPresence(
            nativeTaskbar,
            monitor,
            usedMonitorFallback);

        return new DesktopGeometrySnapshot(
            snapshot,
            monitorSnapshot,
            taskbarPresence,
            isFallback == true
                ? DisplayTrackingState.Fallback
                : DisplayTrackingState.Tracked,
            geometryCaptureFailure,
            dpiCaptureFailure);
    }

    private static TaskbarPresence CaptureTaskbarPresence(
        NativeTaskbarSnapshot taskbar,
        NativeMonitorSnapshot monitor,
        bool usedMonitorFallback)
    {
        if (usedMonitorFallback == true)
        {
            return TaskbarPresence.Unknown;
        }

        if (taskbar.IsAutoHideEnabled == false)
        {
            return TaskbarPresence.Visible;
        }

        if (taskbar.CurrentWindowBounds is not PixelRect currentBounds ||
            currentBounds.IsEmpty == true)
        {
            return TaskbarPresence.Unknown;
        }

        var expectedThickness = taskbar.Edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? taskbar.Bounds.Width
            : taskbar.Bounds.Height;
        if (expectedThickness <= 0)
        {
            return TaskbarPresence.Unknown;
        }

        var parallelOverlap = taskbar.Edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? CalculateOverlap(
                currentBounds.Top,
                currentBounds.Bottom,
                monitor.Bounds.Top,
                monitor.Bounds.Bottom)
            : CalculateOverlap(
                currentBounds.Left,
                currentBounds.Right,
                monitor.Bounds.Left,
                monitor.Bounds.Right);
        if (parallelOverlap <= 0)
        {
            return TaskbarPresence.Unknown;
        }

        var visibleThickness = taskbar.Edge switch
        {
            TaskbarEdge.Left => currentBounds.Right - monitor.Bounds.Left,
            TaskbarEdge.Top => currentBounds.Bottom - monitor.Bounds.Top,
            TaskbarEdge.Right => monitor.Bounds.Right - currentBounds.Left,
            _ => monitor.Bounds.Bottom - currentBounds.Top,
        };
        visibleThickness = Math.Clamp(visibleThickness, 0, expectedThickness);

        if (visibleThickness >= expectedThickness)
        {
            return TaskbarPresence.Visible;
        }

        if (visibleThickness <= _MaximumConcealedThickness)
        {
            return TaskbarPresence.Concealed;
        }

        // An intermediate rectangle is most likely an auto-hide animation frame.
        // Unknown keeps the policy from moving the panel during that transition.
        return TaskbarPresence.Unknown;
    }

    private static int CalculateOverlap(
        int firstStart,
        int firstEnd,
        int secondStart,
        int secondEnd)
    {
        return Math.Max(
            0,
            Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
    }

    private static TaskbarEdge InferTaskbarEdge(NativeMonitorSnapshot monitor)
    {
        var leftInset = Math.Max(0, monitor.WorkArea.Left - monitor.Bounds.Left);
        var topInset = Math.Max(0, monitor.WorkArea.Top - monitor.Bounds.Top);
        var rightInset = Math.Max(0, monitor.Bounds.Right - monitor.WorkArea.Right);
        var bottomInset = Math.Max(0, monitor.Bounds.Bottom - monitor.WorkArea.Bottom);
        var largestInset = Math.Max(
            Math.Max(leftInset, topInset),
            Math.Max(rightInset, bottomInset));

        if (largestInset == leftInset && leftInset > 0)
        {
            return TaskbarEdge.Left;
        }

        if (largestInset == topInset && topInset > 0)
        {
            return TaskbarEdge.Top;
        }

        if (largestInset == rightInset && rightInset > 0)
        {
            return TaskbarEdge.Right;
        }

        return TaskbarEdge.Bottom;
    }

    private static PixelRect CalculateTaskbarBounds(
        NativeMonitorSnapshot monitor,
        TaskbarEdge edge)
    {
        return edge switch
        {
            TaskbarEdge.Left => new PixelRect(
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                monitor.WorkArea.Left,
                monitor.Bounds.Bottom),
            TaskbarEdge.Top => new PixelRect(
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                monitor.Bounds.Right,
                monitor.WorkArea.Top),
            TaskbarEdge.Right => new PixelRect(
                monitor.WorkArea.Right,
                monitor.Bounds.Top,
                monitor.Bounds.Right,
                monitor.Bounds.Bottom),
            _ => new PixelRect(
                monitor.Bounds.Left,
                monitor.WorkArea.Bottom,
                monitor.Bounds.Right,
                monitor.Bounds.Bottom),
        };
    }

    private static bool IsRecoverablePlatformFailure(Exception exception)
    {
        return exception is InvalidOperationException or
            System.Runtime.InteropServices.ExternalException or
            ArgumentException or
            DllNotFoundException or
            EntryPointNotFoundException;
    }
}
