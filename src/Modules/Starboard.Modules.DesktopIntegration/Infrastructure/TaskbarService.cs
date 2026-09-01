using System.ComponentModel;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal static class TaskbarService
{
    internal static TaskbarSnapshot Capture(nint windowHandle)
    {
        var appBarData = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(),
        };

        if (NativeMethods.SHAppBarMessage(
            NativeMethods.AppBarGetTaskbarPosition,
            ref appBarData) == 0)
        {
            throw new Win32Exception("Windows did not return the system taskbar position.");
        }

        var taskbarRectangle = appBarData.Rectangle;
        var monitorHandle = NativeMethods.MonitorFromRect(
            in taskbarRectangle,
            NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };

        if (NativeMethods.GetMonitorInfoW(monitorHandle, ref monitorInfo) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var stateData = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(),
        };
        var state = NativeMethods.SHAppBarMessage(
            NativeMethods.AppBarGetState,
            ref stateData);
        var dpi = NativeMethods.GetDpiForWindow(windowHandle);

        return new TaskbarSnapshot(
            ToEdge(appBarData.Edge),
            ToPixelRect(taskbarRectangle),
            ToPixelRect(monitorInfo.Monitor),
            ToPixelRect(monitorInfo.WorkArea),
            (state & NativeMethods.AppBarStateAutoHide) != 0,
            dpi == 0 ? 96u : dpi);
    }

    private static TaskbarEdge ToEdge(uint edge)
    {
        return edge switch
        {
            0 => TaskbarEdge.Left,
            1 => TaskbarEdge.Top,
            2 => TaskbarEdge.Right,
            _ => TaskbarEdge.Bottom,
        };
    }

    private static PixelRect ToPixelRect(NativeRect rectangle)
    {
        return new PixelRect(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
    }
}
