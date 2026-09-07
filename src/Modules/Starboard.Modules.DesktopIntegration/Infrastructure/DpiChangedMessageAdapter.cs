using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal static class DpiChangedMessageAdapter
{
    internal static DpiChangedWindowMessage Capture(WindowMessage message)
    {
        if (message.LongParameter == 0)
        {
            throw new ArgumentException(
                "WM_DPICHANGED did not provide a suggested window rectangle.",
                nameof(message));
        }

        // lParam is owned by the window procedure. Copy it before returning so no
        // pointer escapes the synchronous message callback.
        var rectangle = Marshal.PtrToStructure<NativeRect>(message.LongParameter);
        var suggestedBounds = new PixelRect(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

        return DpiChangedWindowMessage.FromWindowMessage(
            message,
            suggestedBounds);
    }
}
