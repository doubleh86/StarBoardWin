using System.ComponentModel;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal interface IWinEventHookNativeApi
{
    uint GetCurrentThreadIdentifier();

    nint RegisterForegroundChanged(WinEventCallback callback);

    bool Unregister(nint hook, out int errorCode);
}

internal sealed class WinEventHookNativeApi : IWinEventHookNativeApi
{
    public uint GetCurrentThreadIdentifier()
    {
        return NativeMethods.GetCurrentThreadId();
    }

    public nint RegisterForegroundChanged(WinEventCallback callback)
    {
        var hook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground,
                                                 NativeMethods.EventSystemForeground, 0, callback, 0, 0,
                                                 NativeMethods.WinEventOutOfContext);
        if (hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return hook;
    }

    public bool Unregister(nint hook, out int errorCode)
    {
        var released = NativeMethods.UnhookWinEvent(hook);
        errorCode = released == true ? 0 : Marshal.GetLastWin32Error();

        return released;
    }
}
