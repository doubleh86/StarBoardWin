using System.Windows.Controls;
using System.Windows.Threading;
using Starboard.Modules.Terminal;
using Starboard.Modules.Terminal.Contracts;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class TerminalLayoutCheckIntegrationTests
{
    [TestMethod]
    public async Task PublicRequestOnUnavailableSurfaceAndAfterShutdownPreservesRendererAndDoesNotStartShell()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // No StartAsync or browser initialization: exercise the actual public entry point
                    // on an unloaded surface, with worker requests and pending dispatcher shutdown work.
                    using var module = new TerminalModule(new NullDiagnosticLog());
                    var surface = module.Surface;
                    var host = (Grid)surface.FindName("RendererHost");
                    var renderer = host.Children[0];
                    await Task.Run(() =>
                    {
                        for (var revision = 1; revision <= 100; revision++)
                        {
                            module.RequestLayoutCheck(new TerminalLayoutCheckRequest(revision));
                        }
                    });
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    module.NotifyPanelVisibilityChanged(false);
                    module.RequestLayoutCheck(new TerminalLayoutCheckRequest(101));
                    module.NotifyPanelVisibilityChanged(true);
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.AreSame(surface, module.Surface);
                    Assert.HasCount(1, host.Children);
                    Assert.AreSame(renderer, host.Children[0]);
                    module.RequestLayoutCheck(new TerminalLayoutCheckRequest(102));
                    await module.ShutdownAsync();
                    await Task.Run(() => module.RequestLayoutCheck(new TerminalLayoutCheckRequest(103)));
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.AreSame(surface, module.Surface);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
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

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
        }
    }
}
