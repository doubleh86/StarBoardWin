using System.ComponentModel;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal readonly record struct NativeMonitorSnapshot(
    nint Handle,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary);

internal readonly record struct NativeTaskbarSnapshot(
    TaskbarEdge Edge,
    PixelRect Bounds,
    bool IsAutoHideEnabled,
    PixelRect? CurrentWindowBounds = null);

internal interface IDesktopNativeApi
{
    IReadOnlyList<NativeMonitorSnapshot> GetConnectedMonitors();

    NativeTaskbarSnapshot GetTaskbar();

    nint GetMonitorForRectangle(PixelRect rectangle);

    DisplayDpi GetMonitorDpi(nint monitorHandle);

    uint GetWindowDpi(nint windowHandle);

    nint GetForegroundWindow();

    nint GetShellWindow();

    bool IsWindowVisible(nint windowHandle);

    bool IsWindowMinimized(nint windowHandle);

    bool IsWindowCloaked(nint windowHandle);

    PixelRect GetWindowBounds(nint windowHandle);

    nint GetMonitorForWindow(nint windowHandle);
}

internal sealed class DesktopNativeApi : IDesktopNativeApi
{
    public IReadOnlyList<NativeMonitorSnapshot> GetConnectedMonitors()
    {
        var monitors = new List<NativeMonitorSnapshot>();
        Win32Exception? callbackFailure = null;
        MonitorEnumerationCallback callback = (monitor, _, _, _) =>
        {
            var monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>(),
            };

            if (NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo) == false)
            {
                callbackFailure = new Win32Exception(Marshal.GetLastWin32Error());
                return false;
            }

            monitors.Add(new NativeMonitorSnapshot(
                monitor,
                ToPixelRect(monitorInfo.Monitor),
                ToPixelRect(monitorInfo.WorkArea),
                (monitorInfo.Flags & NativeMethods.MonitorInfoPrimary) != 0));
            return true;
        };

        if (NativeMethods.EnumDisplayMonitors(0, 0, callback, 0) == false)
        {
            throw callbackFailure ?? new Win32Exception(Marshal.GetLastWin32Error());
        }

        return monitors;
    }

    public NativeTaskbarSnapshot GetTaskbar()
    {
        var positionData = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(),
        };

        if (NativeMethods.SHAppBarMessage(
            NativeMethods.AppBarGetTaskbarPosition,
            ref positionData) == 0)
        {
            throw new InvalidOperationException(
                "Windows did not return the current system taskbar position.");
        }

        var stateData = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(),
        };
        var state = NativeMethods.SHAppBarMessage(
            NativeMethods.AppBarGetState,
            ref stateData);
        var isAutoHideEnabled = (state & NativeMethods.AppBarStateAutoHide) != 0;
        var currentWindowBounds = isAutoHideEnabled == true
            ? TryGetAutoHideTaskbarWindowBounds(positionData.Edge)
            : null;

        return new NativeTaskbarSnapshot(
            ToTaskbarEdge(positionData.Edge),
            ToPixelRect(positionData.Rectangle),
            isAutoHideEnabled,
            currentWindowBounds);
    }

    public nint GetMonitorForRectangle(PixelRect rectangle)
    {
        var nativeRectangle = ToNativeRect(rectangle);
        return NativeMethods.MonitorFromRect(
            in nativeRectangle,
            NativeMethods.MonitorDefaultToNull);
    }

    public DisplayDpi GetMonitorDpi(nint monitorHandle)
    {
        var result = NativeMethods.GetDpiForMonitor(
            monitorHandle,
            NativeMethods.DpiTypeEffective,
            out var dpiX,
            out var dpiY);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return new DisplayDpi(dpiX, dpiY);
    }

    public uint GetWindowDpi(nint windowHandle)
    {
        return NativeMethods.GetDpiForWindow(windowHandle);
    }

    public nint GetForegroundWindow()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public nint GetShellWindow()
    {
        return NativeMethods.GetShellWindow();
    }

    public bool IsWindowVisible(nint windowHandle)
    {
        return NativeMethods.IsWindowVisible(windowHandle);
    }

    public bool IsWindowMinimized(nint windowHandle)
    {
        return NativeMethods.IsIconic(windowHandle);
    }

    public bool IsWindowCloaked(nint windowHandle)
    {
        var result = NativeMethods.DwmGetWindowAttributeUInt32(
            windowHandle,
            NativeMethods.DwmWindowAttributeCloaked,
            out var cloaked,
            sizeof(uint));
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return cloaked != 0;
    }

    public PixelRect GetWindowBounds(nint windowHandle)
    {
        var result = NativeMethods.DwmGetWindowAttributeRectangle(
            windowHandle,
            NativeMethods.DwmWindowAttributeExtendedFrameBounds,
            out var rectangle,
            (uint)Marshal.SizeOf<NativeRect>());
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return ToPixelRect(rectangle);
    }

    public nint GetMonitorForWindow(nint windowHandle)
    {
        return NativeMethods.MonitorFromWindow(
            windowHandle,
            NativeMethods.MonitorDefaultToNull);
    }

    private static TaskbarEdge ToTaskbarEdge(uint edge)
    {
        return edge switch
        {
            0 => TaskbarEdge.Left,
            1 => TaskbarEdge.Top,
            2 => TaskbarEdge.Right,
            _ => TaskbarEdge.Bottom,
        };
    }

    private static PixelRect? TryGetAutoHideTaskbarWindowBounds(uint edge)
    {
        var autoHideData = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(),
            Edge = edge,
        };
        var taskbarWindow = unchecked((nint)NativeMethods.SHAppBarMessage(
            NativeMethods.AppBarGetAutoHideBar,
            ref autoHideData));
        if (taskbarWindow == 0)
        {
            return null;
        }

        if (NativeMethods.GetWindowRect(taskbarWindow, out var rectangle) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return ToPixelRect(rectangle);
    }

    private static PixelRect ToPixelRect(NativeRect rectangle)
    {
        return new PixelRect(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
    }

    private static NativeRect ToNativeRect(PixelRect rectangle)
    {
        return new NativeRect
        {
            Left = rectangle.Left,
            Top = rectangle.Top,
            Right = rectangle.Right,
            Bottom = rectangle.Bottom,
        };
    }
}
