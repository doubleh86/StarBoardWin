namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// A lifetime-safe copy of the information supplied by WM_DPICHANGED.
/// </summary>
public readonly record struct DpiChangedWindowMessage(nint WindowHandle, DisplayDpi Dpi, PixelRect SuggestedBounds)
{
    public static DpiChangedWindowMessage FromWindowMessage(WindowMessage message, PixelRect suggestedBounds)
    {
        var dpiX = (uint)(message.WordParameter & 0xffffu);
        var dpiY = (uint)((message.WordParameter >> 16) & 0xffffu);

        return new DpiChangedWindowMessage(message.WindowHandle, new DisplayDpi(dpiX, dpiY), suggestedBounds);
    }
}
