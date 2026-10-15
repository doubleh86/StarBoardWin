using System.Windows.Threading;

namespace Starboard.Modules.Terminal.Presentation;

// UI-owned: timer observations and host requests share one task and at most one follow-up.
internal sealed class RendererLayoutObservation(Func<Task> observe, Func<bool> canObserve, Func<bool> scriptPending)
{
    private bool _requested;

    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal long Epoch { get; private set; }

    internal void Request()
    {
        _requested = true;
        Observe();
    }

    internal void Cancel()
    {
        Epoch++;
        _requested = false;
    }

    internal void Observe()
    {
        if (Completion.IsCompleted == false || scriptPending() == true || canObserve() == false)
        {
            return;
        }

        Completion = ObserveRequestedAsync();
    }

    private async Task ObserveRequestedAsync()
    {
        do
        {
            _requested = false;
            await observe();
        }
        while (_requested == true && scriptPending() == false && canObserve() == true);

        // A timed-out browser call remains owned; the timer may resume only after it completes.
        _requested = false;
    }
}

// The lock covers both the watermark and the single owned dispatcher operation, including shutdown races.
internal sealed class RendererLayoutRequestDispatcher(Dispatcher dispatcher, Action<long> checkLayout) : IDisposable
{
    private readonly Lock _gate = new();
    private DispatcherOperation? _operation;
    private long _latestRevision;
    private long _pendingRevision;
    private bool _isDisposed;

    internal void Request(long revision)
    {
        lock (_gate)
        {
            if (_isDisposed == true || revision <= _latestRevision ||
                dispatcher.HasShutdownStarted == true || dispatcher.HasShutdownFinished == true)
            {
                return;
            }

            _latestRevision = revision;
            _pendingRevision = revision;
            _operation ??= dispatcher.InvokeAsync(Dispatch, DispatcherPriority.Background);
        }
    }

    internal void CancelPending()
    {
        lock (_gate)
        {
            _operation?.Abort();
            _operation = null;
            _pendingRevision = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;
            CancelPending();
        }
    }

    private void Dispatch()
    {
        lock (_gate)
        {
            var revision = _pendingRevision;
            _operation = null;
            _pendingRevision = 0;
            if (_isDisposed == false && revision > 0)
            {
                checkLayout(revision);
            }
        }
    }
}

internal readonly record struct RendererLayoutSample(double WidthDip, double HeightDip, double Scale,
                                                     double ControlWidthDip, double DomWidth, double DomScale,
                                                     double TabWidth, double WorkspaceWidth, double TerminalWidth,
                                                     double ScrollbarRight, double ExpectedScrollbarRight, double ZoomFactor = 1)
{
    internal bool IsValid => double.IsFinite(WidthDip) == true && double.IsFinite(HeightDip) == true &&
        double.IsFinite(Scale) == true && double.IsFinite(DomWidth) == true && double.IsFinite(DomScale) == true &&
        double.IsFinite(ControlWidthDip) == true && double.IsFinite(TabWidth) == true &&
        double.IsFinite(WorkspaceWidth) == true && double.IsFinite(TerminalWidth) == true &&
        double.IsFinite(ScrollbarRight) == true && double.IsFinite(ExpectedScrollbarRight) == true &&
        double.IsFinite(ZoomFactor) == true && ZoomFactor > 0 &&
        WidthDip > 0 && HeightDip > 0 && Scale > 0 && DomWidth > 0 && DomScale > 0;

    internal bool NeedsRepair => Math.Abs(ControlWidthDip - WidthDip) > 1 ||
        Math.Abs(DomScale - Scale * ZoomFactor) > 0.01 || Math.Abs(DomWidth * DomScale - WidthDip * Scale) > 2 ||
        Math.Abs(TabWidth - DomWidth) > 2 || Math.Abs(WorkspaceWidth - DomWidth) > 2 ||
        (TerminalWidth > 0 && Math.Abs(TerminalWidth - WorkspaceWidth) > 2) ||
        (ScrollbarRight > 0 && Math.Abs(ScrollbarRight - ExpectedScrollbarRight) > 2);
}

internal sealed class RendererLayoutRecovery(TimeProvider timeProvider)
{
    private long _started;
    private int _attempts;
    private bool _isActive;

    internal int Attempts => _attempts;

    internal long Revision { get; private set; }

    internal bool BeginRevision(long revision)
    {
        if (revision <= Revision)
        {
            return false;
        }

        Revision = revision;
        _started = timeProvider.GetTimestamp();
        _attempts = 0;
        _isActive = true;

        return true;
    }

    internal void Cancel()
    {
        _isActive = false;
    }

    internal bool TryRepair(RendererLayoutSample sample)
    {
        if (_isActive == false || sample.IsValid == false || sample.NeedsRepair == false)
        {
            return false;
        }

        if (_attempts >= 5 || timeProvider.GetElapsedTime(_started) >= TimeSpan.FromSeconds(5))
        {
            return false;
        }

        _attempts++;

        return true;
    }
}
