using System.Runtime.InteropServices;

namespace Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

internal static partial class NativeMethods
{
    internal const uint AppBarGetState = 0x00000004;
    internal const uint AppBarGetTaskbarPosition = 0x00000005;
    internal const uint AppBarStateAutoHide = 0x00000001;
    internal const uint MonitorDefaultToNearest = 0x00000002;

    internal const int WindowLongExtendedStyle = -20;
    internal const long ExtendedStyleToolWindow = 0x00000080L;

    internal const uint SetWindowPositionNoSize = 0x0001;
    internal const uint SetWindowPositionNoMove = 0x0002;
    internal const uint SetWindowPositionNoZOrder = 0x0004;
    internal const uint SetWindowPositionNoActivate = 0x0010;
    internal const uint SetWindowPositionShowWindow = 0x0040;

    internal const uint ModifierAlt = 0x0001;
    internal const uint ModifierControl = 0x0002;
    internal const uint ModifierNoRepeat = 0x4000;
    internal const uint VirtualKeyE = 0x45;
    internal const uint VirtualKeyS = 0x53;

    internal static readonly nint Top = new(0);
    internal static readonly nint NotTopMost = new(-2);

    [LibraryImport("shell32.dll")]
    internal static partial nuint SHAppBarMessage(uint message, ref AppBarData data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromRect(
        in NativeRect rectangle,
        uint flags);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetWindowLongPtrW(nint windowHandle, int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWindowLongPtrW(nint windowHandle, int index, nint newLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processIdentifier);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(
        uint sourceThreadIdentifier,
        uint targetThreadIdentifier,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(
        nint windowHandle,
        int identifier,
        uint modifiers,
        uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint windowHandle, int identifier);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string messageName);
}
