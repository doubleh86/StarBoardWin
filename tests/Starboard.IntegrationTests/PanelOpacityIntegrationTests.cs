using System.Windows;
using System.Windows.Interop;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class PanelOpacityIntegrationTests
{
    private static readonly double[] _OpacityValues = [0.97, 0.8, 1.0, 0.97];

    [TestMethod]
    [Timeout(10_000)]
    public async Task SetOpacityOnOpaqueWpfWindowAppliesWithoutStartupFailure()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window
                {
                    AllowsTransparency = false,
                    Opacity = 0.97,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Width = 300,
                    Height = 100,
                };
                window.Show();
                var handle = new WindowInteropHelper(window).Handle;
                WindowPlacementService.ConfigureToolWindow(handle);
                var nativeApi = new DesktopNativeApi();
                var foreground = nativeApi.GetForegroundWindow();
                var bounds = nativeApi.GetWindowBounds(handle);
                using var runtime = new WindowsDesktopIntegrationRuntime(new NullDiagnosticLog(),
                                                                         opacity => window.Opacity = opacity);
                foreach (var opacity in _OpacityValues)
                {
                    runtime.SetPanelOpacity(handle, opacity);
                    Assert.AreEqual(opacity, window.Opacity);
                    Assert.AreEqual(bounds, nativeApi.GetWindowBounds(handle));
                    Assert.AreEqual(foreground, nativeApi.GetForegroundWindow());
                }
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void SetOpacityPropagatesHostFailureForSettingsRollback()
    {
        var failure = new InvalidOperationException("Host opacity failed.");
        using var runtime = new WindowsDesktopIntegrationRuntime(new NullDiagnosticLog(), _ => throw failure);
        var actual = Assert.ThrowsExactly<InvalidOperationException>(() => runtime.SetPanelOpacity(0, 0.8));
        Assert.AreSame(failure, actual);
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
        }
    }
}
