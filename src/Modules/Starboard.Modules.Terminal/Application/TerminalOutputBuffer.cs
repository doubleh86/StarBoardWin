using System.Text;

namespace Starboard.Modules.Terminal.Application;

internal sealed record TerminalOutputBatch(long Sequence, string Data);

// Owned by TerminalView's output lock. ACKs bound the WebView/xterm queue, not just this buffer.
internal sealed class TerminalOutputBuffer(long generation)
{
    internal const int MaximumBatchLength = 65_536;
    internal const int MaximumInFlightLength = 256 * 1024;
    internal const int MaximumPendingLength = 4 * 1024 * 1024;
    private const int _PauseLength = 512 * 1024;
    private const int _ResumeLength = 128 * 1024;

    private readonly StringBuilder _pending = new();
    private readonly Queue<TerminalOutputBatch> _inFlight = new();
    private long _nextSequence;
    private bool _isPaused;

    internal long Generation { get; } = generation;

    internal int PendingLength => _pending.Length;

    internal int InFlightLength { get; private set; }

    internal bool Append(string data)
    {
        // Avoid a temporary allocation proportional to a very large incoming fragment.
        if (data.Length >= MaximumPendingLength)
        {
            var discarded = _pending.Length > 0 || data.Length > MaximumPendingLength;
            _pending.Clear();
            var start = data.Length - MaximumPendingLength;
            if (char.IsLowSurrogate(data[start]) == true)
            {
                start++;
                discarded = true;
            }

            _pending.Append(data.AsSpan(start));
            return discarded;
        }

        _pending.Append(data);
        return TrimPending();
    }

    internal TerminalOutputBatch? TakeBatch()
    {
        var length = Math.Min(_pending.Length, Math.Min(MaximumBatchLength, MaximumInFlightLength - InFlightLength));
        if (length > 0 && char.IsHighSurrogate(_pending[length - 1]) == true)
        {
            // StreamReader can split a pair too. Retain a trailing high surrogate until its
            // next fragment arrives; JSON serialization would otherwise replace it permanently.
            length--;
        }

        if (length == 0)
        {
            return null;
        }

        var batch = new TerminalOutputBatch(++_nextSequence, _pending.ToString(0, length));
        _pending.Remove(0, length);
        _inFlight.Enqueue(batch);
        InFlightLength += length;
        return batch;
    }

    internal bool Acknowledge(long sequence)
    {
        if (_inFlight.TryPeek(out var batch) == false || batch.Sequence != sequence)
        {
            return false;
        }

        _inFlight.Dequeue();
        InFlightLength -= batch.Data.Length;
        return true;
    }

    internal bool ResetDelivery()
    {
        // A new renderer has no previous screen: replay unacknowledged data before newer output.
        var unacknowledged = string.Concat(_inFlight.Select(batch => batch.Data));
        _pending.Insert(0, unacknowledged);
        _inFlight.Clear();
        InFlightLength = 0;
        _isPaused = false;
        return TrimPending();
    }

    internal bool ShouldPauseReading(bool deliveryAvailable)
    {
        var length = PendingLength + InFlightLength;
        if (deliveryAvailable == false || length <= _ResumeLength)
        {
            _isPaused = false;
        }
        else if (length >= _PauseLength)
        {
            _isPaused = true;
        }

        return _isPaused;
    }

    private bool TrimPending()
    {
        var excess = _pending.Length - MaximumPendingLength;
        if (excess <= 0)
        {
            return false;
        }

        if (char.IsLowSurrogate(_pending[excess]) == true)
        {
            excess++;
        }

        _pending.Remove(0, excess);
        return true;
    }
}
