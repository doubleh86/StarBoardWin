using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

[TestClass]
public sealed class DpiChangedMessageAdapterTests
{
    [TestMethod]
    public void CaptureCopiesMessageDpiAndSuggestedRectangleBeforeNativeMemoryIsReleased()
    {
        var rectanglePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        DpiChangedWindowMessage result;

        try
        {
            Marshal.StructureToPtr(new NativeRect
                                   {
                                       Left = -1600,
                                       Top = 100,
                                       Right = 0,
                                       Bottom = 1000,
                                   },
                                   rectanglePointer, false);
            var message = new WindowMessage(new nint(51), 0x02e0, (nuint)((192u << 16) | 120u), rectanglePointer);

            result = DpiChangedMessageAdapter.Capture(message);
        }
        finally
        {
            Marshal.FreeHGlobal(rectanglePointer);
        }

        Assert.AreEqual(new DisplayDpi(120, 192), result.Dpi);
        Assert.AreEqual(new PixelRect(-1600, 100, 0, 1000), result.SuggestedBounds);
    }
}
