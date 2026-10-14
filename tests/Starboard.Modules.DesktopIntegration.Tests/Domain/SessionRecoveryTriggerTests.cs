using Starboard.Modules.DesktopIntegration.Domain;

namespace Starboard.Modules.DesktopIntegration.Tests.Domain;

[TestClass]
public sealed class SessionRecoveryTriggerTests
{
    [TestMethod]
    public void TryRequestDuplicateReverseAndDelayedEventsCoalescesWithoutExtendingWindow()
    {
        var time = new FakeTimeProvider();
        var trigger = new SessionRecoveryTrigger(time);
        Assert.IsTrue(trigger.TryRequest(8));
        Assert.IsFalse(trigger.TryRequest(1));
        Assert.IsFalse(trigger.TryRequest(4));
        time.Timestamp = 4_999;
        Assert.IsFalse(trigger.TryRequest(8));
        time.Timestamp = 5_000;
        Assert.IsTrue(trigger.TryRequest(4));
        Assert.IsFalse(trigger.TryRequest(15));
        Assert.IsFalse(trigger.TryRequest(7));
        Assert.IsFalse(trigger.TryRequest(99));
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => Timestamp;
    }
}
