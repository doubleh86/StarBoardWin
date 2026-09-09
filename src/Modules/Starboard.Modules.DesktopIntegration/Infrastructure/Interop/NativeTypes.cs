using System.Runtime.InteropServices;

namespace Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppBarData
{
    internal int Size;
    internal nint WindowHandle;
    internal uint CallbackMessage;
    internal uint Edge;
    internal NativeRect Rectangle;
    internal nint Parameter;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MonitorInfo
{
    internal int Size;
    internal NativeRect Monitor;
    internal NativeRect WorkArea;
    internal uint Flags;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[return: MarshalAs(UnmanagedType.Bool)]
internal delegate bool MonitorEnumerationCallback(nint monitor, nint deviceContext, nint monitorRectangle,
                                                  nint applicationData);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void WinEventCallback(nint hook, uint eventType, nint windowHandle, int objectIdentifier,
                                        int childIdentifier, uint eventThreadIdentifier, uint eventTime);
