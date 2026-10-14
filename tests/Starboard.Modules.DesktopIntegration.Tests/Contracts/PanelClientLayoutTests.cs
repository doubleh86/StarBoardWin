using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Tests.Contracts;

[TestClass]
public sealed class PanelClientLayoutTests
{
    [TestMethod]
    [DataRow(96u, 1d, 1920d)]
    [DataRow(120u, 1.25d, 1536d)]
    [DataRow(144u, 1.5d, 1280d)]
    [DataRow(192u, 2d, 960d)]
    public void TryGetSizeDipCurrentWindowDpiConvertsClientPixelsOnce(uint dpi, double scale, double expectedWidth)
    {
        var layout = new PanelClientLayout(new PixelRect(0, 0, 1920, 400), 1920, 400, new DisplayDpi(dpi, dpi));
        Assert.IsTrue(layout.TryGetSizeDip(scale, scale, out var width, out var height));
        Assert.AreEqual(expectedWidth, width);
        Assert.AreEqual(400 / scale, height);
    }

    [TestMethod]
    public void TryGetSizeDipStaleWpfDpiOrEmptyClientRejectsRecovery()
    {
        var layout = new PanelClientLayout(default, 1920, 300, new DisplayDpi(144, 144));
        Assert.IsFalse(layout.TryGetSizeDip(1, 1, out _, out _));
        Assert.IsFalse(layout.TryGetSizeDip(double.NaN, 1.5, out _, out _));
        Assert.IsFalse((layout with { ClientWidthPixel = 0 }).TryGetSizeDip(1.5, 1.5, out _, out _));
    }
}
