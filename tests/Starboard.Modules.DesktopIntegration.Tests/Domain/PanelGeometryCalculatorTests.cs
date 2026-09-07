using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;

namespace Starboard.Modules.DesktopIntegration.Tests.Domain;

[TestClass]
public sealed class PanelGeometryCalculatorTests
{
    [TestMethod]
    [DataRow(96u, 100)]
    [DataRow(120u, 125)]
    [DataRow(144u, 150)]
    [DataRow(192u, 200)]
    public void DipToPixelsAcrossSupportedScalesUsesMonitorDpi(
        uint dpi,
        int expectedPixels)
    {
        var result = PanelGeometryCalculator.DipToPixels(100, dpi);

        Assert.AreEqual(expectedPixels, result);
    }

    [TestMethod]
    public void PixelsToDipWithMixedDpiUsesIndependentAxesAndPreservesVirtualOrigin()
    {
        var result = PanelGeometryCalculator.PixelsToDip(
            new PixelRect(-2400, 144, 0, 1584),
            new DisplayDpi(120, 144));

        Assert.AreEqual(new DipRect(-1920, 96, 0, 1056), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithVerticalTaskbarUsesHorizontalDpiAxis()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Left,
            new PixelRect(0, 0, 60, 1440),
            new PixelRect(0, 0, 2560, 1440),
            new PixelRect(60, 0, 2560, 1440),
            false,
            144);

        var result = PanelGeometryCalculator.CalculateCollapsed(
            snapshot,
            new DisplayDpi(120, 192),
            100);

        Assert.AreEqual(new PixelRect(60, 0, 185, 1440), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithAutoHideWorkAreaUsesTaskbarInnerEdge()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Bottom,
            new PixelRect(0, 1040, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            true,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 200);

        Assert.AreEqual(new PixelRect(0, 840, 1920, 1040), result);
    }
}
