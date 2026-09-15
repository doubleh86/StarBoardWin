using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal readonly record struct ScreenPixelPoint(int X, int Y);

internal static class SizingMessageAdapter
{
    internal static ScreenPixelPoint CaptureScreenPoint(WindowMessage message)
    {
        var packedCoordinates = unchecked((ulong)message.LongParameter.ToInt64());
        var x = unchecked((short)(packedCoordinates & 0xffff));
        var y = unchecked((short)((packedCoordinates >> 16) & 0xffff));

        return new ScreenPixelPoint(x, y);
    }

    internal static PixelRect CaptureBounds(WindowMessage message)
    {
        if (message.LongParameter == 0)
        {
            throw new ArgumentException("WM_SIZING did not provide a window rectangle.", nameof(message));
        }

        var rectangle = Marshal.PtrToStructure<NativeRect>(message.LongParameter);

        return new PixelRect(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    internal static void ApplyBounds(WindowMessage message, PixelRect bounds)
    {
        if (message.LongParameter == 0)
        {
            throw new ArgumentException("WM_SIZING did not provide a window rectangle.", nameof(message));
        }

        var rectangle = new NativeRect
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Right,
            Bottom = bounds.Bottom,
        };
        Marshal.StructureToPtr(rectangle, message.LongParameter, false);
    }
}
