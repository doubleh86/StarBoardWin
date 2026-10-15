using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Terminal;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Windows.Shell;

namespace Starboard.IntegrationTests;

public sealed partial class DesktopWindowIntegrationTests
{
    [TestMethod]
    public async Task MainWindowSameOuterSizeRepairsRootAndForwardsOriginalRevisionWithoutRestart()
    {
        await RunHostLayoutTestAsync(async (window, runtime, module, clock) =>
        {
            using var terminal = new TerminalModule(new NullDiagnosticLog());
            var surface = terminal.Surface;
            var rendererHost = (Grid)surface.FindName("RendererHost");
            var renderer = rendererHost.Children[0];
            window.SetTerminalContent(surface);
            var forwarded = new List<long>();
            window.PanelLayoutCheckReady += revision =>
            {
                forwarded.Add(revision);
                terminal.RequestLayoutCheck(new TerminalLayoutCheckRequest(revision));
            };
            var root = (Border)window.FindName("PanelBorder");
            var outerWidth = window.ActualWidth;
            var height = window.Height;
            var anchor = runtime.Placements[runtime.Placements.Count - 1];
            root.Arrange(new Rect(0, 0, 400, root.ActualHeight));
            module.Refresh();
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(400, root.ActualWidth);
            Assert.AreEqual(0, runtime.ClientCaptureCount);
            Assert.HasCount(0, forwarded);
            ReturnFromSession(window, module);
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(outerWidth, root.ActualWidth, 1);
            Assert.AreEqual(outerWidth, window.ActualWidth);
            Assert.AreEqual(height, window.Height);
            Assert.AreEqual(anchor, runtime.Placements[runtime.Placements.Count - 1]);
            Assert.AreEqual(0, runtime.ActivationCount);
            Assert.IsTrue(forwarded.Count > 0);
            Assert.IsTrue(forwarded.All(revision => revision == 1));
            Assert.AreSame(surface, terminal.Surface);
            Assert.AreSame(renderer, rendererHost.Children[0]);

            for (var attempt = 1; attempt < 5; attempt++)
            {
                root.Arrange(new Rect(0, 0, 400, root.ActualHeight));
                clock.Timestamp += 500;
                window.RequestPanelLayoutCheck(1);
                window.CheckPanelLayout();
                Assert.AreEqual(outerWidth, root.ActualWidth, 1);
            }

            root.Arrange(new Rect(0, 0, 400, root.ActualHeight));
            module.Refresh();
            window.RequestPanelLayoutCheck(0);
            window.CheckPanelLayout();
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(400, root.ActualWidth);
            Assert.HasCount(5, forwarded);
            await terminal.ShutdownAsync();
        });
    }

    [TestMethod]
    public async Task MainWindowDpiAndTemporaryGeometryWaitWithoutExtendingFiveSecondDeadline()
    {
        await RunHostLayoutTestAsync(async (window, runtime, module, clock) =>
        {
            var root = (Border)window.FindName("PanelBorder");
            var good = runtime.ClientLayout!.Value;
            root.Arrange(new Rect(0, 0, 400, root.ActualHeight));
            runtime.ClientLayout = good with { WindowDpi = new DisplayDpi(384, 384) };
            ReturnFromSession(window, module);
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(400, root.ActualWidth);
            clock.Timestamp = 1_000;
            runtime.Geometry = runtime.Geometry with { TrackingState = DisplayTrackingState.Fallback };
            module.Refresh();
            runtime.ClientLayout = good;
            window.CheckPanelLayout();
            Assert.AreEqual(400, root.ActualWidth);
            clock.Timestamp = 2_000;
            runtime.Geometry = CreateDefaultGeometry();
            module.Refresh();
            runtime.ClientLayout = good with { ClientWidthPixel = 0 };
            window.CheckPanelLayout();
            Assert.AreEqual(400, root.ActualWidth);
            clock.Timestamp = 3_000;
            runtime.ClientLayout = good;
            window.CheckPanelLayout();
            Assert.AreEqual(window.ActualWidth, root.ActualWidth, 1);
            root.Arrange(new Rect(0, 0, 400, root.ActualHeight));
            clock.Timestamp = 5_000;
            ReturnFromSession(window, module);
            window.CheckPanelLayout();
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(400, root.ActualWidth);
        });
    }

