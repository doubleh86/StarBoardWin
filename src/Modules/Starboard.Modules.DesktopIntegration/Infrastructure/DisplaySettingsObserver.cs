namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed class DisplaySettingsObserver : IDisposable
{
    private readonly IDisplaySettingsEventSource _eventSource;
    private int _disposeState;

    internal DisplaySettingsObserver()
        : this(DisplaySettingsEventSource.Instance)
    {
    }

    internal DisplaySettingsObserver(IDisplaySettingsEventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);

        _eventSource = eventSource;
        _eventSource.Changed += HandleDisplaySettingsChanged;
    }

    internal event EventHandler? DisplaySettingsChanged;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            _eventSource.Changed -= HandleDisplaySettingsChanged;
        }
        catch
        {
            // A failed unsubscribe still owns the static event subscription. Keep
            // disposal retryable rather than marking the observer as released.
            Volatile.Write(ref _disposeState, 0);
            throw;
        }
    }

    private void HandleDisplaySettingsChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
