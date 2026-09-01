using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration;

public sealed class DesktopIntegrationModule : IDisposable
{
    public const int WindowMessageHotKey = 0x0312;
    public const int WindowMessageDisplayChange = 0x007E;
    public const int WindowMessageSettingChange = 0x001A;
    public const int WindowMessageDpiChanged = 0x02E0;

    private const int ExpandHotKeyIdentifier = 0x5342;
    internal const int ActivationHotKeyIdentifier = 0x5343;

    private readonly IDiagnosticLog diagnosticLog;
    private readonly Lock stateLock = new();

    private System.Threading.Timer? reconciliationTimer;
    private nint windowHandle;
    private PanelOptions options = new(116);
    private TrayIconService? _trayIconService;
    private bool isExpanded;
    private bool _isPanelVisible = true;
    private bool expandHotKeyRegistered;
    private bool activationHotKeyRegistered;
    private bool isDisposed;

    public DesktopIntegrationModule(IDiagnosticLog diagnosticLog)
    {
        this.diagnosticLog = diagnosticLog;
        TaskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
    }

    public uint TaskbarCreatedMessage { get; }

    public event EventHandler? PanelVisibilityToggleRequested;

    public event EventHandler? PanelActivationToggleRequested;

    public event EventHandler? PanelSummonRequested;

    public event EventHandler? ExitRequested;

    public void Attach(nint attachedWindowHandle, PanelOptions panelOptions)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        windowHandle = attachedWindowHandle;
        options = panelOptions;
        isExpanded = panelOptions.StartExpanded;

        _trayIconService = new TrayIconService();
        _trayIconService.ToggleVisibilityRequested += HandleTrayToggleVisibilityRequested;
        _trayIconService.SummonRequested += HandleTraySummonRequested;
        _trayIconService.ExitRequested += HandleTrayExitRequested;

        WindowPlacementService.ConfigureToolWindow(windowHandle);
        expandHotKeyRegistered = NativeMethods.RegisterHotKey(
            windowHandle,
            ExpandHotKeyIdentifier,
            NativeMethods.ModifierControl |
            NativeMethods.ModifierAlt |
            NativeMethods.ModifierNoRepeat,
            NativeMethods.VirtualKeyE);

        if (expandHotKeyRegistered == false)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "RegisterHotKey",
                "Ctrl+Alt+E is already in use; Starboard will continue without the global shortcut.");
        }

        activationHotKeyRegistered = NativeMethods.RegisterHotKey(
            windowHandle,
            ActivationHotKeyIdentifier,
            NativeMethods.ModifierControl |
            NativeMethods.ModifierAlt |
            NativeMethods.ModifierNoRepeat,
            NativeMethods.VirtualKeyS);

        if (activationHotKeyRegistered == false)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "RegisterHotKey",
                "Ctrl+Alt+S is already in use; Starboard will continue without the summon shortcut.");
        }

        Reconcile();
        reconciliationTimer = new System.Threading.Timer(
            static state => ((DesktopIntegrationModule)state!).Reconcile(),
            this,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    public void SetPanelVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        lock (stateLock)
        {
            _isPanelVisible = isVisible;
        }

        _trayIconService?.SetPanelVisible(isVisible);

        if (isVisible == true)
        {
            Reconcile();
        }
    }

    public void ActivatePanel()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (windowHandle == 0)
        {
            return;
        }

        try
        {
            WindowPlacementService.ActivateOnExplicitRequest(windowHandle);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "ActivatePanel",
                "The panel could not be moved to the foreground; it remains visible in the normal window band.",
                exception);
        }
    }

    public bool HandleWindowMessage(int message, nint parameter)
    {
        if (message == WindowMessageHotKey && parameter == ActivationHotKeyIdentifier)
        {
            PanelActivationToggleRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (message == WindowMessageHotKey && parameter == ExpandHotKeyIdentifier)
        {
            ToggleExpanded();
            return true;
        }

        if (message == WindowMessageDisplayChange ||
            message == WindowMessageSettingChange ||
            message == WindowMessageDpiChanged ||
            message == TaskbarCreatedMessage)
        {
            Reconcile();
        }

        return false;
    }

    public void ToggleExpanded()
    {
        lock (stateLock)
        {
            isExpanded = isExpanded == false;
        }

        Reconcile();
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        reconciliationTimer?.Dispose();

        if (_trayIconService is not null)
        {
            _trayIconService.ToggleVisibilityRequested -= HandleTrayToggleVisibilityRequested;
            _trayIconService.SummonRequested -= HandleTraySummonRequested;
            _trayIconService.ExitRequested -= HandleTrayExitRequested;
            _trayIconService.Dispose();
        }

        if (expandHotKeyRegistered == true && windowHandle != 0)
        {
            _ = NativeMethods.UnregisterHotKey(windowHandle, ExpandHotKeyIdentifier);
        }

        if (activationHotKeyRegistered == true && windowHandle != 0)
        {
            _ = NativeMethods.UnregisterHotKey(windowHandle, ActivationHotKeyIdentifier);
        }
    }

    private void Reconcile()
    {
        if (isDisposed == true || windowHandle == 0)
        {
            return;
        }

        try
        {
            var snapshot = TaskbarService.Capture(windowHandle);

            lock (stateLock)
            {
                if (_isPanelVisible == false)
                {
                    return;
                }

                var rectangle = isExpanded == true
                    ? PanelGeometryCalculator.CalculateExpanded(snapshot)
                    : PanelGeometryCalculator.CalculateCollapsed(
                        snapshot,
                        options.CollapsedHeightDip);

                WindowPlacementService.PlaceWithoutActivation(windowHandle, rectangle);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "Reconcile",
                "The panel position could not be refreshed; the last safe position remains active.",
                exception);
        }
    }

    private void HandleTrayToggleVisibilityRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        PanelVisibilityToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTraySummonRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        PanelSummonRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrayExitRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