    [TestMethod]
    public async Task MainWindowWorkerRequestsCoalesceAndShutdownDiscardsPendingAndTimerCallbacks()
    {
        await RunHostLayoutTestAsync(async (window, runtime, module, clock) =>
        {
            var forwarded = new List<long>();
            window.PanelLayoutCheckReady += forwarded.Add;
            await Task.Run(() =>
            {
                window.RequestPanelLayoutCheck(3);
                window.RequestPanelLayoutCheck(2);
                window.RequestPanelLayoutCheck(3);
            });
            await DrainHostDispatcherAsync(window);
            CollectionAssert.AreEqual(new long[] { 3 }, forwarded);
            window.RequestPanelLayoutCheck(4);
            window.StopPanelLayoutRecovery();
            var captures = runtime.ClientCaptureCount;
            await Task.Run(() => window.RequestPanelLayoutCheck(5));
            window.CheckPanelLayout();
            module.Refresh();
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(captures, runtime.ClientCaptureCount);
            CollectionAssert.AreEqual(new long[] { 3 }, forwarded);
        });
    }

    [TestMethod]
    public async Task MainWindowHiddenPanelDoesNotRepairOrResumeOldRevisionOnShow()
    {
        await RunHostLayoutTestAsync(async (window, runtime, module, clock) =>
        {
            ReturnFromSession(window, module);
            window.Hide();
            await DrainHostDispatcherAsync(window);
            var captures = runtime.ClientCaptureCount;
            window.Show();
            module.Refresh();
            window.CheckPanelLayout();
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(captures, runtime.ClientCaptureCount);
        });
    }

    [TestMethod]
    public async Task MainWindowExpiredQueuedBurstIsDiscardedAndOnlyNewRevisionCanStartAgain()
    {
        await RunHostLayoutTestAsync(async (window, runtime, module, clock) =>
        {
            var forwarded = new List<long>();
            window.PanelLayoutCheckReady += forwarded.Add;
            window.RequestPanelLayoutCheck(1);
            clock.Timestamp = 5_000;
            await DrainHostDispatcherAsync(window);
            Assert.HasCount(0, forwarded);
            Assert.AreEqual(0, runtime.ClientCaptureCount);
            window.RequestPanelLayoutCheck(1);
            window.CheckPanelLayout();
            Assert.HasCount(0, forwarded);
            window.RequestPanelLayoutCheck(2);
            await DrainHostDispatcherAsync(window);
            CollectionAssert.AreEqual(new long[] { 2 }, forwarded);
            window.Close();
            var captures = runtime.ClientCaptureCount;
            await Task.Run(() => window.RequestPanelLayoutCheck(3));
            window.CheckPanelLayout();
            await DrainHostDispatcherAsync(window);
            Assert.AreEqual(captures, runtime.ClientCaptureCount);
        });
    }

    private static void ReturnFromSession(MainWindow window, DesktopIntegrationModule module)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        foreach (var kind in new nuint[] { 8, 1, 4, 4, 15 })
        {
            module.HandleWindowMessage(new WindowMessage(handle,
                                                         DesktopIntegrationModule.WindowMessageSessionChange,
                                                         kind, 12));
        }
    }

    private static async Task DrainHostDispatcherAsync(MainWindow window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task RunHostLayoutTestAsync(Func<MainWindow, FakeDesktopIntegrationRuntime,
                                                     DesktopIntegrationModule, HostLayoutTimeProvider, Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                MainWindow? window = null;
                DesktopIntegrationModule? module = null;
                try
                {
                    var clock = new HostLayoutTimeProvider();
                    var resources = new ResourceDictionary
                    {
                        ["CanvasBrush"] = Brushes.Black,
                        ["ForegroundBrush"] = Brushes.White,
                        ["RuleBrush"] = Brushes.Gray,
                    };
                    window = new MainWindow(clock, resources);
                    window.Show();
                    var runtime = new FakeDesktopIntegrationRuntime();
                    module = CreateModule(runtime);
                    var handle = window.AttachDesktopIntegration(module);
                    module.Attach(handle, new PanelOptions(window.Height));
                    var dpi = VisualTreeHelper.GetDpi(window);
                    var widthPixel = (int)(window.ActualWidth * dpi.DpiScaleX);
                    var heightPixel = (int)(window.ActualHeight * dpi.DpiScaleY);
                    var bounds = new PixelRect(0, 0, widthPixel, heightPixel);
                    var windowDpi = new DisplayDpi((uint)Math.Round(dpi.PixelsPerInchX),
                                                   (uint)Math.Round(dpi.PixelsPerInchY));
                    runtime.ClientLayout = new PanelClientLayout(bounds, widthPixel, heightPixel, windowDpi);
                    await test(window, runtime, module, clock);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    window?.Close();
                    module?.Dispose();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(2)));
    }

    private sealed class HostLayoutTimeProvider : TimeProvider
    {
        internal long Timestamp { get; set; }

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => Timestamp;
    }
}
