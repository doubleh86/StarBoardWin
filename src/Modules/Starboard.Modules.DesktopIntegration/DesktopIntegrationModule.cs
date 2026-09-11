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
    private const double _DefaultOpacity = 0.97;

    private readonly IDiagnosticLog diagnosticLog;
    private readonly bool enablePeriodicReconciliation;
    private readonly IDesktopIntegrationRuntime runtime;
    private readonly IStartupRegistration startupRegistration;
    private readonly Lock stateLock = new();

    private System.Threading.Timer? reconciliationTimer;
    private PanelWindowState state = new(PanelUserVisibility.Visible, PanelMode.Collapsed, PanelEngagement.Idle, null);
    private PanelOptions options = new(200);
    private PanelWindowPolicyDecision? lastDecision;
    private DesktopSettings effectiveSettings = CreateDefaultSettings();
    private HotkeySettings configuredHotkeys = new("Ctrl+Alt+E", "Ctrl+Alt+S");
    private string? expandHotkeyFailure;
    private string? activationHotkeyFailure;
    private bool? lastRequestedVisibility;
    private nint windowHandle;
    private bool expandHotKeyRegistered;
    private bool activationHotKeyRegistered;
    private bool geometryFailureReported;
    private bool fullscreenFailureReported;
    private bool isAttached;
    private bool isDisposed;

    public DesktopIntegrationModule(IDiagnosticLog diagnosticLog, Action<double> applyPanelOpacity)
        : this(diagnosticLog, new WindowsDesktopIntegrationRuntime(diagnosticLog, applyPanelOpacity),
               new RegistryStartupRegistration(new ProcessExecutablePathProvider()), true)
    {
    }

    internal DesktopIntegrationModule(IDiagnosticLog diagnosticLog, IDesktopIntegrationRuntime runtime,
                                      bool enablePeriodicReconciliation)
        : this(diagnosticLog, runtime, new RegistryStartupRegistration(new ProcessExecutablePathProvider()),
               enablePeriodicReconciliation)
    {
    }

    internal DesktopIntegrationModule(IDiagnosticLog diagnosticLog, IDesktopIntegrationRuntime runtime,
                                      IStartupRegistration startupRegistration, bool enablePeriodicReconciliation)
    {
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(startupRegistration);

        this.diagnosticLog = diagnosticLog;
        this.runtime = runtime;
        this.startupRegistration = startupRegistration;
        this.enablePeriodicReconciliation = enablePeriodicReconciliation;
        TaskbarCreatedMessage = runtime.TaskbarCreatedMessage;
    }

    public uint TaskbarCreatedMessage { get; }

    public event EventHandler? PanelVisibilityToggleRequested;

    public event EventHandler? PanelActivationToggleRequested;

    public event EventHandler? PanelSummonRequested;

    /// <summary>
    /// Raised when the Desktop-owned tray menu requests that the host open Preferences.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Raised only after the tray menu explicitly requests the shortcut guide.
    /// </summary>
    public event EventHandler<ShortcutGuideRequestEventArgs>? ShortcutGuideRequested;

    public event Action<bool>? PanelPresentationRequested;

    public event EventHandler? ExitRequested;

    public void Attach(nint attachedWindowHandle, PanelOptions panelOptions)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(panelOptions);

        if (attachedWindowHandle == 0)
        {
            throw new ArgumentException("A valid panel window handle is required.", nameof(attachedWindowHandle));
        }

        if (isAttached == true)
        {
            throw new InvalidOperationException("Desktop integration is already attached to a panel window.");
        }

        windowHandle = attachedWindowHandle;
        options = panelOptions;
        effectiveSettings = effectiveSettings with
        {
            CollapsedHeightDip = panelOptions.CollapsedHeightDip,
        };
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
        runtime.TraySettingsRequested += HandleTraySettingsRequested;
        runtime.TrayShortcutGuideRequested += HandleTrayShortcutGuideRequested;
        runtime.TrayExitRequested += HandleTrayExitRequested;

        try
        {
            runtime.Attach(windowHandle);
            isAttached = true;
            runtime.SetPanelOpacity(windowHandle, effectiveSettings.Opacity);
            RegisterInitialHotkeys();
        }
        catch
        {
            UnsubscribeRuntimeEvents();

            throw;
        }

        Reconcile();
        if (enablePeriodicReconciliation == true)
        {
            reconciliationTimer = new System.Threading.Timer(static module => ((DesktopIntegrationModule)module!).Reconcile(),
                                                             this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "UpdateTrayIcon",
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
            state = state.SetEngagement(isEngaged == true
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
            if (windowHandle == 0 || lastDecision?.Presentation != PanelWindowPresentation.Visible)
            {
                return;
            }

            try
            {
                runtime.ActivateOnExplicitRequest(windowHandle);
            }
            catch (Win32Exception exception)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "ActivatePanel",
                                    "The panel could not be moved to the foreground; it remains visible in the normal window band.",
                                    exception);
            }
        }
    }

    public bool HandleWindowMessage(WindowMessage message)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (message.MessageId == WindowMessageHotKey && message.WordParameter == ActivationHotKeyIdentifier)
        {
            PanelActivationToggleRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (message.MessageId == WindowMessageHotKey && message.WordParameter == ExpandHotKeyIdentifier)
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

        if (TaskbarCreatedMessage != 0 && unchecked((uint)message.MessageId) == TaskbarCreatedMessage)
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
                diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "RecreateTrayIcon",
                                    "Explorer restarted, but the notification icon could not be recreated; global shortcuts remain active.",
                                    exception);
            }

            Reconcile();
        }

        return false;
    }

    public bool HandleWindowMessage(int message, nint parameter)
    {
        return HandleWindowMessage(new WindowMessage(windowHandle, message, unchecked((nuint)parameter), parameter));
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        Reconcile();
    }

    public DesktopSettingsApplyResult ApplySettings(DesktopSettings requestedSettings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(requestedSettings);

        lock (stateLock)
        {
            if (isAttached == false)
            {
                throw new InvalidOperationException("Desktop settings are unavailable before the panel is attached.");
            }

            var previousSettings = effectiveSettings;
            if (TryValidateSettings(requestedSettings, out var requestedHotkeys, out var validationFailure) == false)
            {
                return CreateValidationFailureResult(requestedSettings, previousSettings, validationFailure!);
            }

            var operations = new List<DesktopSettingsOperationResult>(4);
            if (TryApplyPanelAppearance(requestedSettings, previousSettings, out var appearanceFailure) == false)
            {
                operations.Add(new DesktopSettingsOperationResult(DesktopSettingsOperation.PanelAppearance,
                                                                  DesktopSettingsOperationStatus.Failed,
                                                                  appearanceFailure));
                AddUnchangedOperations(operations, DesktopSettingsOperation.MonitorBehavior);
                AddUnchangedOperations(operations, DesktopSettingsOperation.Hotkeys);
                AddUnchangedOperations(operations, DesktopSettingsOperation.Startup);

                return new DesktopSettingsApplyResult(requestedSettings, previousSettings, previousSettings,
                                                      DesktopSettingsApplyStatus.FailedWithoutChange, operations);
            }

            var appearanceChanged = IsAppearanceChanged(requestedSettings, previousSettings);
            operations.Add(new DesktopSettingsOperationResult(DesktopSettingsOperation.PanelAppearance,
                                                              appearanceChanged == true
                                                                  ? DesktopSettingsOperationStatus.Applied
                                                                  : DesktopSettingsOperationStatus.Unchanged,
                                                              null));
            operations.Add(new DesktopSettingsOperationResult(DesktopSettingsOperation.MonitorBehavior,
                                                              DesktopSettingsOperationStatus.Unchanged, null));

            var hotkeysChanged = requestedSettings.Hotkeys != previousSettings.Hotkeys;
            var actualHotkeys = previousSettings.Hotkeys;
            string? hotkeyFailure = null;
            var hotkeysApplied = hotkeysChanged == false ||
                TryReplaceHotkeys(requestedHotkeys, out actualHotkeys, out hotkeyFailure);
            if (hotkeysApplied == false)
            {
                var appearanceRestored = RestorePanelAppearance(previousSettings);
                operations[0] = operations[0] with
                {
                    Status = appearanceChanged == true
                        ? appearanceRestored == true
                            ? DesktopSettingsOperationStatus.Restored
                            : DesktopSettingsOperationStatus.RestoreFailed
                        : DesktopSettingsOperationStatus.Unchanged,
                };
                operations.Add(new DesktopSettingsOperationResult(DesktopSettingsOperation.Hotkeys,
                                                                  actualHotkeys == previousSettings.Hotkeys
                                                                      ? DesktopSettingsOperationStatus.Restored
                                                                      : DesktopSettingsOperationStatus.RestoreFailed,
                                                                  hotkeyFailure));
                AddUnchangedOperations(operations, DesktopSettingsOperation.Startup);
                var actualSettings = previousSettings with
                {
                    CollapsedHeightDip = appearanceRestored == true
                        ? previousSettings.CollapsedHeightDip
                        : requestedSettings.CollapsedHeightDip,
                    Opacity = appearanceRestored == true
                        ? previousSettings.Opacity
                        : requestedSettings.Opacity,
                    Hotkeys = actualHotkeys,
                };
                effectiveSettings = actualSettings;

                return new DesktopSettingsApplyResult(requestedSettings, previousSettings, actualSettings,
                                                      appearanceRestored == true && actualSettings == previousSettings
                                                          ? DesktopSettingsApplyStatus.FailedAndRestored
                                                          : DesktopSettingsApplyStatus.FailedAndRestoreIncomplete,
                                                      operations);
            }

            operations.Add(new DesktopSettingsOperationResult(DesktopSettingsOperation.Hotkeys,
                                                              hotkeysChanged == true
                                                                  ? DesktopSettingsOperationStatus.Applied
                                                                  : DesktopSettingsOperationStatus.Unchanged,
                                                              null));

            try
            {
                if (requestedSettings.Startup != previousSettings.Startup)
                {
                    startupRegistration.SetEnabled(requestedSettings.Startup.StartWithWindows);
                }
            }
            catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
            {
                var startupRestored = TryRestoreStartup(previousSettings.Startup);
                var hotkeysRestored = true;
                if (hotkeysChanged == true)
                {
                    UnregisterCurrentHotkeys();
                    hotkeysRestored = TryRestoreHotkeys(previousSettings.Hotkeys, out actualHotkeys);
                }

                var appearanceRestored = RestorePanelAppearance(previousSettings);
                operations[0] = operations[0] with
                {
                    Status = appearanceChanged == true
                        ? appearanceRestored == true
                            ? DesktopSettingsOperationStatus.Restored
                            : DesktopSettingsOperationStatus.RestoreFailed
                        : DesktopSettingsOperationStatus.Unchanged,
                };
                operations[2] = operations[2] with
                {
                    Status = hotkeysChanged == false
                        ? DesktopSettingsOperationStatus.Unchanged
                        : hotkeysRestored == true
                            ? DesktopSettingsOperationStatus.Restored
                            : DesktopSettingsOperationStatus.RestoreFailed,
                    FailureMessage = hotkeysChanged == true && hotkeysRestored == false
                        ? "One or more previous shortcuts could not be restored."
                        : null,
                };
                operations.Add(new DesktopSettingsOperationResult(DesktopSettingsOperation.Startup,
                                                                  startupRestored == true
                                                                      ? DesktopSettingsOperationStatus.Restored
                                                                      : DesktopSettingsOperationStatus.RestoreFailed,
                                                                  exception.Message));
                var actualSettings = previousSettings with
                {
                    CollapsedHeightDip = appearanceRestored == true
                        ? previousSettings.CollapsedHeightDip
                        : requestedSettings.CollapsedHeightDip,
                    Opacity = appearanceRestored == true
                        ? previousSettings.Opacity
                        : requestedSettings.Opacity,
                    Hotkeys = actualHotkeys,
                    Startup = startupRestored == true
                        ? previousSettings.Startup
                        : requestedSettings.Startup,
                };
                effectiveSettings = actualSettings;

                return new DesktopSettingsApplyResult(requestedSettings, previousSettings, actualSettings,
                                                      actualSettings == previousSettings
                                                          ? DesktopSettingsApplyStatus.FailedAndRestored
                                                          : DesktopSettingsApplyStatus.FailedAndRestoreIncomplete,
                                                      operations);
            }

            effectiveSettings = requestedSettings with
            {
                Hotkeys = actualHotkeys,
            };
            configuredHotkeys = requestedSettings.Hotkeys;
            expandHotkeyFailure = null;
            activationHotkeyFailure = null;

            return new DesktopSettingsApplyResult(requestedSettings, previousSettings, effectiveSettings,
                                                  DesktopSettingsApplyStatus.Applied,
                                                  operations.Append(new DesktopSettingsOperationResult(DesktopSettingsOperation.Startup,
                                                                                                       requestedSettings.Startup == previousSettings.Startup
                                                                                                           ? DesktopSettingsOperationStatus.Unchanged
                                                                                                           : DesktopSettingsOperationStatus.Applied,
                                                                                                       null)).ToArray());
        }
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
                var fullscreen = runtime.CaptureFullscreen(windowHandle, geometry.Monitor);
                LogCaptureRecovery(geometry, fullscreen);

                if (state.Mode == PanelMode.Collapsed && state.LastSafeCollapsedBounds is null)
                {
                    state = state.RememberSafeCollapsedBounds(CalculateCollapsed(geometry));
                }

                var input = new PanelWindowPolicyInput(state.UserVisibility, fullscreen.State, state.Mode,
                                                       state.Engagement, geometry.TaskbarPresence,
                                                       geometry.TrackingState, geometry.Monitor,
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
                    diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "Reconcile",
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

    private void ApplyGeometry(PanelWindowPolicyDecision decision, DesktopGeometrySnapshot geometry)
    {
        PixelRect? targetBounds = decision.GeometryAction switch
        {
            PanelWindowGeometryAction.ApplyCollapsed => CalculateCollapsed(geometry),
            PanelWindowGeometryAction.RestoreCollapsed => SelectRestoredOrLatestBounds(decision.TargetBounds, geometry),
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

    private PixelRect SelectRestoredOrLatestBounds(PixelRect? requestedBounds, DesktopGeometrySnapshot geometry)
    {
        if (requestedBounds is PixelRect bounds && IsWithinWorkArea(bounds, geometry.Monitor.WorkArea) == true)
        {
            return bounds;
        }

        return CalculateCollapsed(geometry);
    }

    private PixelRect CalculateCollapsed(DesktopGeometrySnapshot geometry)
    {
        return PanelGeometryCalculator.CalculateCollapsed(geometry.Taskbar, geometry.Monitor.Dpi,
                                                          options.CollapsedHeightDip);
    }

    private void LogCaptureRecovery(DesktopGeometrySnapshot geometry, FullscreenObservation fullscreen)
    {
        var geometryFailure = geometry.GeometryCaptureFailure ?? geometry.DpiCaptureFailure;
        if (geometryFailure is not null && geometryFailureReported == false)
        {
            geometryFailureReported = true;
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "CaptureGeometry",
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
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "CaptureFullscreen",
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

    private void HandleTraySettingsRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrayShortcutGuideRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        GlobalShortcutRegistrationSnapshot snapshot;
        lock (stateLock)
        {
            snapshot = CreateShortcutRegistrationSnapshot();
        }

        ShortcutGuideRequested?.Invoke(this, new ShortcutGuideRequestEventArgs(snapshot));
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
        runtime.TraySettingsRequested -= HandleTraySettingsRequested;
        runtime.TrayShortcutGuideRequested -= HandleTrayShortcutGuideRequested;
        runtime.TrayExitRequested -= HandleTrayExitRequested;
    }

    private void RegisterInitialHotkeys()
    {
        var requestedHotkeys = ToParsedHotkeys(effectiveSettings.Hotkeys);
        expandHotKeyRegistered = runtime.RegisterHotKey(windowHandle, ExpandHotKeyIdentifier, requestedHotkeys.Expand);
        activationHotKeyRegistered = runtime.RegisterHotKey(windowHandle, ActivationHotKeyIdentifier,
                                                            requestedHotkeys.Activation);
        effectiveSettings = effectiveSettings with
        {
            Hotkeys = new HotkeySettings(expandHotKeyRegistered == true
                                             ? requestedHotkeys.Expand.DisplayText
                                             : string.Empty,
                                         activationHotKeyRegistered == true
                                             ? requestedHotkeys.Activation.DisplayText
                                             : string.Empty),
        };

        if (expandHotKeyRegistered == false)
        {
            expandHotkeyFailure = "Windows could not register this shortcut because another app may already be using it.";
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "RegisterHotKey",
                                "The expand shortcut is already in use; Starboard will continue without it.");
        }

        if (activationHotKeyRegistered == false)
        {
            activationHotkeyFailure = "Windows could not register this shortcut because another app may already be using it.";
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "RegisterHotKey",
                                "The summon shortcut is already in use; Starboard will continue without it.");
        }
    }

    private bool TryApplyPanelAppearance(DesktopSettings requestedSettings, DesktopSettings previousSettings,
                                         out string? failureMessage)
    {
        failureMessage = null;
        if (IsAppearanceChanged(requestedSettings, previousSettings) == false)
        {
            return true;
        }

        try
        {
            runtime.SetPanelOpacity(windowHandle, requestedSettings.Opacity);
            options = options with { CollapsedHeightDip = requestedSettings.CollapsedHeightDip };
            Reconcile();

            return true;
        }
        catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
        {
            failureMessage = exception.Message;
            _ = RestorePanelAppearance(previousSettings);

            return false;
        }
    }

    private bool RestorePanelAppearance(DesktopSettings settings)
    {
        try
        {
            runtime.SetPanelOpacity(windowHandle, settings.Opacity);
            options = options with { CollapsedHeightDip = settings.CollapsedHeightDip };
            Reconcile();

            return true;
        }
        catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "RestorePanelAppearance",
                                "The prior panel appearance could not be restored.", exception);
            return false;
        }
    }

    private bool TryReplaceHotkeys(ParsedHotkeys requestedHotkeys, out HotkeySettings actualHotkeys,
                                   out string? failureMessage)
    {
        var previousHotkeys = effectiveSettings.Hotkeys;
        UnregisterCurrentHotkeys();

        expandHotKeyRegistered = runtime.RegisterHotKey(windowHandle, ExpandHotKeyIdentifier, requestedHotkeys.Expand);
        if (expandHotKeyRegistered == true)
        {
            activationHotKeyRegistered = runtime.RegisterHotKey(windowHandle, ActivationHotKeyIdentifier,
                                                                requestedHotkeys.Activation);
        }

        if (expandHotKeyRegistered == true && activationHotKeyRegistered == true)
        {
            actualHotkeys = new HotkeySettings(requestedHotkeys.Expand.DisplayText,
                                               requestedHotkeys.Activation.DisplayText);
            failureMessage = null;

            return true;
        }

        var failedShortcut = expandHotKeyRegistered == false
            ? requestedHotkeys.Expand.DisplayText
            : requestedHotkeys.Activation.DisplayText;
        UnregisterCurrentHotkeys();
        var restoreSucceeded = TryRestoreHotkeys(previousHotkeys, out actualHotkeys);
        failureMessage = restoreSucceeded == true
            ? $"{failedShortcut} could not be registered. Previous shortcuts were restored."
            : $"{failedShortcut} could not be registered, and one or more previous shortcuts could not be restored.";

        return false;
    }

    private GlobalShortcutRegistrationSnapshot CreateShortcutRegistrationSnapshot()
    {
        var status = isAttached == true
            ? GlobalShortcutRegistrationStatus.NotRegistered
            : GlobalShortcutRegistrationStatus.Unknown;
        var expand = new GlobalShortcutRegistrationState(configuredHotkeys.ExpandShortcut,
                                                         expandHotKeyRegistered == true
                                                              ? configuredHotkeys.ExpandShortcut
                                                              : null,
                                                         expandHotKeyRegistered == true
                                                              ? GlobalShortcutRegistrationStatus.Registered
                                                              : status,
                                                         expandHotKeyRegistered == true ? null : expandHotkeyFailure);
        var activation = new GlobalShortcutRegistrationState(configuredHotkeys.ActivationShortcut,
                                                             activationHotKeyRegistered == true
                                                                  ? configuredHotkeys.ActivationShortcut
                                                                  : null,
                                                             activationHotKeyRegistered == true
                                                                  ? GlobalShortcutRegistrationStatus.Registered
                                                                  : status,
                                                             activationHotKeyRegistered == true ? null : activationHotkeyFailure);

        return new GlobalShortcutRegistrationSnapshot(expand, activation);
    }

    private bool TryRestoreHotkeys(HotkeySettings settings, out HotkeySettings actualHotkeys)
    {
        var expandRegistered = false;
        var activationRegistered = false;
        expandRegistered = TryRestoreHotkey(settings.ExpandShortcut, ExpandHotKeyIdentifier);
        activationRegistered = TryRestoreHotkey(settings.ActivationShortcut, ActivationHotKeyIdentifier);

        expandHotKeyRegistered = expandRegistered;
        activationHotKeyRegistered = activationRegistered;
        actualHotkeys = new HotkeySettings(expandRegistered == true ? settings.ExpandShortcut : string.Empty,
                                           activationRegistered == true ? settings.ActivationShortcut : string.Empty);

        return actualHotkeys == settings;
    }

    private bool TryRestoreHotkey(string shortcut, int identifier)
    {
        if (string.IsNullOrWhiteSpace(shortcut) == true)
        {
            return false;
        }

        if (GlobalHotkeyParser.TryParse(shortcut, out var hotkey, out _) == false)
        {
            return false;
        }

        return runtime.RegisterHotKey(windowHandle, identifier, hotkey);
    }

    private void UnregisterCurrentHotkeys()
    {
        if (expandHotKeyRegistered == true)
        {
            runtime.UnregisterHotKey(windowHandle, ExpandHotKeyIdentifier);
            expandHotKeyRegistered = false;
        }

        if (activationHotKeyRegistered == true)
        {
            runtime.UnregisterHotKey(windowHandle, ActivationHotKeyIdentifier);
            activationHotKeyRegistered = false;
        }
    }

    private bool TryRestoreStartup(StartupSettings settings)
    {
        try
        {
            startupRegistration.SetEnabled(settings.StartWithWindows);
            return true;
        }
        catch (Exception exception) when (IsRecoverablePlatformFailure(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "RestoreStartup",
                                "The prior per-user startup state could not be restored.", exception);
            return false;
        }
    }

    private static bool TryValidateSettings(DesktopSettings settings, out ParsedHotkeys hotkeys,
                                            out string? failureMessage)
    {
        hotkeys = default;
        failureMessage = null;
        if (settings.CollapsedHeightDip < 96 || settings.CollapsedHeightDip > 720)
        {
            failureMessage = "Collapsed height must be between 96 and 720 DIP.";
            return false;
        }

        if (settings.Opacity < 0.72 || settings.Opacity > 1)
        {
            failureMessage = "Opacity must be between 0.72 and 1.";
            return false;
        }

        if (settings.PreferredMonitorBehavior != PreferredMonitorBehavior.TaskbarMonitor)
        {
            failureMessage = "The requested monitor behavior is unsupported.";
            return false;
        }

        if (GlobalHotkeyParser.TryParse(settings.Hotkeys.ExpandShortcut, out var expand, out failureMessage) == false)
        {
            return false;
        }

        if (GlobalHotkeyParser.TryParse(settings.Hotkeys.ActivationShortcut, out var activation, out failureMessage) == false)
        {
            return false;
        }

        if (expand == activation)
        {
            failureMessage = "Expand and summon shortcuts must differ.";
            return false;
        }

        hotkeys = new ParsedHotkeys(expand, activation);

        return true;
    }

    private static ParsedHotkeys ToParsedHotkeys(HotkeySettings settings)
    {
        if (GlobalHotkeyParser.TryParse(settings.ExpandShortcut, out var expand, out var expandFailure) == false)
        {
            throw new InvalidOperationException(expandFailure);
        }

        if (GlobalHotkeyParser.TryParse(settings.ActivationShortcut, out var activation, out var activationFailure) == false)
        {
            throw new InvalidOperationException(activationFailure);
        }

        return new ParsedHotkeys(expand, activation);
    }

    private static DesktopSettingsApplyResult CreateValidationFailureResult(DesktopSettings requestedSettings,
                                                                            DesktopSettings previousSettings,
                                                                            string failureMessage)
    {
        return new DesktopSettingsApplyResult(requestedSettings, previousSettings, previousSettings,
                                              DesktopSettingsApplyStatus.FailedWithoutChange,
                                              [
                                                  new DesktopSettingsOperationResult(DesktopSettingsOperation.PanelAppearance,
                                                                                     DesktopSettingsOperationStatus.Unchanged,
                                                                                     null),
                                                  new DesktopSettingsOperationResult(DesktopSettingsOperation.MonitorBehavior,
                                                                                     DesktopSettingsOperationStatus.Unchanged,
                                                                                     null),
                                                  new DesktopSettingsOperationResult(DesktopSettingsOperation.Hotkeys,
                                                                                     DesktopSettingsOperationStatus.Failed,
                                                                                     failureMessage),
                                                  new DesktopSettingsOperationResult(DesktopSettingsOperation.Startup,
                                                                                     DesktopSettingsOperationStatus.Unchanged,
                                                                                     null),
                                              ]);
    }

    private static void AddUnchangedOperations(List<DesktopSettingsOperationResult> operations,
                                               DesktopSettingsOperation operation)
    {
        operations.Add(new DesktopSettingsOperationResult(operation, DesktopSettingsOperationStatus.Unchanged, null));
    }

    private static bool IsAppearanceChanged(DesktopSettings requestedSettings, DesktopSettings previousSettings)
    {
        return requestedSettings.CollapsedHeightDip != previousSettings.CollapsedHeightDip ||
            requestedSettings.Opacity != previousSettings.Opacity;
    }

    private static DesktopSettings CreateDefaultSettings()
    {
        return new DesktopSettings(200, _DefaultOpacity, PreferredMonitorBehavior.TaskbarMonitor,
                                   new HotkeySettings("Ctrl+Alt+E", "Ctrl+Alt+S"), new StartupSettings(false));
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
            UnauthorizedAccessException or
            IOException or
            DllNotFoundException or
            EntryPointNotFoundException;
    }
}

