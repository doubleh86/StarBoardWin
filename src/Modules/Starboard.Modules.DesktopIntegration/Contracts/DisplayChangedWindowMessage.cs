namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// The display depth and primary-display resolution supplied by WM_DISPLAYCHANGE.
/// </summary>
/// <remarks>
/// This notification is a recapture trigger. Monitor-specific bounds and work areas
/// must be refreshed through the monitor adapter rather than inferred from the primary
/// resolution carried by this message.
/// </remarks>
public readonly record struct DisplayChangedWindowMessage(nint WindowHandle, uint BitsPerPixel,
                                                          uint HorizontalResolution, uint VerticalResolution)
{
    public static DisplayChangedWindowMessage FromWindowMessage(WindowMessage message)
    {
        var packedResolution = unchecked((nuint)message.LongParameter);
        var horizontalResolution = (uint)(packedResolution & 0xffffu);
        var verticalResolution = (uint)((packedResolution >> 16) & 0xffffu);

        return new DisplayChangedWindowMessage(message.WindowHandle, (uint)message.WordParameter, horizontalResolution,
                                               verticalResolution);
    }
}
