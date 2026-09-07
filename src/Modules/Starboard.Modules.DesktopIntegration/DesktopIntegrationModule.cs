using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

[assembly: InternalsVisibleTo("Starboard.IntegrationTests")]

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
    private readonly bool enablePeriodicReconciliation;
    private readonly IDesktopIntegrationRuntime runtime;
    private readonly Lock stateLock = new();

    private System.Threading.Timer? reconciliationTimer;
    private PanelWindowState state = new(
        PanelUserVisibility.Visible,
        PanelMode.Collapsed,
        PanelEngagement.Idle,
        null);
    private PanelOptions options = new(200);
    private PanelWindowPolicyDecision? lastDecision;
    private bool? lastRequestedVisibility;
    private nint windowHandle;
    private bool expandHotKeyRegistered;
    private bool activationHotKeyRegistered;
    private bool geometryFailureReported;
    private bool fullscreenFailureReported;
    private bool isAttached;
    private bool isDisposed;

    public DesktopIntegrationModule(IDiagnosticLog diagnosticLog)
        : this(
            diagnosticLog,
            new WindowsDesktopIntegrationRuntime(diagnosticLog),
            true)
    {
    }

    internal DesktopIntegrationModule(
        IDiagnosticLog diagnosticLog,
        IDesktopIntegrationRuntime runtime,
        bool enablePeriodicReconciliation)
    {
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        ArgumentNullException.ThrowIfNull(runtime);

        this.diagnosticLog = diagnosticLog;
        this.runtime = runtime;
        this.enablePeriodicReconciliation = enablePeriodicReconciliation;
        TaskbarCreatedMessage = runtime.TaskbarCreatedMessage;
    }

    public uint TaskbarCreatedMessage { get; }

    public event EventHandler? PanelVisibilityToggleRequested;

    public event EventHandler? PanelActivationToggleRequested;

    public event EventHandler? PanelSummonRequested;

    public event Action<bool>? PanelPresentationRequested;

    public event EventHandler? ExitRequested;

    public void Attach(nint attachedWindowHandle, PanelOptions panelOptions)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(panelOptions);

        if (attachedWindowHandle == 0)
        {
            throw new ArgumentException(
                "A valid panel window handle is required.",
                nameof(attachedWindowHandle));
        }

        if (isAttached == true)
        {
            throw new InvalidOperationException(
                "Desktop integration is already attached to a panel window.");
        }

        windowHandle = attachedWindowHandle;
        options = panelOptions;
        state = state with
        {
            Mode = panelOptions.StartExpanded == true
                ? PanelMode.Expanded
                : PanelMode.Collapsed,
        };

        runtime.EnvironmentChanged += HandleEnvironmentChanged;
        runtime.ForegroundChanged += HandleForegroundChanged;
        runtime.TrayToggleVisibilityRequested += HandleTrayToggleVisibilityRequested;
        runtime.TraySummonRequested += HandleTraySummonRequested;
        runtime.TrayExitRequested += HandleTrayExitRequested;

        try
        {
            runtime.Attach(windowHandle);
            isAttached = true;
            expandHotKeyRegistered = runtime.RegisterHotKey(
                windowHandle,
                ExpandHotKeyIdentifier,
                NativeMethods.VirtualKeyE);
            activationHotKeyRegistered = runtime.RegisterHotKey(
                windowHandle,
                ActivationHotKeyIdentifier,
                NativeMethods.VirtualKeyS);
        }
        catch
        {
            UnsubscribeRuntimeEvents();
            throw;
        }

        if (expandHotKeyRegistered == false)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "RegisterHotKey",
                "Ctrl+Alt+E is already in use; Starboard will continue without the global shortcut.");
        }

        if (activationHotKeyRegistered == false)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "RegisterHotKey",
                "Ctrl+Alt+S is already in use; Starboard will continue without the summon shortcut.");
        }

        Reconcile();
        if (enablePeriodicReconciliation == true)
        {
            reconciliationTimer = new System.Threading.Timer(
                static module => ((DesktopIntegrationModule)module!).Reconcile(),
                this,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
        }
    }

    public void SetPanelVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        lock (stateLock)
        {
            state = isVisible == true
                ? state.ShowOnExplicitUserRequest()
                : state.HideOnExplicitUserRequest();
        }

        try
        {
            runtime.SetTrayPanelVisible(isVisible);
        }
        catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "UpdateTrayIcon",
                "The notification icon state could not be updated; panel policy remains active.",
                exception);
        }

        Reconcile();
    }

    public bool TogglePanelVisibility()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        bool isVisible;
        lock (stateLock)
        {
            isVisible = state.UserVisibility == PanelUserVisibility.Hidden;
        }

        SetPanelVisible(isVisible);

        return isVisible;
    }

    public void SetPanelEngaged(bool isEngaged)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        lock (stateLock)
        {
            state = state.SetEngagement(
                isEngaged == true
                    ? PanelEngagement.Active
                    : PanelEngagement.Idle);
        }

        Reconcile();
    }

    public void ActivatePanel()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        lock (stateLock)
        {
            state = state.SetEngagement(PanelEngagement.Active);
        }

        Reconcile();

        lock (stateLock)
        {
            if (windowHandle == 0 ||
                lastDecision?.Presentation != PanelWindowPresentation.Visible)
            {
                return;
            }

            try
            {
                runtime.ActivateOnExplicitRequest(windowHandle);
            }
            catch (Win32Exception exception)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "DesktopIntegration",
                    "ActivatePanel",
                    "The panel could not be moved to the foreground; it remains visible in the normal window band.",
                    exception);
            }
        }
    }

    public bool HandleWindowMessage(WindowMessage message)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (message.MessageId == WindowMessageHotKey &&
            message.WordParameter == ActivationHotKeyIdentifier)
        {
            PanelActivationToggleRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (message.MessageId == WindowMessageHotKey &&
            message.WordParameter == ExpandHotKeyIdentifier)
        {
            ToggleExpanded();
            return true;
        }

        if (message.MessageId == WindowMessageDpiChanged)
        {
            _ = DpiChangedMessageAdapter.Capture(message);
            Reconcile();
            return false;
        }

        if (message.MessageId == WindowMessageDisplayChange)
        {
            _ = DisplayChangedWindowMessage.FromWindowMessage(message);
            Reconcile();
            return false;
        }

        if (message.MessageId == WindowMessageSettingChange)
        {
            Reconcile();
            return false;
        }

        if (TaskbarCreatedMessage != 0 &&
            unchecked((uint)message.MessageId) == TaskbarCreatedMessage)
        {
            bool isUserVisible;
            lock (stateLock)
            {
                isUserVisible = state.UserVisibility == PanelUserVisibility.Visible;
            }

            try
            {
                runtime.RecreateTrayIcon(isUserVisible);
            }
            catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "DesktopIntegration",
                    "RecreateTrayIcon",
                    "Explorer restarted, but the notification icon could not be recreated; global shortcuts remain active.",
                    exception);
            }

            Reconcile();
        }

        return false;
    }

    public bool HandleWindowMessage(int message, nint parameter)
    {
        return HandleWindowMessage(new WindowMessage(
            windowHandle,
            message,
            unchecked((nuint)parameter),
            parameter));
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        Reconcile();
    }

    public void ToggleExpanded()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        lock (stateLock)
        {
            state = state.ToggleMode();
        }

        Reconcile();
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (isDisposed == true)
            {
                return;
            }

            isDisposed = true;
        }

        reconciliationTimer?.Dispose();

        lock (stateLock)
        {
            UnsubscribeRuntimeEvents();

            if (expandHotKeyRegistered == true && windowHandle != 0)
            {
                runtime.UnregisterHotKey(windowHandle, ExpandHotKeyIdentifier);
            }

            if (activationHotKeyRegistered == true && windowHandle != 0)
            {
                runtime.UnregisterHotKey(windowHandle, ActivationHotKeyIdentifier);
            }

            runtime.Dispose();
        }
    }

    private void Reconcile()
    {
        bool? requestedVisibility = null;

        lock (stateLock)
        {
            if (isDisposed == true || windowHandle == 0)
            {
                return;
            }

            try
            {
                var geometry = runtime.CaptureGeometry(windowHandle);
                var fullscreen = runtime.CaptureFullscreen(
                    windowHandle,
                    geometry.Monitor);
                LogCaptureRecovery(geometry, fullscreen);

                if (state.Mode == PanelMode.Collapsed &&
                    state.LastSafeCollapsedBounds is null)
                {
                    state = state.RememberSafeCollapsedBounds(
                        CalculateCollapsed(geometry));
                }

                var input = new PanelWindowPolicyInput(
                    state.UserVisibility,
                    fullscreen.State,
                    state.Mode,
                    state.Engagement,
                    geometry.TaskbarPresence,
                    geometry.TrackingState,
                    geometry.Monitor,
                    state.LastSafeCollapsedBounds);
                var decision = PanelWindowPolicy.Decide(input);
                ApplyGeometry(decision, geometry);
                lastDecision = decision;

                var shouldBeVisible = decision.Presentation == PanelWindowPresentation.Visible;
                if (lastRequestedVisibility != shouldBeVisible)
                {
                    lastRequestedVisibility = shouldBeVisible;
                    requestedVisibility = shouldBeVisible;
                }

            }
            catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
            {
                if (geometryFailureReported == false)
                {
                    geometryFailureReported = true;
                    diagnosticLog.Write(
                        DiagnosticLevel.Warning,
                        "DesktopIntegration",
                        "Reconcile",
                        "The panel state could not be refreshed; the last safe position and foreground owner remain unchanged.",
                        exception);
                }
            }
        }

        if (requestedVisibility is bool isVisible)
        {
            PanelPresentationRequested?.Invoke(isVisible);
        }
    }

    private void ApplyGeometry(
        PanelWindowPolicyDecision decision,
        DesktopGeometrySnapshot geometry)
    {
        PixelRect? targetBounds = decision.GeometryAction switch
        {
            PanelWindowGeometryAction.ApplyCollapsed => CalculateCollapsed(geometry),
            PanelWindowGeometryAction.RestoreCollapsed => SelectRestoredOrLatestBounds(
                decision.TargetBounds,
                geometry),
            PanelWindowGeometryAction.ApplyExpanded => decision.TargetBounds ??
                PanelGeometryCalculator.CalculateExpanded(geometry.Taskbar),
            _ => null,
        };

        if (targetBounds is not PixelRect bounds || bounds.IsEmpty == true)
        {
            return;
        }

        runtime.PlaceWithoutActivation(windowHandle, bounds);
        if (decision.GeometryAction is PanelWindowGeometryAction.ApplyCollapsed or
            PanelWindowGeometryAction.RestoreCollapsed)
        {
            state = state.RememberSafeCollapsedBounds(bounds);
        }
    }

    private PixelRect SelectRestoredOrLatestBounds(
        PixelRect? requestedBounds,
        DesktopGeometrySnapshot geometry)
    {
        if (requestedBounds is PixelRect bounds &&
            IsWithinWorkArea(bounds, geometry.Monitor.WorkArea) == true)
        {
            return bounds;
        }

        return CalculateCollapsed(geometry);
    }

    private PixelRect CalculateCollapsed(DesktopGeometrySnapshot geometry)
    {
        return PanelGeometryCalculator.CalculateCollapsed(
            geometry.Taskbar,
            geometry.Monitor.Dpi,
            options.CollapsedHeightDip);
    }

    private void LogCaptureRecovery(
        DesktopGeometrySnapshot geometry,
        FullscreenObservation fullscreen)
    {
        var geometryFailure = geometry.GeometryCaptureFailure ?? geometry.DpiCaptureFailure;
        if (geometryFailure is not null && geometryFailureReported == false)
        {
            geometryFailureReported = true;
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "CaptureGeometry",
                "Windows display data was incomplete; the current connected-monitor fallback is in use.",
                geometryFailure);
        }

        if (geometryFailure is null)
        {
            geometryFailureReported = false;
        }

        if (fullscreen.IsReliable == false && fullscreenFailureReported == false)
        {
            fullscreenFailureReported = true;
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "CaptureFullscreen",
                "Fullscreen detection is temporarily unavailable; normal z-order and foreground ownership are preserved.",
                fullscreen.Failure);
        }

        if (fullscreen.IsReliable == true)
        {
            fullscreenFailureReported = false;
        }
    }

    private void HandleEnvironmentChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        Reconcile();
    }

    private void HandleForegroundChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        Reconcile();
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

    private void UnsubscribeRuntimeEvents()
    {
        runtime.EnvironmentChanged -= HandleEnvironmentChanged;
        runtime.ForegroundChanged -= HandleForegroundChanged;
        runtime.TrayToggleVisibilityRequested -= HandleTrayToggleVisibilityRequested;
        runtime.TraySummonRequested -= HandleTraySummonRequested;
        runtime.TrayExitRequested -= HandleTrayExitRequested;
    }

    private static bool IsWithinWorkArea(PixelRect bounds, PixelRect workArea)
    {
        return bounds.IsEmpty == false &&
            bounds.Left >= workArea.Left &&
            bounds.Top >= workArea.Top &&
            bounds.Right <= workArea.Right &&
            bounds.Bottom <= workArea.Bottom;
    }

    internal static bool IsRecoverablePlatformFailure(Exception exception)
    {
        return exception is InvalidOperationException or
            ExternalException or
            ArgumentException or
            ObjectDisposedException or
            DllNotFoundException or
            EntryPointNotFoundException;
    }
}

