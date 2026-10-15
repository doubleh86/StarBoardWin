using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Size = System.Windows.Size;

namespace Starboard.Windows.Shell;

public partial class MainWindow
{
    private readonly TimeProvider _layoutTimeProvider;
    private readonly Lock _layoutGate = new();
    private DispatcherOperation? _layoutOperation;
    private DispatcherTimer? _panelLayoutTimer;
    private long _requestedLayoutRevision;
    private long _layoutStarted;
    private int _layoutChecks;
    private bool _layoutStopped;

    internal event Action<long>? PanelLayoutCheckReady;

    private void HandlePanelLayoutVisibilityChanged(object sender, DependencyPropertyChangedEventArgs eventArguments)
    {
        if (IsVisible == false)
        {
            lock (_layoutGate)
            {
                _layoutChecks = 5;
                _layoutOperation?.Abort();
                _layoutOperation = null;
                StopPanelLayoutTimer();
            }
        }
    }

    private void HandlePanelLayoutCheckRequested(object? sender, EventArgs eventArguments)
    {
        if (sender is Modules.DesktopIntegration.DesktopIntegrationModule module)
        {
            RequestPanelLayoutCheck(module.LayoutRecoveryRevision);
        }
    }

    internal void RequestPanelLayoutCheck(long revision)
    {
        lock (_layoutGate)
        {
            if (_layoutStopped == true || revision <= _requestedLayoutRevision ||
                Dispatcher.HasShutdownStarted == true || Dispatcher.HasShutdownFinished == true)
            {
                return;
            }

            _requestedLayoutRevision = revision;
            _layoutStarted = _layoutTimeProvider.GetTimestamp();
            _layoutChecks = 0;
            _layoutOperation ??= Dispatcher.InvokeAsync(BeginPanelLayoutChecks, DispatcherPriority.Background);
        }
    }

    private void BeginPanelLayoutChecks()
    {
        lock (_layoutGate)
        {
            _layoutOperation = null;
            StopPanelLayoutTimer();
            if (_layoutStopped == true)
            {
                return;
            }

            _panelLayoutTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                                                    CheckPanelLayoutOnTimer, Dispatcher);
            CheckPanelLayout();
        }
    }

    private void CheckPanelLayoutOnTimer(object? sender, EventArgs eventArguments)
    {
        CheckPanelLayout();
    }

    // Also used by host boundary tests with a controlled clock; no native APIs belong in this view.
    internal void CheckPanelLayout()
    {
        Dispatcher.VerifyAccess();
        lock (_layoutGate)
        {
            if (_layoutStopped == true || IsVisible == false || _requestedLayoutRevision <= 0 ||
                _layoutChecks >= 5 || _layoutTimeProvider.GetElapsedTime(_layoutStarted) >= TimeSpan.FromSeconds(5))
            {
                _layoutChecks = 5;
                StopPanelLayoutTimer();
                return;
            }

            _layoutChecks++;
            var layout = desktopIntegration?.CapturePanelClientLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            if (layout is { } sample && sample.WindowBounds.IsEmpty == false &&
                sample.ClientWidthPixel <= sample.WindowBounds.Width &&
                sample.ClientHeightPixel <= sample.WindowBounds.Height &&
                sample.TryGetSizeDip(dpi.DpiScaleX, dpi.DpiScaleY, out var width, out var height) == true &&
                (Math.Abs(PanelBorder.ActualWidth - width) > 1 || Math.Abs(PanelBorder.ActualHeight - height) > 1))
            {
                // Repair only the client root. Window placement, taskbar anchor and activation remain module-owned.
                PanelBorder.InvalidateMeasure();
                PanelBorder.Measure(new Size(width, height));
                PanelBorder.Arrange(new Rect(0, 0, width, height));
                PanelBorder.UpdateLayout();
            }

            // Forward immediately, even while DPI/geometry settles. Terminal owns its own observations;
            // retries retain this revision and cannot reopen its budget after root stabilization.
            PanelLayoutCheckReady?.Invoke(_requestedLayoutRevision);
            if (_layoutChecks >= 5)
            {
                StopPanelLayoutTimer();
            }
        }
    }

    internal void StopPanelLayoutRecovery()
    {
        Dispatcher.VerifyAccess();
        lock (_layoutGate)
        {
            _layoutStopped = true;
            _layoutOperation?.Abort();
            _layoutOperation = null;
            StopPanelLayoutTimer();
            if (desktopIntegration is not null)
            {
                desktopIntegration.PanelLayoutCheckRequested -= HandlePanelLayoutCheckRequested;
            }
        }
    }

    private void StopPanelLayoutTimer()
    {
        if (_panelLayoutTimer is not null)
        {
            _panelLayoutTimer.Stop();
            _panelLayoutTimer.Tick -= CheckPanelLayoutOnTimer;
            _panelLayoutTimer = null;
        }
    }
}
