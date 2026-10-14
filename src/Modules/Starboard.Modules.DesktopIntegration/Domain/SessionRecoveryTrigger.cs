namespace Starboard.Modules.DesktopIntegration.Domain;

internal sealed class SessionRecoveryTrigger(TimeProvider timeProvider)
{
    private long? _lastTimestamp;

    internal bool TryRequest(nuint eventKind)
    {
        // No state machine assumes that remote disconnect precedes console connect/unlock.
        if (eventKind != 1 && eventKind != 2 && eventKind != 3 && eventKind != 4 && eventKind != 8 && eventKind != 15)
        {
            return false;
        }

        var timestamp = timeProvider.GetTimestamp();
        if (_lastTimestamp is long previous && timeProvider.GetElapsedTime(previous, timestamp) < TimeSpan.FromSeconds(5))
        {
            return false;
        }

        _lastTimestamp = timestamp;

        return true;
    }
}