internal interface IDesktopIntegrationRuntime : IDisposable
{
    uint TaskbarCreatedMessage { get; }

    event EventHandler? EnvironmentChanged;

    event EventHandler? ForegroundChanged;

    event EventHandler? TrayToggleVisibilityRequested;

    event EventHandler? TraySummonRequested;

    event EventHandler? TrayExitRequested;

    void Attach(nint windowHandle);

    bool RegisterHotKey(nint windowHandle, int identifier, uint virtualKey);

    void UnregisterHotKey(nint windowHandle, int identifier);

    DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle);

    FullscreenObservation CaptureFullscreen(
        nint panelWindowHandle,
        MonitorSnapshot panelMonitor);

    void PlaceWithoutActivation(nint windowHandle, PixelRect bounds);

    void ActivateOnExplicitRequest(nint windowHandle);

    void SetTrayPanelVisible(bool isVisible);

    void RecreateTrayIcon(bool isVisible);
}

internal sealed class WindowsDesktopIntegrationRuntime : IDesktopIntegrationRuntime
{
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly FullscreenService _fullscreenService;
    private readonly TaskbarService _taskbarService;

    private DisplaySettingsObserver? _displaySettingsObserver;
    private ForegroundWindowObserver? _foregroundWindowObserver;
    private TrayIconService? _trayIconService;
    private bool _isDisposed;

