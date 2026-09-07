using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

internal sealed class FakeDesktopNativeApi : IDesktopNativeApi
{
    internal IReadOnlyList<NativeMonitorSnapshot> ConnectedMonitors { get; set; } = [];

    internal NativeTaskbarSnapshot Taskbar { get; set; }

    internal nint RectangleMonitorHandle { get; set; }

    internal DisplayDpi MonitorDpi { get; set; } = new(96, 96);

    internal Exception? MonitorDpiException { get; set; }

    internal uint WindowDpi { get; set; } = 96;

    internal Exception? WindowDpiException { get; set; }

    internal Exception? TaskbarException { get; set; }

    internal nint ForegroundWindowHandle { get; set; }

    internal nint ShellWindowHandle { get; set; }

    internal bool WindowVisible { get; set; } = true;

    internal bool WindowMinimized { get; set; }

    internal bool WindowCloaked { get; set; }

    internal PixelRect WindowBounds { get; set; }

    internal Exception? WindowBoundsException { get; set; }

    internal nint WindowMonitorHandle { get; set; }

    internal int ConnectedMonitorCaptureCount { get; private set; }

    public IReadOnlyList<NativeMonitorSnapshot> GetConnectedMonitors()
    {
        ConnectedMonitorCaptureCount++;
        return ConnectedMonitors;
    }

    public NativeTaskbarSnapshot GetTaskbar()
    {
        if (TaskbarException is not null)
        {
            throw TaskbarException;
        }

        return Taskbar;
    }

    public nint GetMonitorForRectangle(PixelRect rectangle)
    {
        _ = rectangle;
        return RectangleMonitorHandle;
    }

    public DisplayDpi GetMonitorDpi(nint monitorHandle)
    {
        _ = monitorHandle;
        if (MonitorDpiException is not null)
        {
            throw MonitorDpiException;
        }

        return MonitorDpi;
    }

    public uint GetWindowDpi(nint windowHandle)
    {
        _ = windowHandle;
        if (WindowDpiException is not null)
        {
            throw WindowDpiException;
        }

        return WindowDpi;
    }

    public nint GetForegroundWindow()
    {
        return ForegroundWindowHandle;
    }

    public nint GetShellWindow()
    {
        return ShellWindowHandle;
    }

    public bool IsWindowVisible(nint windowHandle)
    {
        _ = windowHandle;
        return WindowVisible;
    }

    public bool IsWindowMinimized(nint windowHandle)
    {
        _ = windowHandle;
        return WindowMinimized;
    }

    public bool IsWindowCloaked(nint windowHandle)
    {
        _ = windowHandle;
        return WindowCloaked;
    }

    public PixelRect GetWindowBounds(nint windowHandle)
    {
        _ = windowHandle;
        if (WindowBoundsException is not null)
        {
            throw WindowBoundsException;
        }

        return WindowBounds;
    }

    public nint GetMonitorForWindow(nint windowHandle)
    {
        _ = windowHandle;
        return WindowMonitorHandle;
    }
}
