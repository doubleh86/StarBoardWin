using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;

namespace Starboard.Modules.DesktopIntegration.Tests.Domain;

#pragma warning disable CA1707 // Test names follow the repository's Scenario_ExpectedResult convention.

[TestClass]
public sealed class PanelResizeCalculatorTests
{
    [TestMethod]
    [DataRow(96u, 300)]
    [DataRow(120u, 375)]
    [DataRow(144u, 450)]
    [DataRow(192u, 600)]
    public void CalculateTopResize_SupportedScale_PreservesBottomAnchor(uint dpiY, int expectedPixelHeight)
    {
        var snapshot = CreateBottomTaskbarSnapshot(new PixelRect(0, 0, 1920, 1400), 1400);
        var proposedTop = 1400 - expectedPixelHeight;

        var result = PanelResizeCalculator.CalculateTopResize(snapshot, new DisplayDpi(96, dpiY), proposedTop);

        Assert.AreEqual(300, result.HeightDip);
        Assert.AreEqual(new PixelRect(0, proposedTop, 1920, 1400), result.Bounds);
    }

    [TestMethod]
    public void CalculateTopResize_MixedDpiMonitorMove_UsesLatestVerticalDpiAndVirtualOrigin()
    {
        var snapshot = CreateBottomTaskbarSnapshot(new PixelRect(-2560, -200, 0, 1400), 1400);

        var result = PanelResizeCalculator.CalculateTopResize(snapshot, new DisplayDpi(120, 192), 1000);

        Assert.AreEqual(200, result.HeightDip);
        Assert.AreEqual(new PixelRect(-2560, 1000, 0, 1400), result.Bounds);
    }

    [TestMethod]
    public void CalculateTopResize_ProposedHeightOutsideConfiguredRange_ClampsToLimits()
    {
        var snapshot = CreateBottomTaskbarSnapshot(new PixelRect(0, 0, 1920, 1200), 1200);

        var minimum = PanelResizeCalculator.CalculateTopResize(snapshot, new DisplayDpi(96, 96), 1199);
        var maximum = PanelResizeCalculator.CalculateTopResize(snapshot, new DisplayDpi(96, 96), -500);

        Assert.AreEqual(96, minimum.HeightDip);
        Assert.AreEqual(new PixelRect(0, 1104, 1920, 1200), minimum.Bounds);
        Assert.AreEqual(720, maximum.HeightDip);
        Assert.AreEqual(new PixelRect(0, 480, 1920, 1200), maximum.Bounds);
    }

    [TestMethod]
    public void CalculateTopResize_WorkAreaIsShorterThanMaximum_UsesSafeWorkAreaUpperLimit()
    {
        var snapshot = CreateBottomTaskbarSnapshot(new PixelRect(0, 100, 800, 500), 500);

        var result = PanelResizeCalculator.CalculateTopResize(snapshot, new DisplayDpi(96, 96), -1000);

        Assert.AreEqual(400, result.HeightDip);
        Assert.AreEqual(snapshot.WorkArea, result.Bounds);
    }

    [TestMethod]
    [DataRow(96u, 5, true)]
    [DataRow(120u, 7, true)]
    [DataRow(144u, 8, true)]
    [DataRow(192u, 11, true)]
    [DataRow(192u, 12, false)]
    public void IsTopResizeHit_SupportedScale_UsesDipSizedTopTarget(uint dpiY, int pixelOffset,
                                                                    bool expectedHit)
    {
        var bounds = new PixelRect(-1920, 400, 0, 800);

        var result = PanelResizeCalculator.IsTopResizeHit(bounds, new DisplayDpi(120, dpiY), -100,
                                                          bounds.Top + pixelOffset);

        Assert.AreEqual(expectedHit, result);
    }

    private static TaskbarSnapshot CreateBottomTaskbarSnapshot(PixelRect workArea, int taskbarTop)
    {
        return new TaskbarSnapshot(TaskbarEdge.Bottom,
                                   new PixelRect(workArea.Left, taskbarTop, workArea.Right, taskbarTop + 40),
                                   new PixelRect(workArea.Left, workArea.Top, workArea.Right, taskbarTop + 40),
                                   workArea, false, 96);
    }
}

#pragma warning restore CA1707