    internal WindowsDesktopIntegrationRuntime(IDiagnosticLog diagnosticLog)
    {
        _diagnosticLog = diagnosticLog;
        var nativeApi = new DesktopNativeApi();
        _fullscreenService = new FullscreenService(nativeApi);
        _taskbarService = new TaskbarService(nativeApi);
        TaskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        if (TaskbarCreatedMessage == 0)
        {
            _diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "DesktopIntegration",
                "RegisterTaskbarCreatedMessage",
                "Explorer restart notifications are unavailable; periodic reconciliation remains active.");
        }
    }

    public uint TaskbarCreatedMessage { get; }

    public event EventHandler? EnvironmentChanged;

    public event EventHandler? ForegroundChanged;

    public event EventHandler? TrayToggleVisibilityRequested;

    public event EventHandler? TraySummonRequested;

    public event EventHandler? TrayExitRequested;

    public void Attach(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        WindowPlacementService.ConfigureToolWindow(windowHandle);
        CreateTrayIcon(true);

        try
        {
            _displaySettingsObserver = new DisplaySettingsObserver();
            _displaySettingsObserver.DisplaySettingsChanged += HandleEnvironmentChanged;
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            LogObserverFailure(
                "ObserveDisplaySettings",
                "Display-settings notifications are unavailable; periodic reconciliation remains active.",
                exception);
        }

        try
        {
            _foregroundWindowObserver = new ForegroundWindowObserver();
            _foregroundWindowObserver.ForegroundChanged += HandleForegroundChanged;
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            LogObserverFailure(
                "ObserveForeground",
                "Foreground notifications are unavailable; periodic fullscreen reconciliation remains active.",
                exception);
        }
    }

    public bool RegisterHotKey(nint windowHandle, int identifier, uint virtualKey)
    {
        return NativeMethods.RegisterHotKey(
            windowHandle,
            identifier,
            NativeMethods.ModifierControl |
            NativeMethods.ModifierAlt |
            NativeMethods.ModifierNoRepeat,
            virtualKey);
    }

    public void UnregisterHotKey(nint windowHandle, int identifier)
    {
        _ = NativeMethods.UnregisterHotKey(windowHandle, identifier);
    }

    public DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle)
    {
        return _taskbarService.CaptureLatest(panelWindowHandle);
    }

    public FullscreenObservation CaptureFullscreen(
        nint panelWindowHandle,
        MonitorSnapshot panelMonitor)
    {
        return _fullscreenService.Capture(panelWindowHandle, panelMonitor);
    }

    public void PlaceWithoutActivation(nint windowHandle, PixelRect bounds)
    {
        WindowPlacementService.PlaceWithoutActivation(windowHandle, bounds);
    }

    public void ActivateOnExplicitRequest(nint windowHandle)
    {
        WindowPlacementService.ActivateOnExplicitRequest(windowHandle);
    }

    public void SetTrayPanelVisible(bool isVisible)
    {
        _trayIconService?.SetPanelVisible(isVisible);
    }

    public void RecreateTrayIcon(bool isVisible)
    {
        DisposeTrayIcon();
        CreateTrayIcon(isVisible);
    }

    public void Dispose()
    {
        if (_isDisposed == true)
        {
            return;
        }

        _isDisposed = true;

        if (_displaySettingsObserver is not null)
        {
            _displaySettingsObserver.DisplaySettingsChanged -= HandleEnvironmentChanged;
            DisposeObserver(
                _displaySettingsObserver,
                "DisposeDisplaySettingsObserver");
        }

        if (_foregroundWindowObserver is not null)
        {
            _foregroundWindowObserver.ForegroundChanged -= HandleForegroundChanged;
            DisposeObserver(
                _foregroundWindowObserver,
                "DisposeForegroundObserver");
        }

        DisposeTrayIcon();
    }

    private void CreateTrayIcon(bool isVisible)
    {
        try
        {
            _trayIconService = new TrayIconService();
            _trayIconService.ToggleVisibilityRequested += HandleTrayToggleVisibilityRequested;
            _trayIconService.SummonRequested += HandleTraySummonRequested;
            _trayIconService.ExitRequested += HandleTrayExitRequested;
            _trayIconService.SetPanelVisible(isVisible);
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            _trayIconService = null;
            LogObserverFailure(
                "CreateTrayIcon",
                "The notification icon is unavailable; global shortcuts remain active.",
                exception);
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIconService is null)
        {
            return;
        }

        _trayIconService.ToggleVisibilityRequested -= HandleTrayToggleVisibilityRequested;
        _trayIconService.SummonRequested -= HandleTraySummonRequested;
        _trayIconService.ExitRequested -= HandleTrayExitRequested;
        _trayIconService.Dispose();
        _trayIconService = null;
    }

    private void DisposeObserver(IDisposable observer, string operation)
    {
        try
        {
            observer.Dispose();
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            LogObserverFailure(
                operation,
                "A Windows observer could not be released cleanly during shutdown.",
                exception);
        }
    }

    private void HandleEnvironmentChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        EnvironmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleForegroundChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        ForegroundChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrayToggleVisibilityRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        TrayToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTraySummonRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        TraySummonRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrayExitRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        TrayExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LogObserverFailure(
        string operation,
        string message,
        Exception exception)
    {
        _diagnosticLog.Write(
            DiagnosticLevel.Warning,
            "DesktopIntegration",
            operation,
            message,
            exception);
    }
}
