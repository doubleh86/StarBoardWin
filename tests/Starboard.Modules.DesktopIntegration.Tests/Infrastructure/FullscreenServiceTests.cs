using System.ComponentModel;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

[TestClass]
public sealed class FullscreenServiceTests
{
    [TestMethod]
    public void CaptureWithForegroundCoveringPanelMonitorReportsFullscreen()
    {
        var nativeApi = CreateNativeApi();
        nativeApi.WindowBounds = new PixelRect(0, 0, 1920, 1080);
        var service = new FullscreenService(nativeApi);

        var result = service.Capture(new nint(10), CreatePanelMonitor());

        Assert.AreEqual(PanelFullscreenState.FullscreenOnPanelMonitor, result.State);
        Assert.IsTrue(result.IsReliable);
        Assert.IsNull(result.Failure);
    }

    [TestMethod]
    public void CaptureWithMaximizedWorkAreaWindowDoesNotReportFullscreen()
    {
        var nativeApi = CreateNativeApi();
        nativeApi.WindowBounds = new PixelRect(0, 0, 1920, 1040);
        var service = new FullscreenService(nativeApi);

        var result = service.Capture(new nint(10), CreatePanelMonitor());

        Assert.AreEqual(PanelFullscreenState.Normal, result.State);
        Assert.IsTrue(result.IsReliable);
    }

    [TestMethod]
    public void CaptureWhenNativeDetectionFailsReturnsNonActivatingUnreliableFallback()
    {
        var nativeFailure = new Win32Exception(1400, "Invalid window handle.");
        var nativeApi = CreateNativeApi();
        nativeApi.WindowBoundsException = nativeFailure;
        var service = new FullscreenService(nativeApi);

        var result = service.Capture(new nint(10), CreatePanelMonitor());

        Assert.AreEqual(PanelFullscreenState.Normal, result.State);
        Assert.IsFalse(result.IsReliable);
        Assert.AreSame(nativeFailure, result.Failure);
        Assert.AreEqual(PanelWindowActivation.PreserveForeground, result.Activation);
        Assert.AreEqual(PanelWindowZOrder.PreserveNormal, result.ZOrder);
    }

    private static FakeDesktopNativeApi CreateNativeApi()
    {
        return new FakeDesktopNativeApi
        {
            ForegroundWindowHandle = 20,
            ShellWindowHandle = 30,
            WindowVisible = true,
            WindowMonitorHandle = 1,
        };
    }

    private static MonitorSnapshot CreatePanelMonitor()
    {
        return new MonitorSnapshot(
            new nint(1),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            new DisplayDpi(96, 96));
    }
}
