using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Tests.Contracts;

[TestClass]
public sealed class MonitorSnapshotContractTests
{
    [TestMethod]
    public void SnapshotForSecondaryMonitorWithMixedDpiPreservesVirtualScreenCoordinates()
    {
        var snapshot = new MonitorSnapshot(
            new nint(41),
            new PixelRect(-2560, -200, 0, 1240),
            new PixelRect(-2560, -160, 0, 1240),
            new DisplayDpi(144, 144));

        Assert.AreEqual(new nint(41), snapshot.Handle);
        Assert.AreEqual(new PixelRect(-2560, -200, 0, 1240), snapshot.Bounds);
        Assert.AreEqual(new PixelRect(-2560, -160, 0, 1240), snapshot.WorkArea);
        Assert.AreEqual(1.5, snapshot.Dpi.ScaleX);
        Assert.AreEqual(1.5, snapshot.Dpi.ScaleY);
    }
}
