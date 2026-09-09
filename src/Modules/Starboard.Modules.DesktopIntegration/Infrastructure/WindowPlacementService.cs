using System.ComponentModel;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal static class WindowPlacementService
{
    internal static void ConfigureToolWindow(nint windowHandle)
    {
        var currentStyle = NativeMethods.GetWindowLongPtrW(windowHandle, NativeMethods.WindowLongExtendedStyle);
        var updatedStyle = new nint(currentStyle.ToInt64() | NativeMethods.ExtendedStyleToolWindow);

        Marshal.SetLastPInvokeError(0);
        var previousStyle = NativeMethods.SetWindowLongPtrW(windowHandle, NativeMethods.WindowLongExtendedStyle,
                                                            updatedStyle);
        if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (NativeMethods.SetWindowPos(windowHandle, NativeMethods.NotTopMost, 0, 0, 0, 0,
                                       NativeMethods.SetWindowPositionNoSize |
                                       NativeMethods.SetWindowPositionNoMove |
                                       NativeMethods.SetWindowPositionNoActivate) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static void PlaceWithoutActivation(nint windowHandle, PixelRect rectangle)
    {
        if (NativeMethods.SetWindowPos(windowHandle, 0, rectangle.Left, rectangle.Top, rectangle.Width,
                                       rectangle.Height,
                                       NativeMethods.SetWindowPositionNoZOrder |
                                       NativeMethods.SetWindowPositionNoActivate |
                                       NativeMethods.SetWindowPositionShowWindow) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static void ActivateOnExplicitRequest(nint windowHandle)
    {
        var currentThreadIdentifier = NativeMethods.GetCurrentThreadId();
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThreadIdentifier = foregroundWindow == 0
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var inputQueuesAttached = foregroundThreadIdentifier != 0 &&
            foregroundThreadIdentifier != currentThreadIdentifier &&
            NativeMethods.AttachThreadInput(currentThreadIdentifier, foregroundThreadIdentifier, true);

        try
        {
            if (NativeMethods.SetWindowPos(windowHandle, NativeMethods.Top, 0, 0, 0, 0,
                                           NativeMethods.SetWindowPositionNoSize |
                                           NativeMethods.SetWindowPositionNoMove |
                                           NativeMethods.SetWindowPositionShowWindow) == false)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _ = NativeMethods.SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (inputQueuesAttached == true)
            {
                _ = NativeMethods.AttachThreadInput(currentThreadIdentifier, foregroundThreadIdentifier, false);
            }
        }
    }
}
