using System.Runtime.InteropServices;

namespace Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

internal static partial class NativeMethods
{
    internal const uint AppBarGetState = 0x00000004;
    internal const uint AppBarGetTaskbarPosition = 0x00000005;
    internal const uint AppBarGetAutoHideBar = 0x00000007;
    internal const uint AppBarStateAutoHide = 0x00000001;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const uint MonitorDefaultToNull = 0x00000000;
    internal const uint MonitorInfoPrimary = 0x00000001;

    internal const uint DpiTypeEffective = 0;

    internal const uint EventSystemForeground = 0x0003;
    internal const uint WinEventOutOfContext = 0x0000;

    internal const uint DwmWindowAttributeExtendedFrameBounds = 9;
    internal const uint DwmWindowAttributeCloaked = 14;

    internal const int WindowMessageNonClientHitTest = 0x0084;
    internal const int WindowMessageNonClientLeftButtonDown = 0x00A1;
    internal const int WindowMessageNonClientLeftButtonDoubleClick = 0x00A3;
    internal const int WindowMessageSizing = 0x0214;
    internal const int WindowMessageEnterSizeMove = 0x0231;
    internal const int WindowMessageExitSizeMove = 0x0232;
    internal const int HitTestClient = 1;
    internal const int HitTestTop = 12;
    internal const int SizingEdgeTop = 3;

    internal const int WindowLongStyle = -16;
    internal const int WindowLongExtendedStyle = -20;
    internal const long WindowStyleThickFrame = 0x00040000L;
    internal const long ExtendedStyleToolWindow = 0x00000080L;

    internal const uint SetWindowPositionNoSize = 0x0001;
    internal const uint SetWindowPositionNoMove = 0x0002;
    internal const uint SetWindowPositionNoZOrder = 0x0004;
    internal const uint SetWindowPositionNoActivate = 0x0010;
    internal const uint SetWindowPositionFrameChanged = 0x0020;
    internal const uint SetWindowPositionShowWindow = 0x0040;

    internal const uint ModifierAlt = 0x0001;
    internal const uint ModifierControl = 0x0002;
    internal const uint ModifierShift = 0x0004;
    internal const uint ModifierNoRepeat = 0x4000;
    internal const uint VirtualKeyF1 = 0x70;
    internal const uint VirtualKeyF24 = 0x87;

    internal static readonly nint Top = new(0);
    internal static readonly nint NotTopMost = new(-2);

    [LibraryImport("shell32.dll")]
    internal static partial nuint SHAppBarMessage(uint message, ref AppBarData data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromRect(in NativeRect rectangle, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(nint deviceContext, nint clipRectangle,
                                                     MonitorEnumerationCallback callback, nint applicationData);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint windowHandle, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint windowHandle);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, uint dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(nint windowHandle, int message, nint wordParameter,
                                             nint longParameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetShellWindow();

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttributeUInt32(nint windowHandle, uint attribute, out uint attributeValue,
                                                            uint attributeSize);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttributeRectangle(nint windowHandle, uint attribute,
                                                               out NativeRect attributeValue, uint attributeSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWinEventHook(uint eventMinimum, uint eventMaximum, nint hookModule,
                                                 WinEventCallback callback, uint processIdentifier,
                                                 uint threadIdentifier, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetWindowLongPtrW(nint windowHandle, int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWindowLongPtrW(nint windowHandle, int index, nint newLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height,
                                              uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint sourceThreadIdentifier, uint targetThreadIdentifier,
                                                   [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint windowHandle, int identifier);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string messageName);
}
