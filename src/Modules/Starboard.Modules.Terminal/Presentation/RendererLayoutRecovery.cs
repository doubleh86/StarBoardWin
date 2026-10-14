namespace Starboard.Modules.Terminal.Presentation;

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
    private (double Width, double Height, double Scale)? _target;
    private long _started;
    private int _attempts;

    internal int Attempts => _attempts;

    internal bool TryRepair(RendererLayoutSample sample)
    {
        if (sample.IsValid == false)
        {
            return false;
        }

        if (sample.NeedsRepair == false)
        {
            _target = null;
            _attempts = 0;
            return false;
        }

        var target = (sample.WidthDip, sample.HeightDip, sample.Scale);
        if (_target != target)
        {
            _target = target;
            _started = timeProvider.GetTimestamp();
            _attempts = 0;
        }

        if (_attempts >= 5 || timeProvider.GetElapsedTime(_started) > TimeSpan.FromSeconds(5))
        {
            return false;
        }

        _attempts++;

        return true;
    }
}