internal readonly record struct ParsedHotkeys(GlobalHotkey Expand, GlobalHotkey Activation);

internal interface IDesktopIntegrationRuntime : IDisposable
{
    uint TaskbarCreatedMessage { get; }

    event EventHandler? EnvironmentChanged;

    event EventHandler? ForegroundChanged;

    event EventHandler? TrayToggleVisibilityRequested;

    event EventHandler? TraySummonRequested;

    event EventHandler? TraySettingsRequested
    {
        add { }
        remove { }
    }

    event EventHandler? TrayShortcutGuideRequested
    {
        add { }
        remove { }
    }

    event EventHandler? TrayExitRequested;

    void Attach(nint windowHandle);

    bool RegisterHotKey(nint windowHandle, int identifier, uint virtualKey);

    bool RegisterHotKey(nint windowHandle, int identifier, GlobalHotkey hotkey)
    {
        return RegisterHotKey(windowHandle, identifier, hotkey.VirtualKey);
    }

    void UnregisterHotKey(nint windowHandle, int identifier);

    DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle);

    FullscreenObservation CaptureFullscreen(nint panelWindowHandle, MonitorSnapshot panelMonitor);

    void PlaceWithoutActivation(nint windowHandle, PixelRect bounds);

    void SetPanelOpacity(nint windowHandle, double opacity)
    {
        _ = windowHandle;
        _ = opacity;
    }

    void ActivateOnExplicitRequest(nint windowHandle);

    void SetTrayPanelVisible(bool isVisible);

    void RecreateTrayIcon(bool isVisible);
}

