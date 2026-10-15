using Starboard.Modules.Terminal.Presentation;

namespace Starboard.Modules.Terminal.Tests.Presentation;

[TestClass]
public sealed class RendererLayoutRecoveryTests
{
    private static RendererLayoutSample Normal => new(1920, 200, 1, 1920, 1920, 1, 1920, 1920, 1920, 1908, 1908);

    [TestMethod]
    public void TryRepairSameOuterSizeWithNarrowInternalBoundsDetectsEachBoundary()
    {
        var recovery = new RendererLayoutRecovery(new FakeTimeProvider());
        Assert.IsTrue(recovery.BeginRevision(1));
        Assert.IsFalse(recovery.TryRepair(Normal));
        Assert.IsTrue(recovery.TryRepair(Normal with { ControlWidthDip = 1040 }));
        Assert.IsTrue(recovery.TryRepair(Normal with { DomWidth = 1040 }));
        Assert.IsTrue(recovery.TryRepair(Normal with { TabWidth = 1040, WorkspaceWidth = 1040 }));
        Assert.IsTrue(recovery.TryRepair(Normal with { ScrollbarRight = 1028 }));
        Assert.IsFalse(recovery.TryRepair(Normal));
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(1.25d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public void TryRepairCurrentDpiUsesPhysicalPixelComparisonWithoutDoubleScaling(double scale)
    {
        var recovery = new RendererLayoutRecovery(new FakeTimeProvider());
        Assert.IsTrue(recovery.BeginRevision(1));
        var sample = Normal with { Scale = scale, DomScale = scale };
        Assert.IsFalse(recovery.TryRepair(sample));
        Assert.IsTrue(recovery.TryRepair(sample with { DomScale = 0.75 }));
    }

    [TestMethod]
    public void TryRepairBrowserZoomKeepsPhysicalBoundsIndependentOfCssPixels()
    {
        var recovery = new RendererLayoutRecovery(new FakeTimeProvider());
        Assert.IsTrue(recovery.BeginRevision(1));
        var sample = Normal with
        {
            Scale = 1.5, ZoomFactor = 2, DomScale = 3, DomWidth = 960,
            TabWidth = 960, WorkspaceWidth = 960, TerminalWidth = 960,
            ScrollbarRight = 948, ExpectedScrollbarRight = 948,
        };
        Assert.IsFalse(recovery.TryRepair(sample));
    }

    [TestMethod]
    public void TryRepairContinuousMismatchStopsAfterFiveAttemptsOrFiveSeconds()
    {
        var time = new FakeTimeProvider();
        var recovery = new RendererLayoutRecovery(time);
        Assert.IsTrue(recovery.BeginRevision(1));
        var narrow = Normal with { DomWidth = 1040 };
        for (var attempt = 0; attempt < 5; attempt++)
        {
            time.Timestamp = attempt * 1_000;
            Assert.IsTrue(recovery.TryRepair(narrow));
        }

        for (var attempt = 5; attempt < 20; attempt++)
        {
            time.Timestamp = attempt * 1_000;
            Assert.IsFalse(recovery.TryRepair(narrow));
        }

        // Neither a new target nor a normal sample starts another recovery burst.
        Assert.IsFalse(recovery.TryRepair(narrow with { WidthDip = 2560 }));
        Assert.IsFalse(recovery.TryRepair(Normal));
        Assert.IsFalse(recovery.TryRepair(narrow));
        Assert.IsTrue(recovery.BeginRevision(2));
        Assert.IsTrue(recovery.TryRepair(narrow with { WidthDip = 2560 }));
        time.Timestamp += 5_001;
        Assert.IsFalse(recovery.TryRepair(narrow with { WidthDip = 2560 }));
        Assert.IsFalse(recovery.TryRepair(Normal with { WidthDip = 0 }));
        Assert.IsFalse(recovery.TryRepair(Normal with { DomScale = double.NaN }));
        Assert.IsFalse(recovery.TryRepair(Normal));
        Assert.IsFalse(recovery.TryRepair(narrow));
    }

    [TestMethod]
    public void TryRepairPeriodicObservationsCannotStartOrRenewRecoveryBurst()
    {
        var time = new FakeTimeProvider();
        var recovery = new RendererLayoutRecovery(time);
        var narrow = Normal with { WorkspaceWidth = 1040 };
        Assert.IsFalse(recovery.TryRepair(narrow));
        Assert.IsFalse(recovery.BeginRevision(0));
        Assert.IsFalse(recovery.BeginRevision(-1));
        Assert.IsTrue(recovery.BeginRevision(7));
        Assert.IsTrue(recovery.TryRepair(narrow));
        Assert.IsFalse(recovery.TryRepair(Normal));
        Assert.AreEqual(1, recovery.Attempts);
        time.Timestamp = 4_999;
        Assert.IsFalse(recovery.BeginRevision(7));
        Assert.IsFalse(recovery.BeginRevision(6));
        Assert.IsTrue(recovery.TryRepair(narrow with { WidthDip = 2560, Scale = 1.5 }));
        time.Timestamp = 5_000;
        Assert.IsFalse(recovery.TryRepair(narrow));
        Assert.IsFalse(recovery.BeginRevision(7));
        Assert.AreEqual(2, recovery.Attempts);
        Assert.IsTrue(recovery.BeginRevision(8));
        Assert.IsTrue(recovery.TryRepair(narrow));
        Assert.AreEqual(1, recovery.Attempts);
    }

    [TestMethod]
    public void CancelSameOrOlderRevisionCannotResumeAfterVisibilityOrRendererChange()
    {
        var recovery = new RendererLayoutRecovery(new FakeTimeProvider());
        var narrow = Normal with { DomWidth = 1040 };
        Assert.IsTrue(recovery.BeginRevision(2));
        Assert.IsTrue(recovery.TryRepair(narrow));
        recovery.Cancel();
        Assert.IsFalse(recovery.TryRepair(narrow));
        Assert.IsFalse(recovery.BeginRevision(2));
        Assert.IsFalse(recovery.BeginRevision(1));
        Assert.IsTrue(recovery.BeginRevision(3));
        Assert.IsTrue(recovery.TryRepair(narrow));
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => Timestamp;
    }
}
