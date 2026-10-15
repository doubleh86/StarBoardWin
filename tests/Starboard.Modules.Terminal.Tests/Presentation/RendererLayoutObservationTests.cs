using Starboard.Modules.Terminal.Presentation;

namespace Starboard.Modules.Terminal.Tests.Presentation;

[TestClass]
public sealed class RendererLayoutObservationTests
{
    [TestMethod]
    public async Task RequestDuringObservationMergesIntoOneFollowUpWithoutParallelWork()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var calls = 0;
        var observation = new RendererLayoutObservation(() => ++calls == 1 ? first.Task : second.Task,
                                                        () => true, () => false);
        observation.Observe();
        var completion = observation.Completion;
        for (var request = 0; request < 100; request++)
        {
            observation.Request();
            observation.Observe();
        }

        Assert.AreEqual(1, calls);
        Assert.AreSame(completion, observation.Completion);
        first.SetResult();
        Assert.AreEqual(2, calls);
        second.SetResult();
        await completion;
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task CancelDuringObservationInvalidatesEpochAndDropsPendingFollowUp()
    {
        var pending = new TaskCompletionSource();
        var calls = 0;
        var observation = new RendererLayoutObservation(() =>
                                                        {
                                                            calls++;
                                                            return pending.Task;
                                                        }, () => true, () => false);
        observation.Request();
        var epoch = observation.Epoch;
        observation.Request();
        observation.Cancel();
        pending.SetResult();
        await observation.Completion;
        Assert.AreEqual(1, calls);
        Assert.AreNotEqual(epoch, observation.Epoch);
    }

    [TestMethod]
    public async Task RequestAfterCancellationDuringObservationMergesIntoOneNewLifetimeFollowUp()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var calls = 0;
        var available = true;
        var observation = new RendererLayoutObservation(() => ++calls == 1 ? first.Task : second.Task,
                                                        () => available, () => false);
        observation.Request();
        var completion = observation.Completion;
        var oldEpoch = observation.Epoch;
        available = false;
        observation.Cancel();
        observation.Observe();
        available = true;
        for (var request = 0; request < 100; request++)
        {
            observation.Request();
        }

        Assert.AreEqual(1, calls);
        Assert.AreSame(completion, observation.Completion);
        Assert.AreNotEqual(oldEpoch, observation.Epoch);
        first.SetResult();
        Assert.AreEqual(2, calls);
        second.SetResult();
        await completion;
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task TimedOutBrowserCallBlocksFurtherObservationsUntilUnderlyingTaskCompletes()
    {
        var pending = new TaskCompletionSource();
        var scriptPending = true;
        var available = true;
        var calls = 0;
        var observation = new RendererLayoutObservation(() =>
                                                        {
                                                            calls++;
                                                            return pending.Task;
                                                        }, () => available, () => scriptPending);
        observation.Request();
        Assert.AreEqual(0, calls);
        scriptPending = false;
        observation.Observe();
        Assert.AreEqual(1, calls);
        observation.Request();
        scriptPending = true;
        pending.SetResult();
        await observation.Completion;
        for (var request = 0; request < 100; request++)
        {
            observation.Request();
            observation.Observe();
        }

        Assert.AreEqual(1, calls);
        scriptPending = false;
        available = false;
        observation.Cancel();
        observation.Observe();
        Assert.AreEqual(1, calls);
        available = true;
        observation.Observe();
        await observation.Completion;
        Assert.AreEqual(2, calls);
    }
}
