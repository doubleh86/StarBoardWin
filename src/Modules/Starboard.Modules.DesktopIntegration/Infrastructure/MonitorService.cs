using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed class MonitorService
{
    private readonly IDesktopNativeApi _nativeApi;

    internal MonitorService(IDesktopNativeApi nativeApi)
    {
        _nativeApi = nativeApi;
    }

    internal IReadOnlyList<NativeMonitorSnapshot> CaptureConnected()
    {
        var monitors = _nativeApi.GetConnectedMonitors();
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Windows did not report any connected monitors.");
        }

        return monitors;
    }

    internal NativeMonitorSnapshot SelectCurrentTaskbarMonitor(PixelRect taskbarBounds,
                                                               IReadOnlyList<NativeMonitorSnapshot> connectedMonitors,
                                                               out bool usedFallback)
    {
        var taskbarMonitorHandle = _nativeApi.GetMonitorForRectangle(taskbarBounds);
        foreach (var monitor in connectedMonitors)
        {
            if (taskbarMonitorHandle != 0 && monitor.Handle == taskbarMonitorHandle)
            {
                usedFallback = false;
                return monitor;
            }
        }

        usedFallback = true;

        return SelectFallbackMonitor(connectedMonitors);
    }

    internal static NativeMonitorSnapshot SelectFallbackMonitor(IReadOnlyList<NativeMonitorSnapshot> connectedMonitors)
    {
        foreach (var monitor in connectedMonitors)
        {
            if (monitor.IsPrimary == true)
            {
                return monitor;
            }
        }

        return connectedMonitors[0];
    }

    internal DisplayDpi CaptureDpi(NativeMonitorSnapshot monitor, nint panelWindowHandle,
                                   out Exception? monitorDpiFailure)
    {
        try
        {
            var dpi = _nativeApi.GetMonitorDpi(monitor.Handle);
            if (dpi.X != 0 && dpi.Y != 0)
            {
                monitorDpiFailure = null;
                return dpi;
            }

            monitorDpiFailure = new InvalidOperationException("Windows returned a zero DPI for the selected monitor.");
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or ArgumentException or DllNotFoundException or EntryPointNotFoundException)
        {
            monitorDpiFailure = exception;
        }

        try
        {
            var windowDpi = _nativeApi.GetWindowDpi(panelWindowHandle);
            if (windowDpi != 0)
            {
                return new DisplayDpi(windowDpi, windowDpi);
            }
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or ArgumentException or DllNotFoundException or EntryPointNotFoundException)
        {
            monitorDpiFailure = new AggregateException("Both monitor and window DPI queries failed.", monitorDpiFailure,
                                                       exception);
        }

        return new DisplayDpi(DisplayDpi.Default, DisplayDpi.Default);
    }
}
