using System.Text;
using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalOutputBufferTests
{
    [TestMethod]
    [DataRow(4095)]
    [DataRow(65535)]
    public void OutputRoundTripsAcrossUtf16FragmentAndBatchBoundaries(int prefixLength)
    {
        var input = new string('x', prefixLength) + "😀한글尾";
        var buffer = new TerminalOutputBuffer(1);
        var result = new StringBuilder();
        _ = buffer.Append(input[..(prefixLength + 1)]);
        DrainThroughJson(buffer, result);
        Assert.AreEqual(prefixLength, result.Length);
        Assert.AreEqual(1, buffer.PendingLength);

        _ = buffer.Append(input[(prefixLength + 1)..]);
        DrainThroughJson(buffer, result);

        Assert.AreEqual(input, result.ToString());
        Assert.AreEqual(0, buffer.PendingLength);
        Assert.AreEqual(0, buffer.InFlightLength);
    }

    [TestMethod]
    public void OutputBatchDoesNotSplitPairAlreadyInBuffer()
    {
        var input = new string('x', 65_535) + "😀tail";
        var buffer = new TerminalOutputBuffer(1);
        _ = buffer.Append(input);
        var result = new StringBuilder();

        DrainThroughJson(buffer, result);

        Assert.AreEqual(input, result.ToString());
    }

    [TestMethod]
    public void SlowParserCannotAccumulateBeyondInFlightLimit()
    {
        var buffer = new TerminalOutputBuffer(1);
        _ = buffer.Append(new string('x', 512 * 1024));
        var batches = new List<TerminalOutputBatch>();
        while (buffer.TakeBatch() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.AreEqual(4, batches.Count);
        Assert.AreEqual(256 * 1024, buffer.InFlightLength);
        Assert.IsTrue(buffer.ShouldPauseReading(true));
        Assert.IsFalse(buffer.Acknowledge(batches[1].Sequence));
        Assert.IsFalse(buffer.Acknowledge(-1));
        Assert.IsNull(buffer.TakeBatch());

        Assert.IsTrue(buffer.Acknowledge(batches[0].Sequence));
        Assert.IsFalse(buffer.Acknowledge(batches[0].Sequence));
        Assert.IsNotNull(buffer.TakeBatch());
        Assert.AreEqual(256 * 1024, buffer.InFlightLength);
        Assert.IsTrue(buffer.ShouldPauseReading(true));
    }

    [TestMethod]
    public void ReadGateResumesOnlyAfterLowWatermarkAndRendererFailure()
    {
        var buffer = new TerminalOutputBuffer(1);
        _ = buffer.Append(new string('x', 512 * 1024));
        Assert.IsTrue(buffer.ShouldPauseReading(true));
        for (var index = 0; index < 5; index++)
        {
            var batch = buffer.TakeBatch();
            Assert.IsNotNull(batch);
            Assert.IsTrue(buffer.Acknowledge(batch.Sequence));
            Assert.IsTrue(buffer.ShouldPauseReading(true));
        }

        var last = buffer.TakeBatch();
        Assert.IsNotNull(last);
        Assert.IsTrue(buffer.Acknowledge(last.Sequence));
        Assert.IsFalse(buffer.ShouldPauseReading(true));

        _ = buffer.Append(new string('x', 512 * 1024));
        Assert.IsTrue(buffer.ShouldPauseReading(true));
        Assert.IsFalse(buffer.ShouldPauseReading(false));
    }

    [TestMethod]
    public void ReconnectReplaysUnacknowledgedDataAndRejectsOldSequence()
    {
        var buffer = new TerminalOutputBuffer(7);
        _ = buffer.Append("first😀");
        var first = buffer.TakeBatch();
        Assert.IsNotNull(first);
        _ = buffer.Append("next한글");

        Assert.IsFalse(buffer.ResetDelivery());
        Assert.IsFalse(buffer.Acknowledge(first.Sequence));
        var replay = buffer.TakeBatch();
        Assert.IsNotNull(replay);
        Assert.AreEqual("first😀next한글", replay.Data);
        Assert.IsTrue(replay.Sequence > first.Sequence);
        Assert.IsFalse(buffer.Acknowledge(first.Sequence));
        Assert.IsTrue(buffer.Acknowledge(replay.Sequence));
    }

    [TestMethod]
    public void FailureBacklogTrimsOldestWithoutLeavingLowSurrogate()
    {
        var buffer = new TerminalOutputBuffer(1);
        _ = buffer.Append("😀" + new string('x', 4 * 1024 * 1024 - 2));
        Assert.IsTrue(buffer.Append("y"));
        Assert.AreEqual(4 * 1024 * 1024 - 1, buffer.PendingLength);
        var batch = buffer.TakeBatch();
        Assert.IsNotNull(batch);
        Assert.AreEqual('x', batch.Data[0]);

        var oversized = new TerminalOutputBuffer(2);
        Assert.IsTrue(oversized.Append("😀" + new string('x', 4 * 1024 * 1024 - 1)));
        Assert.AreEqual(4 * 1024 * 1024 - 1, oversized.PendingLength);
        Assert.IsFalse(oversized.ShouldPauseReading(false));
    }

    [TestMethod]
    public async Task PausedReadWaitIsAsynchronousResumableAndCancellable()
    {
        var gate = new TerminalOutputReadGate();
        await gate.WaitAsync(CancellationToken.None);
        gate.SetPaused(true);
        var pending = gate.WaitAsync(CancellationToken.None);
        Assert.IsFalse(pending.IsCompleted);
        gate.SetPaused(true);
        Assert.IsFalse(pending.IsCompleted);
        gate.SetPaused(false);
        await pending.WaitAsync(TimeSpan.FromSeconds(2));

        gate.SetPaused(true);
        using var cancellation = new CancellationTokenSource();
        var cancelled = gate.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => cancelled);
        gate.SetPaused(false);
        await gate.WaitAsync(CancellationToken.None);
    }

    private static void DrainThroughJson(TerminalOutputBuffer buffer, StringBuilder result)
    {
        while (buffer.TakeBatch() is { } batch)
        {
            var json = RendererProtocol.SerializeSessionMessage("output", TerminalSessionId.CreateNew(),
                                                                new { data = batch.Data });
            using var document = JsonDocument.Parse(json);
            result.Append(document.RootElement.GetProperty("payload").GetProperty("data").GetString());
            Assert.IsTrue(buffer.Acknowledge(batch.Sequence));
        }
    }
}