internal sealed class WindowsDesktopIntegrationRuntime : IDesktopIntegrationRuntime
{
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly Action<double> _applyPanelOpacity;
    private readonly FullscreenService _fullscreenService;
    private readonly TaskbarService _taskbarService;

    private DisplaySettingsObserver? _displaySettingsObserver;
    private ForegroundWindowObserver? _foregroundWindowObserver;
    private TrayIconService? _trayIconService;
    private bool _isDisposed;

    internal WindowsDesktopIntegrationRuntime(IDiagnosticLog diagnosticLog, Action<double> applyPanelOpacity)
    {
        ArgumentNullException.ThrowIfNull(applyPanelOpacity);
        _diagnosticLog = diagnosticLog;
        _applyPanelOpacity = applyPanelOpacity;
        var nativeApi = new DesktopNativeApi();
        _fullscreenService = new FullscreenService(nativeApi);
        _taskbarService = new TaskbarService(nativeApi);
        TaskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        if (TaskbarCreatedMessage == 0)
        {
            _diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "RegisterTaskbarCreatedMessage",
                                 "Explorer restart notifications are unavailable; periodic reconciliation remains active.");
        }
    }

    public uint TaskbarCreatedMessage { get; }

    public event EventHandler? EnvironmentChanged;

    public event EventHandler? ForegroundChanged;

    public event EventHandler? TrayToggleVisibilityRequested;

    public event EventHandler? TraySummonRequested;

    public event EventHandler? TraySettingsRequested;

    public event EventHandler? TrayShortcutGuideRequested;

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
            LogObserverFailure("ObserveDisplaySettings",
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
            LogObserverFailure("ObserveForeground",
                               "Foreground notifications are unavailable; periodic fullscreen reconciliation remains active.",
                               exception);
        }
    }

    public bool RegisterHotKey(nint windowHandle, int identifier, uint virtualKey)
    {
        return RegisterHotKey(windowHandle, identifier,
                              new GlobalHotkey(NativeMethods.ModifierControl | NativeMethods.ModifierAlt, virtualKey, string.Empty));
    }

    public bool RegisterHotKey(nint windowHandle, int identifier, GlobalHotkey hotkey)
    {
        return NativeMethods.RegisterHotKey(windowHandle, identifier, hotkey.Modifiers | NativeMethods.ModifierNoRepeat,
                                            hotkey.VirtualKey);
    }

    public void UnregisterHotKey(nint windowHandle, int identifier)
    {
        _ = NativeMethods.UnregisterHotKey(windowHandle, identifier);
    }

    public DesktopGeometrySnapshot CaptureGeometry(nint panelWindowHandle)
    {
        return _taskbarService.CaptureLatest(panelWindowHandle);
    }

    public FullscreenObservation CaptureFullscreen(nint panelWindowHandle, MonitorSnapshot panelMonitor)
    {
        return _fullscreenService.Capture(panelWindowHandle, panelMonitor);
    }

    public void PlaceWithoutActivation(nint windowHandle, PixelRect bounds)
    {
        WindowPlacementService.PlaceWithoutActivation(windowHandle, bounds);
    }

    public void SetPanelOpacity(nint windowHandle, double opacity)
    {
        _ = windowHandle;
        // WPF owns its layered-window style. Let the host apply its supported
        // surface opacity instead of forcing WS_EX_LAYERED on an opaque HWND.
        _applyPanelOpacity(opacity);
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
            DisposeObserver(_displaySettingsObserver, "DisposeDisplaySettingsObserver");
        }

        if (_foregroundWindowObserver is not null)
        {
            _foregroundWindowObserver.ForegroundChanged -= HandleForegroundChanged;
            DisposeObserver(_foregroundWindowObserver, "DisposeForegroundObserver");
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
            _trayIconService.SettingsRequested += HandleTraySettingsRequested;
            _trayIconService.ShortcutGuideRequested += HandleTrayShortcutGuideRequested;
            _trayIconService.ExitRequested += HandleTrayExitRequested;
            _trayIconService.SetPanelVisible(isVisible);
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            _trayIconService = null;
            LogObserverFailure("CreateTrayIcon",
                               "The notification icon is unavailable; global shortcuts remain active.", exception);
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
        _trayIconService.SettingsRequested -= HandleTraySettingsRequested;
        _trayIconService.ShortcutGuideRequested -= HandleTrayShortcutGuideRequested;
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
            LogObserverFailure(operation, "A Windows observer could not be released cleanly during shutdown.",
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

    private void HandleTraySettingsRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        TraySettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrayShortcutGuideRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        TrayShortcutGuideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrayExitRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        TrayExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LogObserverFailure(string operation, string message, Exception exception)
    {
        _diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", operation, message, exception);
    }
}
