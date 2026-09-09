using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed record FullscreenObservation(PanelFullscreenState State, bool IsReliable, Exception? Failure,
                                             PanelWindowActivation Activation, PanelWindowZOrder ZOrder);

internal sealed class FullscreenService
{
    private readonly IDesktopNativeApi _nativeApi;

    internal FullscreenService(IDesktopNativeApi nativeApi)
    {
        _nativeApi = nativeApi;
    }

    internal FullscreenObservation Capture(nint panelWindowHandle, MonitorSnapshot panelMonitor)
    {
        try
        {
            var foregroundWindow = _nativeApi.GetForegroundWindow();
            if (foregroundWindow == 0 || foregroundWindow == panelWindowHandle ||
                foregroundWindow == _nativeApi.GetShellWindow())
            {
                return NormalObservation();
            }

            if (_nativeApi.IsWindowVisible(foregroundWindow) == false ||
                _nativeApi.IsWindowMinimized(foregroundWindow) == true ||
                _nativeApi.IsWindowCloaked(foregroundWindow) == true)
            {
                return NormalObservation();
            }

            var foregroundMonitor = _nativeApi.GetMonitorForWindow(foregroundWindow);
            if (foregroundMonitor == 0)
            {
                throw new InvalidOperationException("The foreground window is not associated with a connected monitor.");
            }

            if (foregroundMonitor != panelMonitor.Handle)
            {
                return NormalObservation();
            }

            var foregroundBounds = _nativeApi.GetWindowBounds(foregroundWindow);
            var coversMonitor = foregroundBounds.Left <= panelMonitor.Bounds.Left &&
                foregroundBounds.Top <= panelMonitor.Bounds.Top &&
                foregroundBounds.Right >= panelMonitor.Bounds.Right &&
                foregroundBounds.Bottom >= panelMonitor.Bounds.Bottom;

            return new FullscreenObservation(coversMonitor == true
                                                 ? PanelFullscreenState.FullscreenOnPanelMonitor
                                                 : PanelFullscreenState.Normal,
                                             true, null, PanelWindowActivation.PreserveForeground,
                                             PanelWindowZOrder.PreserveNormal);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.ExternalException or ArgumentException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Detection is advisory. On failure the caller keeps normal z-order and
            // foreground ownership; it must not compensate by activating or pinning.
            return new FullscreenObservation(PanelFullscreenState.Normal, false, exception,
                                             PanelWindowActivation.PreserveForeground, PanelWindowZOrder.PreserveNormal);
        }
    }

    private static FullscreenObservation NormalObservation()
    {
        return new FullscreenObservation(PanelFullscreenState.Normal, true, null,
                                         PanelWindowActivation.PreserveForeground, PanelWindowZOrder.PreserveNormal);
    }
}
