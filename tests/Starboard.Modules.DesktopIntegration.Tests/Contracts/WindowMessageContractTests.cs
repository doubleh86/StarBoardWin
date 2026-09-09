using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Tests.Contracts;

[TestClass]
public sealed class WindowMessageContractTests
{
    [TestMethod]
    public void WindowMessageWithPointerSizedArgumentsPreservesAllNativeBits()
    {
        var wordParameter = Environment.Is64BitProcess
            ? unchecked((nuint)0xfedcba9876543210UL)
            : 0x76543210u;
        var longParameter = Environment.Is64BitProcess
            ? unchecked((nint)0x8123456789abcdefUL)
            : unchecked((nint)0x89abcdefu);

        var message = new WindowMessage(new nint(0x1234), 0x02e0, wordParameter, longParameter);

        Assert.AreEqual(wordParameter, message.WordParameter);
        Assert.AreEqual(longParameter, message.LongParameter);
    }

    [TestMethod]
    public void FromWindowMessageForDpiChangeCopiesBothDpiAxesAndSuggestedBounds()
    {
        var rawDpi = (nuint)((144u << 16) | 120u);
        var message = new WindowMessage(new nint(23), 0x02e0, rawDpi, new nint(0x5678));
        var suggestedBounds = new PixelRect(-1920, 0, 0, 1080);

        var result = DpiChangedWindowMessage.FromWindowMessage(message, suggestedBounds);

        Assert.AreEqual(new nint(23), result.WindowHandle);
        Assert.AreEqual(new DisplayDpi(120, 144), result.Dpi);
        Assert.AreEqual(suggestedBounds, result.SuggestedBounds);
    }

    [TestMethod]
    public void FromWindowMessageForDisplayChangeDecodesDocumentedMessageFields()
    {
        const uint horizontalResolution = 2560;
        const uint verticalResolution = 1440;
        var packedResolution = (nint)((verticalResolution << 16) | horizontalResolution);
        var message = new WindowMessage(new nint(29), 0x007e, 32, packedResolution);

        var result = DisplayChangedWindowMessage.FromWindowMessage(message);

        Assert.AreEqual(new nint(29), result.WindowHandle);
        Assert.AreEqual(32u, result.BitsPerPixel);
        Assert.AreEqual(horizontalResolution, result.HorizontalResolution);
        Assert.AreEqual(verticalResolution, result.VerticalResolution);
    }
}
