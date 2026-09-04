using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;

namespace Starboard.Modules.DesktopIntegration.Tests;

[TestClass]
public sealed class PanelGeometryCalculatorTests
{
    [TestMethod]
    public void CalculateCollapsedWithTabStripAndEightLineBodyUses200Dip()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Bottom,
            new PixelRect(0, 1040, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 200);

        Assert.AreEqual(new PixelRect(0, 840, 1920, 1040), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithBottomTaskbarAt100PercentUsesConfiguredHeight()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Bottom,
            new PixelRect(0, 1040, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 96);

        Assert.AreEqual(new PixelRect(0, 944, 1920, 1040), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithBottomTaskbarAt150PercentPlacesPanelAboveTaskbar()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Bottom,
            new PixelRect(0, 1392, 2560, 1440),
            new PixelRect(0, 0, 2560, 1440),
            new PixelRect(0, 0, 2560, 1392),
            false,
            144);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 184);

        Assert.AreEqual(new PixelRect(0, 1116, 2560, 1392), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithLeftTaskbarUsesWorkAreaInnerEdge()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Left,
            new PixelRect(0, 0, 64, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(64, 0, 1920, 1080),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 184);

        Assert.AreEqual(new PixelRect(64, 0, 248, 1080), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithTopTaskbarUsesWorkAreaTopEdge()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Top,
            new PixelRect(0, 0, 1920, 40),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 40, 1920, 1080),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 96);

        Assert.AreEqual(new PixelRect(0, 40, 1920, 136), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithRightTaskbarUsesWorkAreaRightEdge()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Right,
            new PixelRect(1856, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1856, 1080),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 96);

        Assert.AreEqual(new PixelRect(1760, 0, 1856, 1080), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithNegativeMonitorCoordinatesPreservesOrigin()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Bottom,
            new PixelRect(-1920, 1040, 0, 1080),
            new PixelRect(-1920, 0, 0, 1080),
            new PixelRect(-1920, 0, 0, 1040),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 96);

        Assert.AreEqual(new PixelRect(-1920, 944, 0, 1040), result);
    }

    [TestMethod]
    public void CalculateCollapsedWithOversizedHeightClampsToWorkArea()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Bottom,
            new PixelRect(0, 560, 800, 600),
            new PixelRect(0, 0, 800, 600),
            new PixelRect(0, 0, 800, 560),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateCollapsed(snapshot, 5000);

        Assert.AreEqual(snapshot.WorkArea, result);
    }

    [TestMethod]
    public void CalculateExpandedWithAnyTaskbarReturnsWorkArea()
    {
        var snapshot = new TaskbarSnapshot(
            TaskbarEdge.Right,
            new PixelRect(1856, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1856, 1080),
            false,
            96);

        var result = PanelGeometryCalculator.CalculateExpanded(snapshot);

        Assert.AreEqual(snapshot.WorkArea, result);
    }
}
