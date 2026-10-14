using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    private DispatcherTimer? _layoutTimer;
    private Task _layoutCheckTask = Task.CompletedTask;
    private Task<string>? _layoutScriptTask;
    private bool _layoutObservationFailureReported;

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
        _layoutTimer?.Stop();
        if (_layoutTimer is not null)
        {
            _layoutTimer.Tick -= CheckRendererLayout;
            _layoutTimer = null;
        }
    }

    private void CheckRendererLayout(object? sender, EventArgs eventArguments)
    {
        if (_layoutCheckTask.IsCompleted == false || isDisposed == true || IsVisible == false ||
            _layoutScriptTask?.IsCompleted == false || rendererFailed == true ||
            rendererReady.Task.IsCompletedSuccessfully == false || renderer.CoreWebView2 is null)
        {
            return;
        }

        _layoutCheckTask = ObserveRendererLayoutAsync();
    }

    private async Task ObserveRendererLayoutAsync()
    {
        var observedRenderer = renderer;
        var generation = rendererGeneration;
        var width = RendererHost.ActualWidth;
        var height = RendererHost.ActualHeight;
        var scale = VisualTreeHelper.GetDpi(renderer).DpiScaleX;
        var zoom = renderer.ZoomFactor;
        try
        {
            // A timeout does not cancel WebView2's underlying script. Keep that task so
            // the next tick cannot accumulate pending browser calls.
            _ = _layoutScriptTask?.Exception;
            _layoutScriptTask = observedRenderer.ExecuteScriptAsync(_LayoutScript);
            var json = await _layoutScriptTask.WaitAsync(TimeSpan.FromMilliseconds(750), lifetimeCancellation.Token);
            if (isDisposed == true || IsVisible == false || ReferenceEquals(observedRenderer, renderer) == false ||
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
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested == true)
        {
            // Shutdown cancels observation before the WebView is disposed.
            return;
        }
        catch (Exception exception)
        {
            if (isDisposed == false && _layoutObservationFailureReported == false)
            {
                _layoutObservationFailureReported = true;
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "ObserveRendererLayout",
                                    $"Layout observation unavailable; existing resize remains active. Error={exception.HResult}.");
            }
        }
    }
}
