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
    public async Task RequestDuringObservation_LatestRevisionWinsAndDuplicatesKeepOriginalBudget()
    {
        await RunOnDispatcherAsync(async dispatcher =>
        {
            var time = new FakeTimeProvider();
            var recovery = new RendererLayoutRecovery(time);
            var first = new TaskCompletionSource();
            var second = new TaskCompletionSource();
            var observations = 0;
            var repairs = new List<long>();
            var narrow = new RendererLayoutSample(1920, 200, 1, 1040, 1920, 1,
                                                  1920, 1920, 1920, 1908, 1908);
            var observation = new RendererLayoutObservation(async () =>
            {
                dispatcher.VerifyAccess();
                var revision = recovery.Revision;
                observations++;
                await (observations == 1 ? first.Task : observations == 2 ? second.Task : Task.CompletedTask);
                if (revision == recovery.Revision && recovery.TryRepair(narrow) == true)
                {
                    repairs.Add(revision);
                }
            }, () => true, () => false);
            using var requests = new RendererLayoutRequestDispatcher(dispatcher, revision =>
            {
                if (recovery.BeginRevision(revision) == true)
                {
                    observation.Request();
                }
            });

            requests.Request(1);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, observations);
            Task.Run(() => Parallel.For(2, 101, revision => requests.Request(revision))).GetAwaiter().GetResult();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(100L, recovery.Revision);
            Assert.AreEqual(1, observations);
            first.SetResult();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(2, observations);
            Assert.HasCount(0, repairs, "The superseded revision must not repair using its stale sample.");
            time.Timestamp = 1_000;
            second.SetResult();
            await observation.Completion;
            CollectionAssert.AreEqual(new long[] { 100 }, repairs);

            for (var attempt = 1; attempt <= 6; attempt++)
            {
                requests.Request(100);
                requests.Request(99);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(attempt + 1, observations, "Duplicates must not schedule another observation.");
                time.Timestamp = 1_000 + attempt * 100;
                observation.Observe();
                await observation.Completion;
            }

            Assert.HasCount(5, repairs);
            Assert.AreEqual(5, recovery.Attempts);
            time.Timestamp = 10_000;
            requests.Request(100);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            observation.Observe();
            await observation.Completion;
            Assert.HasCount(5, repairs);
            requests.Request(101);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(101L, repairs[repairs.Count - 1]);
            Assert.AreEqual(1, recovery.Attempts);
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

    private sealed class FakeTimeProvider : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => Timestamp;
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
