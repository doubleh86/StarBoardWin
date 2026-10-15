using System.Windows.Threading;
using Starboard.Modules.Terminal.Presentation;

namespace Starboard.Modules.Terminal.Tests.Presentation;

[TestClass]
public sealed class RendererLayoutRequestDispatcherTests
{
    [TestMethod]
    public async Task RequestFromWorkerCoalescesLatestRevisionOnUiDispatcher()
    {
        await RunOnDispatcherAsync(async dispatcher =>
        {
            var revisions = new List<long>();
            using var requests = new RendererLayoutRequestDispatcher(dispatcher, revision =>
            {
                dispatcher.VerifyAccess();
                revisions.Add(revision);
            });
            // Block the dispatcher pump while workers submit a burst.
            Task.Run(() => Parallel.For(1, 101, revision => requests.Request(revision))).GetAwaiter().GetResult();
            requests.Request(100);
            requests.Request(1);
            requests.Request(0);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CollectionAssert.AreEqual(new long[] { 100 }, revisions);
            await Task.Run(() => requests.Request(101));
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CollectionAssert.AreEqual(new long[] { 100, 101 }, revisions);
        });
    }

    [TestMethod]
    public async Task CancelAndDisposeAbortPendingRequestsWithoutReplayingOldRevision()
    {
        await RunOnDispatcherAsync(async dispatcher =>
        {
            var revisions = new List<long>();
            using var requests = new RendererLayoutRequestDispatcher(dispatcher, revisions.Add);
            requests.Request(1);
            requests.CancelPending();
            requests.Request(1);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.HasCount(0, revisions);
            requests.Request(2);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CollectionAssert.AreEqual(new long[] { 2 }, revisions);
            requests.Request(3);
            requests.Dispose();
            await Task.Run(() => requests.Request(4));
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CollectionAssert.AreEqual(new long[] { 2 }, revisions);
        });
    }

    [TestMethod]
    public async Task RequestAfterDispatcherShutdownIsIgnored()
    {
        await RunOnDispatcherAsync(dispatcher =>
        {
            var calls = 0;
            using var requests = new RendererLayoutRequestDispatcher(dispatcher, _ => calls++);
            requests.Request(1);
            dispatcher.InvokeShutdown();
            requests.Request(2);
            Assert.AreEqual(0, calls);
            return Task.CompletedTask;
        });
    }

    private static async Task RunOnDispatcherAsync(Func<Dispatcher, Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await action(dispatcher);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    if (dispatcher.HasShutdownStarted == false)
                    {
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    }
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(2)));
    }
}
