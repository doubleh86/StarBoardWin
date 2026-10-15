using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Starboard.Modules.Terminal.Contracts;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Presentation;

internal partial class TerminalView
{
    // Only numeric layout metadata is returned; no text, paths or session identifiers.
    private const string _LayoutScript = "(() => { " +
        "const rect = s => document.querySelector(s)?.getBoundingClientRect(); " +
        "const term = document.querySelector('.session-pane:not([hidden]) .xterm'); " +
        "const t = term?.getBoundingClientRect(); " +
        "const v = term?.querySelector('.xterm-scrollable-element, .xterm-viewport')?.getBoundingClientRect(); " +
        "return { width: innerWidth, scale: devicePixelRatio, tab: rect('.tab-strip')?.width ?? 0, " +
        "workspace: rect('#terminal-workspace')?.width ?? 0, terminal: t?.width ?? 0, " +
        "scrollbar: v?.right ?? 0, expectedScrollbar: t ? t.right - parseFloat(getComputedStyle(term).paddingRight) : 0 }; })()";

    private readonly RendererLayoutRecovery _layoutRecovery = new(TimeProvider.System);
    private readonly RendererLayoutRequestDispatcher _layoutRequests;
    private readonly RendererLayoutObservation _layoutObservation;
    private DispatcherTimer? _layoutTimer;
    private Task<string>? _layoutScriptTask;
    private bool _layoutObservationFailureReported;
    private bool _panelLayoutVisible = true;
    private CancellationTokenSource? _layoutObservationCancellation;

    internal void RequestLayoutCheck(TerminalLayoutCheckRequest request)
    {
        _layoutRequests.Request(request.RecoveryRevision);
    }

    private void RequestRendererLayoutCheck(long revision)
    {
        Dispatcher.VerifyAccess();
        if (CanObserveRendererLayout() == false || _layoutRecovery.BeginRevision(revision) == false)
        {
            return;
        }

        // A newer burst supersedes an in-flight sample; retain only one follow-up observation.
        _layoutObservation.Request();
    }

    private bool CanObserveRendererLayout()
    {
        return isDisposed == false && IsLoaded == true && IsVisible == true && _panelLayoutVisible == true &&
               rendererFailed == false && renderer.IsVisible == true &&
               rendererReady.Task.IsCompletedSuccessfully == true && renderer.CoreWebView2 is not null;
    }

    private void CancelLayoutChecks()
    {
        _layoutRequests.CancelPending();
        _layoutObservation.Cancel();
        _layoutRecovery.Cancel();
        _layoutObservationCancellation?.Cancel();
    }

    private void LayoutVisibilityChanged(object sender, DependencyPropertyChangedEventArgs eventArguments)
    {
        if (IsVisible == false)
        {
            CancelLayoutChecks();
        }
    }

    private void StartLayoutObservation(object? sender, RoutedEventArgs eventArguments)
    {
        if (isDisposed == true || _layoutTimer is not null)
        {
            return;
        }

        _layoutTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                                           CheckRendererLayout, Dispatcher);
    }

    private void StopLayoutObservation(object? sender, RoutedEventArgs eventArguments)
    {
        CancelLayoutChecks();
        _layoutTimer?.Stop();
        if (_layoutTimer is not null)
        {
            _layoutTimer.Tick -= CheckRendererLayout;
            _layoutTimer = null;
        }
    }

    private void CheckRendererLayout(object? sender, EventArgs eventArguments)
    {
        _layoutObservation.Observe();
    }

    private async Task ObserveRendererLayoutAsync()
    {
        var observedRenderer = renderer;
        var generation = rendererGeneration;
        var revision = _layoutRecovery.Revision;
        var epoch = _layoutObservation.Epoch;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token,
                                                                                 rendererOperationCancellation.Token);
        _layoutObservationCancellation = cancellation;
        try
        {
            var width = RendererHost.ActualWidth;
            var height = RendererHost.ActualHeight;
            var scale = VisualTreeHelper.GetDpi(renderer).DpiScaleX;
            var zoom = renderer.ZoomFactor;
            // A timeout does not cancel WebView2's underlying script. Keep that task so
            // the next tick cannot accumulate pending browser calls.
            _ = _layoutScriptTask?.Exception;
            _layoutScriptTask = observedRenderer.ExecuteScriptAsync(_LayoutScript);
            var json = await _layoutScriptTask.WaitAsync(TimeSpan.FromMilliseconds(750), cancellation.Token);
            if (CanObserveRendererLayout() == false || cancellation.IsCancellationRequested == true ||
                epoch != _layoutObservation.Epoch || revision != _layoutRecovery.Revision ||
                ReferenceEquals(observedRenderer, renderer) == false ||
                generation != rendererGeneration || width != RendererHost.ActualWidth || height != RendererHost.ActualHeight ||
                scale != VisualTreeHelper.GetDpi(renderer).DpiScaleX || zoom != renderer.ZoomFactor)
            {
                return;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var sample = new RendererLayoutSample(width, height, scale, renderer.ActualWidth,
                                                  root.GetProperty("width").GetDouble(), root.GetProperty("scale").GetDouble(),
                                                  root.GetProperty("tab").GetDouble(), root.GetProperty("workspace").GetDouble(),
                                                  root.GetProperty("terminal").GetDouble(), root.GetProperty("scrollbar").GetDouble(),
                                                  root.GetProperty("expectedScrollbar").GetDouble(), zoom);
            _layoutObservationFailureReported = false;
            if (_layoutRecovery.TryRepair(sample) == false)
            {
                return;
            }

            if (_layoutRecovery.Attempts == 1)
            {
                diagnosticLog.Write(DiagnosticLevel.Information, "Terminal", "RecoverRendererLayout",
                                    FormattableString.Invariant($"Host={width}x{height} DIP; control={renderer.ActualWidth} DIP; scale={scale}; DOM={sample.DomWidth}; DPR={sample.DomScale}; tab={sample.TabWidth}; workspace={sample.WorkspaceWidth}; terminal={sample.TerminalWidth}; scrollbar={sample.ScrollbarRight}; expected={sample.ExpectedScrollbarRight}; budget=5/5000ms."));
            }

            RendererHost.InvalidateMeasure();
            renderer.InvalidateMeasure();
            RendererHost.UpdateLayout();
            // Use the supported HwndHost path; leave DPI/controller scale ownership to WebView2.
            renderer.UpdateWindowPos();
            SendGlobalMessage("recover-layout", new { });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested == true)
        {
            // Hiding, unloading, renderer replacement/failure and shutdown invalidate the sample.
            return;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or
                                           TimeoutException or JsonException or KeyNotFoundException or FormatException)
        {
            if (isDisposed == false && _layoutObservationFailureReported == false)
            {
                _layoutObservationFailureReported = true;
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "ObserveRendererLayout",
                                    "Layout observation unavailable; existing resize remains active.", exception);
            }
        }
        finally
        {
            _layoutObservationCancellation = null;
        }
    }
}
