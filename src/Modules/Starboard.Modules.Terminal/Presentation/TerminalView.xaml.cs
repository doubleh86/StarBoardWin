using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Presentation;

internal partial class TerminalView : UserControl, IAsyncDisposable
{
    private const string RendererHostName = "starboard.local";
    private const int MaximumOutputBatchLength = 65_536;
    private const int MaximumPendingOutputLength = 4 * 1024 * 1024;

    private readonly IDiagnosticLog diagnosticLog;
    private readonly TerminalSessionCoordinator sessionCoordinator;
    private readonly TerminalWorkspacePersistence workspacePersistence;
    private readonly TerminalSavedTabService savedTabService;
    private readonly TerminalCollapsedHeightChangeCallback collapsedHeightChangeCallback;
    private readonly Action panelResizeRequested;
    private readonly Action exitRequested;
    private readonly TerminalUrlOpenService urlOpenService;
    private readonly TerminalLaunchProfileCatalog launchProfileCatalog;
    private readonly Lock outputLock = new();
    private readonly Dictionary<TerminalSessionId, PendingSessionOutput> pendingOutput = [];
    private readonly HashSet<TerminalSessionId> outputFlushScheduled = [];
    private readonly HashSet<TerminalSessionId> outputOverflowReported = [];
    private readonly HashSet<TerminalSessionId> rendererSessionIds = [];
    private readonly Dictionary<TerminalSessionId, long> rendererSessionGenerations = [];
    private readonly HashSet<TerminalSavedTabRequestId> savedTabLaunchRequestIds = [];
    private readonly Dictionary<TerminalLaunchProfileId, TerminalLaunchProfile> launchProfiles = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private CancellationTokenSource rendererOperationCancellation = new();
    private WebView2 renderer;

    private TaskCompletionSource rendererReady = CreateCompletionSource();
    private TerminalSettings? settings;
    private TerminalAppearanceState? appearanceState;
    private ShellLaunchSpec? defaultShell;
    private TerminalShellKind? defaultShellKind;
    private PendingSafetyConfirmation? pendingSafetyConfirmation;
    private bool savedTabsInitialized;
    private bool rendererFailed;
    private bool rendererRequiresRecreation;
    private bool webViewRuntimeUnavailable;
    private bool launchProfileDiscoveryStarted;
    private bool isDisposed;
    private Guid rendererInstanceId;
    private long rendererGeneration;
    private long collapsedHeightChangeSequence;
    private int columns = 80;
    private int rows = 24;

    internal TerminalView(IDiagnosticLog diagnosticLog, TerminalSessionCoordinator sessionCoordinator,
                          TerminalWorkspacePersistence workspacePersistence,
                          TerminalSavedTabService savedTabService,
                          TerminalCollapsedHeightChangeCallback collapsedHeightChangeCallback,
                          Action panelResizeRequested, Action exitRequested)
    {
        this.diagnosticLog = diagnosticLog;
        this.sessionCoordinator = sessionCoordinator;
        this.workspacePersistence = workspacePersistence;
        this.savedTabService = savedTabService;
        ArgumentNullException.ThrowIfNull(collapsedHeightChangeCallback);
        this.collapsedHeightChangeCallback = collapsedHeightChangeCallback;
        ArgumentNullException.ThrowIfNull(panelResizeRequested);
        this.panelResizeRequested = panelResizeRequested;
        ArgumentNullException.ThrowIfNull(exitRequested);
        this.exitRequested = exitRequested;
        urlOpenService = new TerminalUrlOpenService(new TerminalExternalUrlLauncher());
        launchProfileCatalog = new TerminalLaunchProfileCatalog(new WslProcessRunner(), diagnosticLog);
        sessionCoordinator.WorkspaceChanged += Session_WorkspaceChanged;
        sessionCoordinator.OutputReceived += Session_OutputReceived;
        sessionCoordinator.SessionExited += Session_Exited;
        sessionCoordinator.NewOutputStateChanged += Session_NewOutputStateChanged;
        workspacePersistence.StatusChanged += WorkspacePersistence_StatusChanged;
        InitializeComponent();
        renderer = CreateRendererControl();
        RendererHost.Children.Add(renderer);
        var canvas = ((SolidColorBrush)FindResource("CanvasBrush")).Color;
        SetRendererBackground(canvas);
    }

    internal async Task StartAsync(TerminalOptions terminalOptions, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(terminalOptions);
        var initialAppearance = CreateAppearanceSnapshot(new TerminalAppearanceSettings(terminalOptions.FontFamily, terminalOptions.FontSize, terminalOptions.Theme));
        defaultShell = ShellResolver.Resolve(terminalOptions.ShellExecutable);
        defaultShellKind = ShellResolver.GetConfiguredKind(terminalOptions.ShellExecutable);
        settings = new TerminalSettings(initialAppearance, terminalOptions.ShellExecutable);
        appearanceState = new TerminalAppearanceState(initialAppearance);
        ApplyTheme(initialAppearance.Theme);
        ShowLoading("로컬 renderer와 shell session을 준비하고 있습니다.");

        try
        {
            await InitializeRendererAsync(cancellationToken);
            await InitializeSavedTabsAsync(cancellationToken);
            await StartWorkspaceAsync(terminalOptions.RestoreWorkspaceOnLaunch, cancellationToken);
            ShowTerminal();
        }
        catch (Exception exception) when (IsRecoverableStartupException(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "Start",
                                "The terminal session could not be started.", exception);
            if (rendererFailed == false && renderer.CoreWebView2 is not null &&
                sessionCoordinator.Snapshot.Tabs.Count > 0)
            {
                ShowTerminal();
            }
            else
            {
                webViewRuntimeUnavailable = exception is WebView2RuntimeNotFoundException;
                rendererRequiresRecreation = renderer.CoreWebView2 is null;
                ShowError(ToUserMessage(exception), webViewRuntimeUnavailable);
            }
        }
    }

    internal TerminalSettingsApplyResult ApplySettings(TerminalSettings requestedSettings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(requestedSettings);
        var previousSettings = settings
            ?? throw new InvalidOperationException("Terminal settings are unavailable before startup.");

        TerminalAppearanceSettings requestedAppearance;
        ShellLaunchSpec requestedShell;
        try
        {
            requestedAppearance = CreateAppearanceSnapshot(requestedSettings.Appearance);
            requestedShell = string.Equals(requestedSettings.DefaultShellExecutable,
                                           previousSettings.DefaultShellExecutable, StringComparison.OrdinalIgnoreCase)
                ? defaultShell ?? ShellResolver.Resolve(requestedSettings.DefaultShellExecutable)
                : ShellResolver.Resolve(requestedSettings.DefaultShellExecutable);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception) == true)
        {
            return new TerminalSettingsApplyResult(requestedSettings, previousSettings, previousSettings,
                                                   TerminalSettingsApplyStatus.FailedWithoutChange, exception.Message);
        }

        var effectiveSettings = new TerminalSettings(requestedAppearance, requestedSettings.DefaultShellExecutable);
        var previousShell = defaultShell
            ?? throw new InvalidOperationException("The terminal shell has not been configured.");
        var hasWorkspace = sessionCoordinator.Snapshot.Tabs.Count > 0;
        var defaultShellChanged = hasWorkspace == true &&
                                  requestedShell != previousShell;
        var defaultShellApplied = false;
        var appearanceAttempted = false;

        try
        {
            if (defaultShellChanged == true)
            {
                sessionCoordinator.UpdateDefaultShell(requestedShell,
                                                      ShellResolver.GetConfiguredKind(requestedSettings.DefaultShellExecutable));
                defaultShellApplied = true;
            }

            appearanceAttempted = true;
            ApplyTheme(requestedAppearance.Theme);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception) == true)
        {
            var restoreIncomplete = RestorePreviousSettings(previousSettings, previousShell, defaultShellApplied,
                                                            appearanceAttempted);
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "ApplySettings",
                                "Terminal settings could not be applied and recovery was attempted.", exception);

            return new TerminalSettingsApplyResult(requestedSettings, previousSettings, previousSettings,
                                                   GetFailureStatus(defaultShellApplied, appearanceAttempted, restoreIncomplete),
                                                   exception.Message);
        }

        defaultShell = requestedShell;
        defaultShellKind = ShellResolver.GetConfiguredKind(requestedSettings.DefaultShellExecutable);
        settings = effectiveSettings;
        var rendererAppearance = appearanceState?.Update(requestedAppearance);
        if (rendererAppearance is not null)
        {
            SendAppearanceMessage("apply-appearance", rendererAppearance);
        }

        return new TerminalSettingsApplyResult(requestedSettings, previousSettings, effectiveSettings,
                                               TerminalSettingsApplyStatus.Applied, null);
    }

    private bool RestorePreviousSettings(TerminalSettings previousSettings, ShellLaunchSpec previousShell,
                                         bool restoreDefaultShell, bool restoreAppearance)
    {
        var restoreIncomplete = false;
        if (restoreDefaultShell == true)
        {
            try
            {
                sessionCoordinator.UpdateDefaultShell(previousShell,
                                                      ShellResolver.GetConfiguredKind(previousSettings.DefaultShellExecutable));
            }
            catch (Exception exception) when (IsRecoverableSettingsException(exception) == true)
            {
                restoreIncomplete = true;
                diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "RestoreDefaultShell",
                                    "The previous default shell could not be restored.", exception);
            }
        }

        if (restoreAppearance == true)
        {
            try
            {
                ApplyTheme(previousSettings.Appearance.Theme);
            }
            catch (Exception exception) when (IsRecoverableSettingsException(exception) == true)
            {
                restoreIncomplete = true;
                diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "RestoreAppearance",
                                    "The previous terminal appearance could not be restored.", exception);
            }
        }

        return restoreIncomplete;
    }

    public ValueTask DisposeAsync()
    {
        if (isDisposed == true)
        {
            return ValueTask.CompletedTask;
        }

        CancelPendingSafetyConfirmation(sendRendererCancellation: false);
        isDisposed = true;
        collapsedHeightChangeSequence++;
        rendererOperationCancellation.Cancel();
        lifetimeCancellation.Cancel();
        sessionCoordinator.WorkspaceChanged -= Session_WorkspaceChanged;
        sessionCoordinator.OutputReceived -= Session_OutputReceived;
        sessionCoordinator.SessionExited -= Session_Exited;
        sessionCoordinator.NewOutputStateChanged -= Session_NewOutputStateChanged;
        workspacePersistence.StatusChanged -= WorkspacePersistence_StatusChanged;
        DetachRendererEvents(renderer.CoreWebView2);
        renderer.Dispose();
        rendererSessionGenerations.Clear();
        rendererOperationCancellation.Dispose();
        lifetimeCancellation.Dispose();

        return ValueTask.CompletedTask;
    }

    internal void NotifyPanelVisibilityChanged(bool isVisible)
    {
        if (isVisible == false && isDisposed == false)
        {
            CancelPendingSafetyConfirmation(sendRendererCancellation: true);
        }
    }

    private async Task InitializeRendererAsync(CancellationToken cancellationToken)
    {
        if (renderer.CoreWebView2 is not null)
        {
            return;
        }

        var rendererDirectory = Path.Combine(AppContext.BaseDirectory, "Renderer");
        var indexPath = Path.Combine(rendererDirectory, "index.html");
        if (File.Exists(indexPath) == false)
        {
            throw new FileNotFoundException("번들된 terminal renderer를 찾을 수 없습니다.", indexPath);
        }

        var userDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                             "Starboard", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataDirectory, null);

        await renderer.EnsureCoreWebView2Async(environment);
        var core = renderer.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 initialization returned no core instance.");
        ConfigureRenderer(core);
        appearanceState?.MarkRendererUnavailable();
        core.Navigate($"https://{RendererHostName}/index.html");

        await rendererReady.Task.WaitAsync(cancellationToken);
        rendererFailed = false;
        rendererRequiresRecreation = false;
        webViewRuntimeUnavailable = false;
        SendInitializeMessage();
        PublishLaunchProfiles(launchProfileCatalog.QueryBuiltInProfiles());
        StartLaunchProfileDiscovery();
        rendererSessionIds.Clear();
        SyncWorkspace(sessionCoordinator.Snapshot);
        SchedulePendingOutputFlushes();
    }

    private void ConfigureRenderer(CoreWebView2 core)
    {
        var rendererDirectory = Path.Combine(AppContext.BaseDirectory, "Renderer");
        core.SetVirtualHostNameToFolderMapping(RendererHostName, rendererDirectory,
                                               CoreWebView2HostResourceAccessKind.DenyCors);

        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.WebMessageReceived += Core_WebMessageReceived;
        core.NavigationStarting += Core_NavigationStarting;
        core.NewWindowRequested += static (_, eventArgs) => eventArgs.Handled = true;
        core.DownloadStarting += static (_, eventArgs) => eventArgs.Cancel = true;
        core.PermissionRequested += static (_, eventArgs) =>
        {
            eventArgs.State = CoreWebView2PermissionState.Deny;
            eventArgs.Handled = true;
        };
        core.ProcessFailed += Core_ProcessFailed;
    }

    private void DetachRendererEvents(CoreWebView2? core)
    {
        if (core is null)
        {
            return;
        }

        core.WebMessageReceived -= Core_WebMessageReceived;
        core.NavigationStarting -= Core_NavigationStarting;
        core.ProcessFailed -= Core_ProcessFailed;
    }

    private static WebView2 CreateRendererControl()
    {
        var control = new WebView2
        {
            AllowExternalDrop = false,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetName(control, "Starboard terminal tabs and sessions");

        return control;
    }

    private void RecreateRendererControl()
    {
        DetachRendererEvents(renderer.CoreWebView2);
        RendererHost.Children.Remove(renderer);
        renderer.Dispose();
        renderer = CreateRendererControl();
        RendererHost.Children.Add(renderer);
        var canvas = ((SolidColorBrush)FindResource("CanvasBrush")).Color;
        SetRendererBackground(canvas);
        rendererRequiresRecreation = false;
    }

    internal Task<TerminalWorkspacePersistenceResult> SetWorkspacePersistenceEnabledAsync(
        bool enabled, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return workspacePersistence.SetEnabledAsync(enabled, cancellationToken);
    }

    internal ValueTask<TerminalCollapsedHeightChangeResult> RequestCollapsedHeightChangeAsync(
        TerminalCollapsedHeightChangeRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);

        var task = ApplyCollapsedHeightChangeAsync(request, rendererGeneration, rendererInstanceId,
                                                   rendererScoped: false, cancellationToken);

        return new ValueTask<TerminalCollapsedHeightChangeResult>(task);
    }

    private async Task StartWorkspaceAsync(bool restoreEnabled, CancellationToken cancellationToken)
    {
        var shell = defaultShell
            ?? throw new InvalidOperationException("The terminal shell has not been configured.");
        var loadResult = await workspacePersistence.InitializeAsync(restoreEnabled, cancellationToken);
        var configuration = loadResult.Status is TerminalWorkspaceLoadStatus.Loaded or
                                                 TerminalWorkspaceLoadStatus.RecoveredFromBackup
            ? loadResult.Configuration
            : null;
        await sessionCoordinator.StartAsync(shell, defaultShellKind, configuration, ShellResolver.Resolve,
                                            columns, rows, cancellationToken);
        workspacePersistence.CompleteRestore();
    }

    private async Task InitializeSavedTabsAsync(CancellationToken cancellationToken)
    {
        if (savedTabsInitialized == true)
        {
            return;
        }

        _ = await savedTabService.InitializeAsync(cancellationToken);
        savedTabsInitialized = true;
        SyncSavedTabs();
    }

    private void StartLaunchProfileDiscovery()
    {
        if (launchProfileDiscoveryStarted == true || isDisposed == true)
        {
            return;
        }

        launchProfileDiscoveryStarted = true;
        _ = RefreshLaunchProfilesAsync(rendererGeneration, rendererOperationCancellation.Token);
    }

    private async Task RefreshLaunchProfilesAsync(long requestRendererGeneration,
                                                  CancellationToken cancellationToken)
    {
        try
        {
            var result = await launchProfileCatalog.QueryAsync(cancellationToken);
            PublishLaunchProfiles(result, requestRendererGeneration);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RefreshLaunchProfiles",
                                "Terminal launch profile discovery failed without affecting active sessions.", exception);
        }
        finally
        {
            launchProfileDiscoveryStarted = false;
            if (isDisposed == false && rendererFailed == false && renderer.CoreWebView2 is not null &&
                rendererGeneration != requestRendererGeneration)
            {
                StartLaunchProfileDiscovery();
            }
        }
    }

    private void PublishLaunchProfiles(TerminalLaunchProfileQueryResult result,
                                       long? requestRendererGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (isDisposed == true || rendererFailed == true || renderer.CoreWebView2 is null ||
            (requestRendererGeneration.HasValue == true && rendererGeneration != requestRendererGeneration.Value))
        {
            return;
        }

        launchProfiles.Clear();
        foreach (var profile in result.Profiles)
        {
            launchProfiles[profile.ProfileId] = profile;
        }

        PostRendererMessage(RendererProtocol.SerializeLaunchProfileQueryResult(result));
    }

    private async Task LaunchProfileAsync(TerminalNewTabRequest request, long requestRendererGeneration,
                                          Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false ||
            launchProfiles.TryGetValue(request.ProfileId, out var profile) == false)
        {
            return;
        }

        try
        {
            CancelPendingSafetyConfirmation(sendRendererCancellation: true);
            var result = await sessionCoordinator.LaunchProfileAsync(request, profile, launchProfileCatalog.Resolve,
                                                                     cancellationToken);
            if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                         cancellationToken) == false)
            {
                await RemoveStaleLaunchedTabAsync(result.Tab?.SessionId);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException or
            Win32Exception or UnauthorizedAccessException)
        {
            if (isDisposed == false && cancellationToken.IsCancellationRequested == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "LaunchProfile",
                                    "A terminal launch profile could not start a tab.", exception);
            }
        }
    }

    private async Task DuplicateTabAsync(TerminalTabDuplicateRequest request, long requestRendererGeneration,
                                         Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false)
        {
            return;
        }

        try
        {
            CancelPendingSafetyConfirmation(sendRendererCancellation: true);
            var result = await sessionCoordinator.DuplicateAsync(request, launchProfileCatalog.Resolve,
                                                                 cancellationToken);
            if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                         cancellationToken) == false)
            {
                await RemoveStaleLaunchedTabAsync(result.Tab?.SessionId);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException or
            Win32Exception or UnauthorizedAccessException)
        {
            if (isDisposed == false && cancellationToken.IsCancellationRequested == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "DuplicateTab",
                                    "A terminal tab configuration could not be duplicated.", exception);
            }
        }
    }

    private async Task<TerminalCollapsedHeightChangeResult> ApplyCollapsedHeightChangeAsync(
        TerminalCollapsedHeightChangeRequest request, long requestRendererGeneration,
        Guid requestRendererInstanceId, bool rendererScoped, CancellationToken cancellationToken)
    {
        var requestSequence = ++collapsedHeightChangeSequence;
        if (rendererScoped == true &&
            CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false)
        {
            return CreateUnavailableCollapsedHeightResult(request);
        }

        TerminalCollapsedHeightChangeResult result;
        try
        {
            result = await collapsedHeightChangeCallback(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
        {
            return CreateUnavailableCollapsedHeightResult(request);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException or
            ObjectDisposedException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "ChangeCollapsedHeight",
                                "The collapsed terminal height request failed without stopping active sessions.",
                                exception);
            result = new TerminalCollapsedHeightChangeResult(
                request.RequestId, TerminalCollapsedHeightChangeStatus.Reverted, request.LastSavedHeightDip,
                "패널 높이를 변경하지 못해 마지막 저장값을 유지했습니다.");
        }

        if (requestSequence == collapsedHeightChangeSequence &&
            CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == true)
        {
            PostRendererMessage(RendererProtocol.SerializeCollapsedHeightChangeResult(result));
        }

        return result;
    }

    private bool CanApplyRendererCallback(long requestRendererGeneration, Guid requestRendererInstanceId,
                                          CancellationToken cancellationToken)
    {
        return isDisposed == false && cancellationToken.IsCancellationRequested == false &&
               rendererFailed == false && renderer.CoreWebView2 is not null &&
               rendererGeneration == requestRendererGeneration && requestRendererInstanceId != Guid.Empty &&
               requestRendererInstanceId == rendererInstanceId;
    }

    private static TerminalCollapsedHeightChangeResult CreateUnavailableCollapsedHeightResult(
        TerminalCollapsedHeightChangeRequest request)
    {
        return new TerminalCollapsedHeightChangeResult(
            request.RequestId, TerminalCollapsedHeightChangeStatus.Failed, request.LastSavedHeightDip,
            "패널 높이 변경 요청이 만료되었습니다.");
    }

    private void WorkspacePersistence_StatusChanged(TerminalWorkspaceSaveStatus status)
    {
        if (isDisposed == true)
        {
            return;
        }

        if (Dispatcher.CheckAccess() == false)
        {
            _ = Dispatcher.BeginInvoke(() => WorkspacePersistence_StatusChanged(status));
            return;
        }

        SendGlobalMessage("workspace-save-status",
                          new { state = status.State.ToString().ToLowerInvariant(), message = status.Message });
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (isDisposed == true || ReferenceEquals(sender, renderer.CoreWebView2) == false)
        {
            return;
        }

        var json = eventArgs.WebMessageAsJson;
        if (RendererProtocol.TryParse(json, out var message) == false || message is null)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RendererMessage",
                                "A malformed or unsupported renderer message was ignored.");
            return;
        }

        switch (message.Type)
        {
            case RendererMessageType.Ready:
                rendererInstanceId = message.RendererInstanceId
                    ?? throw new InvalidOperationException("A validated renderer-ready message has no instance identifier.");
                rendererGeneration++;
                renderer.AllowExternalDrop = true;
                rendererReady.TrySetResult();
                break;
            case RendererMessageType.RetryLaunchProfiles:
                StartLaunchProfileDiscovery();
                break;
            case RendererMessageType.NewTab:
                if (message.NewTabRequest is { } newTabRequest)
                {
                    _ = LaunchProfileAsync(newTabRequest, rendererGeneration, rendererInstanceId,
                                           rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.DuplicateTab:
                if (message.TabDuplicateRequest is { } tabDuplicateRequest)
                {
                    _ = DuplicateTabAsync(tabDuplicateRequest, rendererGeneration, rendererInstanceId,
                                          rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.BeginPanelResize:
                panelResizeRequested();
                break;
            case RendererMessageType.ChangeCollapsedHeight:
                if (message.CollapsedHeightChangeRequest is { } collapsedHeightChangeRequest)
                {
                    _ = ApplyCollapsedHeightChangeAsync(collapsedHeightChangeRequest, rendererGeneration,
                                                        rendererInstanceId, rendererScoped: true,
                                                        rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.SelectSession:
                SelectSession(message.SessionId);
                break;
            case RendererMessageType.SelectNext:
                SelectAdjacentSession(selectPrevious: false);
                break;
            case RendererMessageType.SelectPrevious:
                SelectAdjacentSession(selectPrevious: true);
                break;
            case RendererMessageType.Input:
                if (message.SessionId is { } inputSessionId && pendingSafetyConfirmation is null)
                {
                    _ = WriteInputAsync(inputSessionId, message.Data ?? string.Empty);
                }
                break;
            case RendererMessageType.Resize:
                columns = message.Columns;
                rows = message.Rows;
                if (message.SessionId is { } resizeSessionId)
                {
                    ResizeSession(resizeSessionId, message.Columns, message.Rows);
                }
                break;
            case RendererMessageType.Copy:
                if (message.SessionId is { } copySessionId && ContainsSession(copySessionId) == true)
                {
                    CopyToClipboard(message.Data ?? string.Empty);
                }
                break;
            case RendererMessageType.PasteRequest:
                if (message.SessionId is { } pasteSessionId && ContainsSession(pasteSessionId) == true)
                {
                    RequestPasteFromClipboard(pasteSessionId);
                }
                break;
            case RendererMessageType.DropPaths:
                HandlePathDrop(message, eventArgs);
                break;
            case RendererMessageType.OpenUrlRequest:
                HandleUrlOpenRequest(message);
                break;
            case RendererMessageType.CloseSession:
                if (message.SessionId is { } closeSessionId)
                {
                    _ = CloseSessionAsync(closeSessionId);
                }
                break;
            case RendererMessageType.RestartSession:
                if (message.SessionId is { } restartSessionId)
                {
                    _ = RestartSessionAsync(restartSessionId);
                }
                break;
            case RendererMessageType.RenameSession:
                if (message.SessionId is { } renameSessionId)
                {
                    RenameSession(renameSessionId, message.Data);
                }
                break;
            case RendererMessageType.MoveSession:
                if (message.SessionId is { } moveSessionId)
                {
                    MoveSession(moveSessionId, message.Data);
                }
                break;
            case RendererMessageType.SetStartingDirectory:
                if (message.SessionId is { } directorySessionId)
                {
                    if (string.IsNullOrEmpty(message.Data) == true)
                    {
                        SelectStartingDirectory(directorySessionId);
                    }
                    else
                    {
                        UpdateStartingDirectory(directorySessionId, message.Data);
                    }
                }
                break;
            case RendererMessageType.SessionError:
                if (message.SessionId is { } failedSessionId && ContainsSession(failedSessionId) == true)
                {
                    diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RendererSession",
                                        $"Renderer state failed for terminal session {failedSessionId}.");
                }
                break;
            case RendererMessageType.RendererError:
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RendererRuntime",
                                    "The terminal renderer reported a local runtime error.");
                MarkRendererFailed("Terminal renderer에서 복구할 수 없는 오류가 발생했습니다. " +
                                   "shell session은 유지되며 renderer를 다시 연결할 수 있습니다.");
                break;
            case RendererMessageType.ConfirmationResponse:
                if (message.ConfirmationResponse is { } confirmationResponse)
                {
                    _ = ApplyConfirmationResponseAsync(confirmationResponse);
                }
                break;
            case RendererMessageType.CreateSavedTab:
                if (message.SavedTabCreateRequest is { } createSavedTabRequest)
                {
                    _ = CreateSavedTabAsync(createSavedTabRequest, rendererGeneration, rendererInstanceId,
                                            rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.UpdateSavedTab:
                if (message.SavedTabUpdateRequest is { } updateSavedTabRequest)
                {
                    _ = UpdateSavedTabAsync(updateSavedTabRequest, rendererGeneration, rendererInstanceId,
                                            rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.DeleteSavedTab:
                if (message.SavedTabDeleteRequest is { } deleteSavedTabRequest)
                {
                    _ = DeleteSavedTabAsync(deleteSavedTabRequest, rendererGeneration, rendererInstanceId,
                                            rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.LaunchSavedTab:
                if (message.SavedTabLaunchRequest is { } launchSavedTabRequest)
                {
                    StartSavedTabLaunch(launchSavedTabRequest, rendererGeneration, rendererInstanceId,
                                        rendererOperationCancellation.Token);
                }
                break;
            case RendererMessageType.CancelSavedTabLaunch:
                if (message.SavedTabLaunchCancellation is { } savedTabLaunchCancellation)
                {
                    _ = savedTabService.CancelLaunch(savedTabLaunchCancellation);
                }
                break;
            case RendererMessageType.SavedTabDirectoryRequest:
                if (message.SessionId is { } savedTabSessionId && message.RendererInstanceId == rendererInstanceId &&
                    message.SessionGeneration > 0 && message.Data is { } directoryRequestId)
                {
                    _ = ResolveSavedTabDirectoryAsync(savedTabSessionId, message.SessionGeneration,
                                                      directoryRequestId, rendererGeneration, rendererInstanceId,
                                                      rendererOperationCancellation.Token);
                }
                break;
            default:
                throw new InvalidOperationException("Unexpected renderer message type.");
        }
    }

    private async Task ResolveSavedTabDirectoryAsync(TerminalSessionId sessionId, long sessionGeneration,
                                                     string requestId, long requestRendererGeneration,
                                                     Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (rendererSessionGenerations.TryGetValue(sessionId, out var currentGeneration) == false ||
            currentGeneration != sessionGeneration)
        {
            return;
        }

        var session = new TerminalSessionReference(sessionId.Value, sessionGeneration);
        string? directory;
        try
        {
            directory = await Task.Run(() => sessionCoordinator.GetCurrentDirectory(session),
                                       cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false ||
            rendererSessionGenerations.TryGetValue(sessionId, out currentGeneration) == false ||
            currentGeneration != sessionGeneration || sessionCoordinator.IsCurrentSession(session) == false)
        {
            return;
        }

        SendSessionMessage("saved-tab-directory-result", sessionId,
                           new { requestId, sessionGeneration, currentDirectory = directory });
    }

    private async Task CreateSavedTabAsync(TerminalSavedTabCreateRequest request, long requestRendererGeneration,
                                           Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false)
        {
            return;
        }

        try
        {
            var result = await savedTabService.CreateAsync(request, cancellationToken);
            CompleteSavedTabOperation(result, requestRendererGeneration, requestRendererInstanceId,
                                      cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            HandleSavedTabCallbackException("CreateSavedTab", exception, requestRendererGeneration,
                                            requestRendererInstanceId, cancellationToken);
        }
    }

    private async Task UpdateSavedTabAsync(TerminalSavedTabUpdateRequest request, long requestRendererGeneration,
                                           Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false)
        {
            return;
        }

        try
        {
            var result = await savedTabService.UpdateAsync(request, cancellationToken);
            CompleteSavedTabOperation(result, requestRendererGeneration, requestRendererInstanceId,
                                      cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            HandleSavedTabCallbackException("UpdateSavedTab", exception, requestRendererGeneration,
                                            requestRendererInstanceId, cancellationToken);
        }
    }

    private async Task DeleteSavedTabAsync(TerminalSavedTabDeleteRequest request, long requestRendererGeneration,
                                           Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false)
        {
            return;
        }

        try
        {
            var result = await savedTabService.DeleteAsync(request, cancellationToken);
            CompleteSavedTabOperation(result, requestRendererGeneration, requestRendererInstanceId,
                                      cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            HandleSavedTabCallbackException("DeleteSavedTab", exception, requestRendererGeneration,
                                            requestRendererInstanceId, cancellationToken);
        }
    }

    private void CompleteSavedTabOperation(TerminalSavedTabOperationResult result, long requestRendererGeneration,
                                           Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == true)
        {
            PostRendererMessage(RendererProtocol.SerializeSavedTabOperationResult(
                result, sessionCoordinator.Snapshot.Tabs.Count));
        }
    }

    private void StartSavedTabLaunch(TerminalSavedTabLaunchRequest request, long requestRendererGeneration,
                                     Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == false)
        {
            return;
        }

        if (savedTabLaunchRequestIds.Add(request.RequestId) == false)
        {
            var duplicate = new TerminalSavedTabLaunchResult(
                request.RequestId, request.SavedTabId, TerminalSavedTabLaunchStatus.DuplicateRequest, null, null);
            CompleteSavedTabLaunch(duplicate, requestRendererGeneration, requestRendererInstanceId,
                                   cancellationToken);
            return;
        }

        _ = LaunchSavedTabAsync(request, requestRendererGeneration, requestRendererInstanceId, cancellationToken);
    }

    private async Task LaunchSavedTabAsync(TerminalSavedTabLaunchRequest request, long requestRendererGeneration,
                                           Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await savedTabService.LaunchAsync(request, cancellationToken);
            if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                         cancellationToken) == false)
            {
                await RemoveStaleLaunchedSessionAsync(result.Session);
                return;
            }

            CompleteSavedTabLaunch(result, requestRendererGeneration, requestRendererInstanceId,
                                   cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            HandleSavedTabCallbackException("LaunchSavedTab", exception, requestRendererGeneration,
                                            requestRendererInstanceId, cancellationToken);
        }
    }

    private void CompleteSavedTabLaunch(TerminalSavedTabLaunchResult result, long requestRendererGeneration,
                                        Guid requestRendererInstanceId, CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == true)
        {
            PostRendererMessage(RendererProtocol.SerializeSavedTabLaunchResult(
                result, sessionCoordinator.Snapshot.Tabs.Count));
        }
    }

    private async Task RemoveStaleLaunchedTabAsync(TerminalSessionId? sessionId)
    {
        if (sessionId is null)
        {
            return;
        }

        foreach (var state in sessionCoordinator.NewOutputState.States)
        {
            if (state.Session.SessionId == sessionId.Value.Value)
            {
                await RemoveStaleLaunchedSessionAsync(state.Session);
                return;
            }
        }
    }

    private async Task RemoveStaleLaunchedSessionAsync(TerminalSessionReference? session)
    {
        if (session is not { } currentSession || sessionCoordinator.IsCurrentSession(currentSession) == false ||
            lifetimeCancellation.IsCancellationRequested == true)
        {
            return;
        }

        _ = await sessionCoordinator.CloseAsync(new TerminalSessionId(currentSession.SessionId),
                                                lifetimeCancellation.Token);
    }

    private void SyncSavedTabs()
    {
        if (savedTabsInitialized == false || isDisposed == true || rendererFailed == true ||
            renderer.CoreWebView2 is null)
        {
            return;
        }

        PostRendererMessage(RendererProtocol.SerializeSavedTabsSnapshot(
            savedTabService.Snapshot, sessionCoordinator.Snapshot.Tabs.Count));
    }

    private void HandleSavedTabCallbackException(string operation, Exception exception,
                                                 long requestRendererGeneration, Guid requestRendererInstanceId,
                                                 CancellationToken cancellationToken)
    {
        if (CanApplyRendererCallback(requestRendererGeneration, requestRendererInstanceId,
                                     cancellationToken) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation,
                                "A saved terminal tab renderer request could not be completed.", exception);
            SyncSavedTabs();
        }
    }

    private bool ContainsSession(TerminalSessionId sessionId)
    {
        return sessionCoordinator.Snapshot.Tabs.Any(tab => tab.SessionId == sessionId);
    }

    private void RenameSession(TerminalSessionId sessionId, string? name)
    {
        if (sessionCoordinator.Rename(sessionId, name) == false)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RenameSession",
                                "A terminal tab rename request was rejected.");
        }
    }

    private void MoveSession(TerminalSessionId sessionId, string? direction)
    {
        var moved = direction switch
        {
            "left" => sessionCoordinator.MoveLeft(sessionId),
            "right" => sessionCoordinator.MoveRight(sessionId),
            _ => false,
        };

        if (moved == false && (direction == "left" || direction == "right"))
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "MoveSession",
                                "A terminal tab move request was rejected.");
        }
    }

    private void SelectStartingDirectory(TerminalSessionId sessionId)
    {
        var tab = sessionCoordinator.Snapshot.Tabs.FirstOrDefault(candidate => candidate.SessionId == sessionId);
        if (tab is null)
        {
            return;
        }

        try
        {
            var initialDirectory = Directory.Exists(tab.StartingDirectory) == true
                ? tab.StartingDirectory
                : defaultShell?.WorkingDirectory;
            var dialog = new OpenFolderDialog
            {
                InitialDirectory = initialDirectory ?? string.Empty,
                Multiselect = false,
                Title = $"{tab.Name} 탭 시작 폴더 선택",
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                UpdateStartingDirectory(sessionId, dialog.FolderName);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "SelectStartingDirectory",
                                "The local starting-directory picker could not be opened.", exception);
            SendSessionMessage("session-error", sessionId,
                               new { message = "로컬 폴더 선택기를 열지 못했습니다. 경로를 직접 입력해 주세요." });
        }
    }

    private void UpdateStartingDirectory(TerminalSessionId sessionId, string? startingDirectory)
    {
        try
        {
            var result = sessionCoordinator.UpdateStartingDirectory(sessionId, startingDirectory);
            if (result.Succeeded == false)
            {
                SendSessionMessage("session-error", sessionId,
                                   new { message = result.ErrorMessage ?? "시작 폴더를 변경하지 못했습니다." });
            }
        }
        catch (InvalidOperationException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "UpdateStartingDirectory",
                                "A terminal starting-directory request could not be applied.", exception);
        }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, renderer.CoreWebView2) == false)
        {
            eventArgs.Cancel = true;
            return;
        }

        InvalidateRendererOperations();
        CancelPendingSafetyConfirmation(sendRendererCancellation: false);
        rendererInstanceId = Guid.Empty;
        renderer.AllowExternalDrop = false;
        var allowedPrefix = $"https://{RendererHostName}/";
        if (eventArgs.Uri.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase) == false)
        {
            eventArgs.Cancel = true;
        }
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        if (isDisposed == true || ReferenceEquals(sender, renderer.CoreWebView2) == false)
        {
            return;
        }

        diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "RendererProcess",
                            $"The renderer process failed ({eventArgs.ProcessFailedKind}).");
        MarkRendererFailed("Terminal renderer가 중단됐습니다. shell session은 유지되며 " +
                           "renderer를 다시 연결할 수 있습니다.");
    }

    private void MarkRendererFailed(string message)
    {
        if (isDisposed == true || rendererFailed == true)
        {
            return;
        }

        InvalidateRendererOperations();
        rendererReady.TrySetException(new InvalidOperationException("The terminal renderer stopped before it was ready."));
        CancelPendingSafetyConfirmation(sendRendererCancellation: false);
        rendererFailed = true;
        rendererRequiresRecreation = true;
        rendererInstanceId = Guid.Empty;
        renderer.AllowExternalDrop = false;
        rendererSessionGenerations.Clear();
        appearanceState?.MarkRendererUnavailable();
        ShowError(message, showExit: false);
    }

    private void InvalidateRendererOperations()
    {
        if (isDisposed == true)
        {
            return;
        }

        collapsedHeightChangeSequence++;
        rendererGeneration++;
        rendererOperationCancellation.Cancel();
        rendererOperationCancellation.Dispose();
        rendererOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        if (settings is null || isDisposed == true)
        {
            return;
        }

        var reconnectRendererOnly = rendererFailed &&
                                    sessionCoordinator.Snapshot.Tabs.Count > 0;
        RetryButton.IsEnabled = false;
        ExitButton.IsEnabled = false;
        ShowLoading(reconnectRendererOnly
                        ? "terminal renderer를 다시 연결하고 있습니다."
                        : "shell session을 다시 시작하고 있습니다.");

        try
        {
            if (rendererRequiresRecreation == true)
            {
                RecreateRendererControl();
            }

            if (renderer.CoreWebView2 is null)
            {
                rendererReady = CreateCompletionSource();
                await InitializeRendererAsync(lifetimeCancellation.Token);
            }
            else if (rendererFailed == true)
            {
                CancelPendingSafetyConfirmation(sendRendererCancellation: false);
                rendererReady = CreateCompletionSource();
                rendererSessionIds.Clear();
                rendererSessionGenerations.Clear();
                appearanceState?.MarkRendererUnavailable();
                renderer.CoreWebView2.Navigate($"https://{RendererHostName}/index.html");
                await rendererReady.Task.WaitAsync(lifetimeCancellation.Token);
                rendererFailed = false;
                webViewRuntimeUnavailable = false;
                SendInitializeMessage();
                SyncWorkspace(sessionCoordinator.Snapshot);
                SchedulePendingOutputFlushes();
            }

            if (reconnectRendererOnly == true)
            {
                ShowTerminal();
                return;
            }

            var activeSessionId = sessionCoordinator.Snapshot.ActiveSessionId;
            if (activeSessionId is null)
            {
                var shell = defaultShell
                    ?? throw new InvalidOperationException("The terminal shell has not been configured.");
                await sessionCoordinator.StartAsync(shell, defaultShellKind, null, ShellResolver.Resolve,
                                                    columns, rows, lifetimeCancellation.Token);
            }
            else
            {
                CancelPendingSafetyConfirmation(sendRendererCancellation: true);
                RemovePendingOutput(activeSessionId.Value);
                SendSessionMessage("reset", activeSessionId.Value, new { });
                await sessionCoordinator.RestartAsync(activeSessionId.Value, lifetimeCancellation.Token);
            }

            ShowTerminal();
        }
        catch (Exception exception) when (IsRecoverableStartupException(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Error, "Terminal", "Restart",
                                "The terminal session could not be restarted.", exception);
            webViewRuntimeUnavailable = exception is WebView2RuntimeNotFoundException;
            rendererRequiresRecreation = renderer.CoreWebView2 is null;
            ShowError(ToUserMessage(exception), webViewRuntimeUnavailable);
        }
        finally
        {
            RetryButton.IsEnabled = true;
            ExitButton.IsEnabled = true;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (isDisposed == false)
        {
            exitRequested();
        }
    }

    private void SelectSession(TerminalSessionId? sessionId)
    {
        if (sessionId is null || isDisposed == true)
        {
            return;
        }

        try
        {
            CancelPendingSafetyConfirmation(sendRendererCancellation: true);
            _ = sessionCoordinator.Select(sessionId.Value);
        }
        catch (InvalidOperationException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "SelectSession",
                                "A terminal tab could not be selected.", exception);
        }
    }

    private void SelectAdjacentSession(bool selectPrevious)
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            CancelPendingSafetyConfirmation(sendRendererCancellation: true);
            if (selectPrevious == true)
            {
                _ = sessionCoordinator.SelectPrevious();
                return;
            }

            _ = sessionCoordinator.SelectNext();
        }
        catch (InvalidOperationException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "SelectSession",
                                "A terminal tab could not be selected.", exception);
        }
    }

    private async Task CloseSessionAsync(TerminalSessionId sessionId)
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            var confirmation = sessionCoordinator.RequestCloseConfirmation(sessionId);
            if (confirmation is not null)
            {
                PresentCloseConfirmation(confirmation);
                return;
            }

            _ = await sessionCoordinator.CloseAsync(sessionId, lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "CloseSession",
                                    $"Terminal session {sessionId} could not be closed.", exception);
                SendSessionMessage("session-error", sessionId, new { message = "탭을 닫지 못했습니다." });
            }
        }
    }

    private void PresentCloseConfirmation(TerminalCloseConfirmationRequest request)
    {
        var sessionId = new TerminalSessionId(request.Token.Session.SessionId);
        pendingSafetyConfirmation = new PendingSafetyConfirmation(request.Token, ConfirmationKind.Close);
        SendSessionMessage("confirmation-request", sessionId,
                           new
                           {
                               requestId = request.Token.RequestId.ToString(),
                               sessionGeneration = request.Token.Session.Generation,
                               kind = "close",
                               request.SessionName,
                           });
    }

    private void PresentPasteConfirmation(TerminalPasteConfirmationRequest request)
    {
        var sessionId = new TerminalSessionId(request.Token.Session.SessionId);
        pendingSafetyConfirmation = new PendingSafetyConfirmation(request.Token, ConfirmationKind.Paste);
        SendSessionMessage("confirmation-request", sessionId,
                           new
                           {
                               requestId = request.Token.RequestId.ToString(),
                               sessionGeneration = request.Token.Session.Generation,
                               kind = "paste",
                               request.SessionName,
                               request.ClipboardText,
                           });
    }

    private void PresentPathDropConfirmation(TerminalPathDropConfirmationRequest request)
    {
        var sessionId = new TerminalSessionId(request.Token.Session.SessionId);
        pendingSafetyConfirmation = new PendingSafetyConfirmation(request.Token, ConfirmationKind.PathDrop);
        SendSessionMessage("confirmation-request", sessionId,
                           new
                           {
                               requestId = request.Token.RequestId.ToString(),
                               sessionGeneration = request.Token.Session.Generation,
                               kind = "path-drop",
                               request.SessionName,
                               request.QuotedInput,
                           });
    }

    private void PresentUrlOpenConfirmation(TerminalUrlOpenConfirmationRequest request)
    {
        var sessionId = new TerminalSessionId(request.Token.Session.SessionId);
        pendingSafetyConfirmation = new PendingSafetyConfirmation(request.Token, ConfirmationKind.UrlOpen,
                                                                  request.Target, rendererInstanceId);
        SendSessionMessage("confirmation-request", sessionId,
                           new
                           {
                               requestId = request.Token.RequestId.ToString(),
                               sessionGeneration = request.Token.Session.Generation,
                               kind = "url-open",
                               request.SessionName,
                               targetUrl = request.Target.AbsoluteUri,
                           });
    }

    private async Task ApplyConfirmationResponseAsync(TerminalConfirmationResponse response)
    {
        var pending = pendingSafetyConfirmation;
        if (pending is null || pending.Token != response.Token || isDisposed == true)
        {
            return;
        }

        pendingSafetyConfirmation = null;
        try
        {
            if (pending.Kind == ConfirmationKind.Close)
            {
                _ = await sessionCoordinator.ApplyCloseConfirmationAsync(response, lifetimeCancellation.Token);
                return;
            }

            if (pending.Kind == ConfirmationKind.Paste)
            {
                _ = await sessionCoordinator.ApplyPasteConfirmationAsync(response, lifetimeCancellation.Token);
                return;
            }

            if (pending.Kind == ConfirmationKind.UrlOpen)
            {
                await ApplyUrlOpenConfirmationAsync(pending, response);
                return;
            }

            _ = await sessionCoordinator.ApplyPathDropConfirmationAsync(response, lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException or Win32Exception or
                UnauthorizedAccessException)
        {
            if (isDisposed == false && exception is not OperationCanceledException)
            {
                var sessionId = new TerminalSessionId(response.Token.Session.SessionId);
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "SafetyConfirmation",
                                    $"A terminal {pending.Kind.ToString().ToLowerInvariant()} confirmation could not be applied.",
                                    exception);
                if (ContainsSession(sessionId) == true)
                {
                    SendSessionMessage("session-error", sessionId,
                                       new { message = "요청을 안전하게 처리하지 못했습니다. 다시 시도해 주세요." });
                }
            }
        }
    }

    private void CancelPendingSafetyConfirmation(bool sendRendererCancellation)
    {
        var pending = pendingSafetyConfirmation;
        pendingSafetyConfirmation = null;
        sessionCoordinator.CancelPendingConfirmation();
        if (pending is null || sendRendererCancellation == false || rendererFailed == true)
        {
            return;
        }

        var sessionId = new TerminalSessionId(pending.Token.Session.SessionId);
        SendSessionMessage("confirmation-cancel", sessionId,
                           new
                           {
                               requestId = pending.Token.RequestId.ToString(),
                               sessionGeneration = pending.Token.Session.Generation,
                           });
    }

    private async Task RestartSessionAsync(TerminalSessionId sessionId)
    {
        if (isDisposed == true)
        {
            return;
        }

        CancelPendingSafetyConfirmation(sendRendererCancellation: true);
        RemovePendingOutput(sessionId);
        SendSessionMessage("reset", sessionId, new { });
        try
        {
            await sessionCoordinator.RestartAsync(sessionId, lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException or Win32Exception or UnauthorizedAccessException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RestartSession",
                                    $"Terminal session {sessionId} could not be restarted.", exception);
                SendSessionMessage("session-error", sessionId, new { message = ToUserMessage(exception) });
            }
        }
    }

    private async Task WriteInputAsync(TerminalSessionId sessionId, string data)
    {
        if (isDisposed == true)
        {
            return;
        }

        try
        {
            await sessionCoordinator.WriteAsync(sessionId, data, lifetimeCancellation.Token);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or OperationCanceledException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "WriteInput",
                                    "Terminal input could not be delivered.", exception);
                SendSessionMessage("session-error", sessionId, new { message = "shell 입력 연결이 끊겼습니다. 다시 시작해 주세요." });
            }
        }
    }

    private async Task ApplyUrlOpenConfirmationAsync(PendingSafetyConfirmation pending,
                                                     TerminalConfirmationResponse response)
    {
        if (pending.UrlTarget is null || pending.RendererInstanceId == Guid.Empty ||
            pending.RendererInstanceId != rendererInstanceId ||
            rendererSessionGenerations.TryGetValue(new TerminalSessionId(response.Token.Session.SessionId),
                                                   out var currentGeneration) == false ||
            currentGeneration != response.Token.Session.Generation ||
            sessionCoordinator.Snapshot.ActiveSessionId?.Value != response.Token.Session.SessionId ||
            response.Result != TerminalConfirmationResult.Confirmed)
        {
            return;
        }

        var opened = await urlOpenService.TryOpenAsync(pending.UrlTarget, lifetimeCancellation.Token);
        if (opened == true || isDisposed == true)
        {
            return;
        }

        diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "OpenUrl",
                            "The confirmed web address could not be handed to the default browser.");
        var sessionId = new TerminalSessionId(response.Token.Session.SessionId);
        if (ContainsSession(sessionId) == true && pending.RendererInstanceId == rendererInstanceId)
        {
            SendSessionMessage("url-open-result", sessionId,
                               new { succeeded = false, message = "기본 브라우저에서 주소를 열지 못했습니다." });
        }
    }

    private void HandlePathDrop(RendererMessage message, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (message.SessionId is not { } sessionId || message.RendererInstanceId != rendererInstanceId ||
            message.SessionGeneration < 1 || pendingSafetyConfirmation is not null)
        {
            return;
        }

        try
        {
            var objects = eventArgs.AdditionalObjects;
            if (objects.Count == 0 || objects.Count > TerminalPathDropFormatter.MaximumPathCount)
            {
                SendPathDropFailure(sessionId, "한 번에 1개 이상 32개 이하의 로컬 파일이나 폴더를 놓아 주세요.");
                return;
            }

            var paths = new List<string>(objects.Count);
            foreach (var additionalObject in objects)
            {
                if (additionalObject is not CoreWebView2File file || string.IsNullOrWhiteSpace(file.Path) == true)
                {
                    SendPathDropFailure(sessionId, "로컬 파일이나 폴더 경로만 넣을 수 있습니다.");
                    return;
                }

                paths.Add(file.Path);
            }

            var session = new TerminalSessionReference(sessionId.Value, message.SessionGeneration);
            var result = sessionCoordinator.RequestPathDropConfirmation(session, paths);
            if (result.Succeeded == false || result.ConfirmationRequest is null)
            {
                SendPathDropFailure(sessionId, result.ErrorMessage ?? "경로를 안전하게 넣을 수 없습니다.");
                return;
            }

            PresentPathDropConfirmation(result.ConfirmationRequest);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or NotSupportedException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "PathDrop",
                                "A dropped-path request could not be read from the WebView2 message.", exception);
            if (message.RendererInstanceId == rendererInstanceId && ContainsSession(sessionId) == true)
            {
                SendPathDropFailure(sessionId, "드롭한 경로를 확인하지 못했습니다. Explorer에서 다시 시도해 주세요.");
            }
        }
    }

    private void HandleUrlOpenRequest(RendererMessage message)
    {
        var snapshot = sessionCoordinator.Snapshot;
        if (message.SessionId is not { } sessionId || message.UrlOpenTarget is null ||
            message.RendererInstanceId != rendererInstanceId || rendererInstanceId == Guid.Empty ||
            message.SessionGeneration < 1 || pendingSafetyConfirmation is not null ||
            rendererSessionGenerations.TryGetValue(sessionId, out var currentGeneration) == false ||
            currentGeneration != message.SessionGeneration ||
            snapshot.ActiveSessionId != sessionId)
        {
            return;
        }

        var session = new TerminalSessionReference(sessionId.Value, currentGeneration);
        var token = new TerminalConfirmationToken(TerminalConfirmationRequestId.CreateNew(), session);
        var sessionName = snapshot.Tabs.Single(tab => tab.SessionId == sessionId).Name;
        PresentUrlOpenConfirmation(new TerminalUrlOpenConfirmationRequest(token, sessionName,
                                                                          message.UrlOpenTarget));
    }

    private void SendPathDropFailure(TerminalSessionId sessionId, string message)
    {
        SendSessionMessage("path-drop-result", sessionId, new { succeeded = false, message });
    }

    private void ResizeSession(TerminalSessionId sessionId, int requestedColumns, int requestedRows)
    {
        try
        {
            _ = sessionCoordinator.Resize(sessionId, requestedColumns, requestedRows);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Resize",
                                "The ConPTY resize request was rejected.", exception);
        }
    }

    private void Session_WorkspaceChanged(TerminalWorkspaceSnapshot snapshot)
    {
        _ = snapshot;
        if (isDisposed == true)
        {
            return;
        }

        if (Dispatcher.CheckAccess() == true)
        {
            SyncWorkspace(sessionCoordinator.Snapshot);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => SyncWorkspace(sessionCoordinator.Snapshot));
    }

    private void SyncWorkspace(TerminalWorkspaceSnapshot snapshot)
    {
        if (isDisposed == true || rendererFailed == true || renderer.CoreWebView2 is null)
        {
            return;
        }

        var currentSessionIds = snapshot.Tabs
            .Select(tab => tab.SessionId)
            .ToHashSet();
        var removedSessionIds = rendererSessionIds
            .Where(sessionId => currentSessionIds.Contains(sessionId) == false)
            .ToArray();

        foreach (var sessionId in removedSessionIds)
        {
            SendSessionMessage("remove-session", sessionId, new { });
            RemovePendingOutput(sessionId);
        }

        var canAddSession = snapshot.Tabs.Count < TerminalTabRegistry.DefaultMaximumTabs;
        var sessionLifetimes = sessionCoordinator.NewOutputState.States
            .ToDictionary(state => new TerminalSessionId(state.Session.SessionId), state => state.Session.Generation);
        for (var index = 0; index < snapshot.Tabs.Count; index++)
        {
            var tab = snapshot.Tabs[index];
            SendSessionMessage("session-upsert", tab.SessionId,
                               new
                               {
                                   tab.Name,
                                   state = tab.State.ToString().ToLowerInvariant(),
                                   tab.ExitCode,
                                   order = index,
                                   canAddSession,
                                   sessionGeneration = sessionLifetimes[tab.SessionId],
                                   tab.StartingDirectory,
                                   homeDirectory = defaultShell?.WorkingDirectory ?? tab.StartingDirectory,
                                   shellKind = tab.ShellKind?.ToString().ToLowerInvariant() ?? "automatic",
                               });

            if (tab.State == TerminalSessionState.Exited)
            {
                SendSessionMessage("session-error", tab.SessionId,
                                   new
                                   {
                                       message = tab.ExitCode is null
                                           ? "shell이 종료됐습니다."
                                           : $"shell이 종료됐습니다 (exit {tab.ExitCode.Value}).",
                                   });
            }
            else if (tab.State == TerminalSessionState.Failed)
            {
                SendSessionMessage("session-error", tab.SessionId, new { message = "shell session을 시작하거나 유지하지 못했습니다." });
            }
        }

        if (snapshot.ActiveSessionId is { } activeSessionId)
        {
            SendSessionMessage("activate-session", activeSessionId, new { });
        }

        if (rendererFailed == false)
        {
            rendererSessionIds.Clear();
            rendererSessionIds.UnionWith(currentSessionIds);
            SyncNewOutputState(sessionCoordinator.NewOutputState);
            SyncSavedTabs();
        }
    }

    private void Session_NewOutputStateChanged(TerminalNewOutputStateSnapshot snapshot)
    {
        if (isDisposed == true)
        {
            return;
        }

        if (Dispatcher.CheckAccess() == true)
        {
            SyncNewOutputState(snapshot);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => SyncNewOutputState(snapshot));
    }

    private void SyncNewOutputState(TerminalNewOutputStateSnapshot snapshot)
    {
        if (isDisposed == true || rendererFailed == true || renderer.CoreWebView2 is null)
        {
            return;
        }

        _ = snapshot;
        var currentSnapshot = sessionCoordinator.NewOutputState;
        var liveSessionIds = currentSnapshot.States
            .Select(state => new TerminalSessionId(state.Session.SessionId))
            .ToHashSet();
        foreach (var removedSessionId in rendererSessionGenerations.Keys
                     .Where(sessionId => liveSessionIds.Contains(sessionId) == false)
                     .ToArray())
        {
            rendererSessionGenerations.Remove(removedSessionId);
        }

        foreach (var state in currentSnapshot.States)
        {
            var sessionId = new TerminalSessionId(state.Session.SessionId);
            if (rendererSessionIds.Contains(sessionId) == false)
            {
                continue;
            }

            if (rendererSessionGenerations.TryGetValue(sessionId, out var previousGeneration) == true &&
                previousGeneration != state.Session.Generation)
            {
                RemovePendingOutput(sessionId);
            }

            rendererSessionGenerations[sessionId] = state.Session.Generation;
            SendSessionMessage("new-output-state", sessionId,
                               new
                               {
                                   sessionGeneration = state.Session.Generation,
                                   state.HasNewOutput,
                               });
        }
    }

    private void RemovePendingOutput(TerminalSessionId sessionId)
    {
        lock (outputLock)
        {
            pendingOutput.Remove(sessionId);
            outputFlushScheduled.Remove(sessionId);
            outputOverflowReported.Remove(sessionId);
        }
    }

    private void Session_OutputReceived(TerminalSessionOutput output)
    {
        if (isDisposed == true || sessionCoordinator.IsCurrentSession(output.Session) == false)
        {
            return;
        }

        var sessionId = output.SessionId;
        var data = output.Data;
        var reportOverflow = false;
        var scheduleFlush = false;

        lock (outputLock)
        {
            if (pendingOutput.TryGetValue(sessionId, out var sessionOutput) == false ||
                sessionOutput.Generation != output.Session.Generation)
            {
                sessionOutput = new PendingSessionOutput(output.Session.Generation);
                pendingOutput[sessionId] = sessionOutput;
            }

            if (data.Length >= MaximumPendingOutputLength)
            {
                sessionOutput.Data.Clear();
                sessionOutput.Data.Append(data.AsSpan(data.Length - MaximumPendingOutputLength));
                reportOverflow = outputOverflowReported.Add(sessionId);
            }
            else
            {
                var overflowLength = sessionOutput.Data.Length + data.Length - MaximumPendingOutputLength;
                if (overflowLength > 0)
                {
                    sessionOutput.Data.Remove(0, overflowLength);
                    reportOverflow = outputOverflowReported.Add(sessionId);
                }

                sessionOutput.Data.Append(data);
            }

            scheduleFlush = outputFlushScheduled.Add(sessionId);
        }

        if (reportOverflow == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "OutputBacklog",
                                $"Terminal session {sessionId} exceeded its bounded renderer backlog; the oldest data was discarded.");
        }

        if (scheduleFlush == true)
        {
            _ = Dispatcher.BeginInvoke(() => FlushOutput(sessionId));
        }
    }

    private void FlushOutput(TerminalSessionId sessionId)
    {
        if (isDisposed == true)
        {
            RemovePendingOutput(sessionId);
            return;
        }

        if (rendererFailed == true || renderer.CoreWebView2 is null)
        {
            lock (outputLock)
            {
                outputFlushScheduled.Remove(sessionId);
            }

            return;
        }

        string output = string.Empty;
        long generation;
        bool hasMoreOutput;

        lock (outputLock)
        {
            if (pendingOutput.TryGetValue(sessionId, out var sessionOutput) == false)
            {
                outputFlushScheduled.Remove(sessionId);
                return;
            }

            generation = sessionOutput.Generation;
            var length = Math.Min(sessionOutput.Data.Length, MaximumOutputBatchLength);
            output = sessionOutput.Data.ToString(0, length);
            sessionOutput.Data.Remove(0, length);
            hasMoreOutput = sessionOutput.Data.Length > 0;
            if (hasMoreOutput == false)
            {
                pendingOutput.Remove(sessionId);
                outputFlushScheduled.Remove(sessionId);
            }

            if (sessionOutput.Data.Length < MaximumPendingOutputLength / 2)
            {
                outputOverflowReported.Remove(sessionId);
            }
        }

        var session = new TerminalSessionReference(sessionId.Value, generation);
        var isCurrentGeneration = sessionCoordinator.IsCurrentSession(session);
        if (isCurrentGeneration == false)
        {
            RemovePendingOutput(sessionId);
            return;
        }

        if (string.IsNullOrEmpty(output) == false)
        {
            SendSessionMessage("output", sessionId, new { sessionGeneration = generation, data = output });
        }

        if (hasMoreOutput == true && isDisposed == false)
        {
            _ = Dispatcher.BeginInvoke(() => FlushOutput(sessionId));
        }
    }

    private void SchedulePendingOutputFlushes()
    {
        TerminalSessionId[] sessionIds;
        lock (outputLock)
        {
            sessionIds = pendingOutput.Keys
                .Where(sessionId => outputFlushScheduled.Add(sessionId) == true)
                .ToArray();
        }

        foreach (var sessionId in sessionIds)
        {
            _ = Dispatcher.BeginInvoke(() => FlushOutput(sessionId));
        }
    }

    private void Session_Exited(TerminalSessionExit sessionExit)
    {
        if (isDisposed == true || sessionCoordinator.IsCurrentSession(sessionExit.Session) == false)
        {
            return;
        }

        var exitCode = sessionExit.ExitCode;

        diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "ShellExit",
                            $"Terminal session {sessionExit.SessionId} exited with code {exitCode}.");
    }

    private void SendInitializeMessage()
    {
        if (appearanceState is null)
        {
            return;
        }

        SendAppearanceMessage("initialize", appearanceState.MarkRendererReady());
    }

    private void SendAppearanceMessage(string type, TerminalAppearanceSettings appearance)
    {
        SendGlobalMessage(type,
                          new
                          {
                              appearance.FontFamily,
                              appearance.FontSize,
                              theme = new
                              {
                                  appearance.Theme.Canvas,
                                  appearance.Theme.Foreground,
                                  appearance.Theme.Muted,
                                  appearance.Theme.Accent,
                                  appearance.Theme.Cursor,
                                  appearance.Theme.Selection,
                                  appearance.Theme.AnsiPalette,
                              },
                          });
    }

    private void SendGlobalMessage(string type, object payload)
    {
        PostRendererMessage(RendererProtocol.SerializeGlobalMessage(type, payload));
    }

    private void SendSessionMessage(string type, TerminalSessionId sessionId, object payload)
    {
        PostRendererMessage(RendererProtocol.SerializeSessionMessage(type, sessionId, payload));
    }

    private void PostRendererMessage(string json)
    {
        var core = renderer.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            core.PostWebMessageAsJson(json);
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "RendererOutput",
                                "The renderer could not accept a host message.", exception);
            MarkRendererFailed("Terminal renderer 연결이 끊겼습니다. shell session은 유지되며 " +
                               "renderer를 다시 연결할 수 있습니다.");
        }
    }

    private void CopyToClipboard(string data)
    {
        if (string.IsNullOrEmpty(data) == true)
        {
            return;
        }

        try
        {
            Clipboard.SetText(data);
        }
        catch (COMException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Copy",
                                "The clipboard was temporarily unavailable.", exception);
        }
    }

    private void RequestPasteFromClipboard(TerminalSessionId sessionId)
    {
        if (pendingSafetyConfirmation is not null)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsText() == true)
            {
                var data = Clipboard.GetText();
                if (data.Length <= RendererProtocol.MaximumMessageLength)
                {
                    var confirmation = sessionCoordinator.RequestPasteConfirmation(sessionId, data);
                    if (confirmation is null)
                    {
                        if (data.Contains('\r') == false && data.Contains('\n') == false)
                        {
                            SendSessionMessage("paste", sessionId, new { data });
                        }

                        return;
                    }

                    PresentPasteConfirmation(confirmation);
                }
                else
                {
                    diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Paste",
                                        "A clipboard payload larger than the renderer limit was rejected.");
                }
            }
        }
        catch (COMException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Paste",
                                "The clipboard was temporarily unavailable.", exception);
        }
        catch (InvalidOperationException exception)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "Paste",
                                "The clipboard paste request no longer targeted a live terminal session.", exception);
        }
    }

    private void ShowLoading(string message)
    {
        StatusHeading.Text = "STARBOARD / STARTING";
        StatusMessage.Text = message;
        StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        RetryButton.Visibility = Visibility.Collapsed;
        ExitButton.Visibility = Visibility.Collapsed;
        StatusSurface.Visibility = Visibility.Visible;
        renderer.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message, bool showExit)
    {
        StatusHeading.Text = "STARBOARD / TERMINAL OFFLINE";
        StatusMessage.Text = message;
        StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        RetryButton.Visibility = Visibility.Visible;
        ExitButton.Visibility = showExit ? Visibility.Visible : Visibility.Collapsed;
        StatusSurface.Visibility = Visibility.Visible;
        renderer.Visibility = Visibility.Collapsed;
    }

    private void ShowTerminal()
    {
        StatusSurface.Visibility = Visibility.Collapsed;
        renderer.Visibility = Visibility.Visible;
    }

    private void ApplyTheme(TerminalTheme theme)
    {
        var canvas = ParseColor(theme.Canvas);
        var foreground = ParseColor(theme.Foreground);
        var muted = ParseColor(theme.Muted);
        var accent = ParseColor(theme.Accent);
        var selection = ParseColor(theme.Selection);
        var error = ParseColor(theme.AnsiPalette[1]);

        Resources["CanvasBrush"] = new SolidColorBrush(canvas);
        Resources["ForegroundBrush"] = new SolidColorBrush(foreground);
        Resources["MutedBrush"] = new SolidColorBrush(muted);
        Resources["RuleBrush"] = new SolidColorBrush(selection);
        Resources["RuleStrongBrush"] = new SolidColorBrush(Blend(accent, canvas, 0.35));
        Resources["AccentBrush"] = new SolidColorBrush(accent);
        Resources["AccentHoverBrush"] = new SolidColorBrush(Blend(accent, foreground, 0.16));
        Resources["AccentPressedBrush"] = new SolidColorBrush(Blend(accent, canvas, 0.18));
        Resources["FocusBrush"] = new SolidColorBrush(accent);
        Resources["ErrorBrush"] = new SolidColorBrush(error);
        Resources["ButtonInkBrush"] = FindBestButtonInk(accent);
        SetRendererBackground(canvas);
    }

    private static TerminalAppearanceSettings CreateAppearanceSnapshot(TerminalAppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(appearance.Theme);
        if (string.IsNullOrWhiteSpace(appearance.FontFamily) == true)
        {
            throw new ArgumentException("Terminal font family cannot be empty.", nameof(appearance));
        }

        if (double.IsFinite(appearance.FontSize) == false || appearance.FontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance.FontSize,
                                                  "Terminal font size must be a positive finite number.");
        }

        if (appearance.Theme.AnsiPalette is null || appearance.Theme.AnsiPalette.Count != 16)
        {
            throw new ArgumentException("Terminal themes require exactly 16 ANSI colors.", nameof(appearance));
        }

        var palette = appearance.Theme.AnsiPalette
            .Select(ValidateAppearanceColor)
            .ToArray();
        var canvas = ValidateAppearanceColor(appearance.Theme.Canvas);
        var foreground = ValidateAppearanceColor(appearance.Theme.Foreground);
        var muted = ValidateAppearanceColor(appearance.Theme.Muted);
        var accent = ValidateAppearanceColor(appearance.Theme.Accent);
        var cursor = ValidateAppearanceColor(appearance.Theme.Cursor);
        var selection = ValidateAppearanceColor(appearance.Theme.Selection);

        var theme = new TerminalTheme(canvas, foreground, muted, accent, cursor, selection, palette);

        return new TerminalAppearanceSettings(appearance.FontFamily, appearance.FontSize, theme);
    }

    private static string ValidateAppearanceColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color) == true)
        {
            throw new ArgumentException("Terminal appearance colors cannot be empty.");
        }

        _ = ParseColor(color);

        return color;
    }

    private void SetRendererBackground(Color canvas)
    {
        renderer.DefaultBackgroundColor = System.Drawing.Color.FromArgb(canvas.A, canvas.R, canvas.G, canvas.B);
    }

    private SolidColorBrush FindBestButtonInk(Color background)
    {
        var dark = ((SolidColorBrush)FindResource("ButtonInkDarkBrush")).Color;
        var light = ((SolidColorBrush)FindResource("ButtonInkLightBrush")).Color;

        return new SolidColorBrush(ContrastRatio(dark, background) >= ContrastRatio(light, background)
                                       ? dark
                                       : light);
    }

    private static Color ParseColor(string value)
    {
        return (Color)ColorConverter.ConvertFromString(value);
    }

    private static Color Blend(Color source, Color target, double targetWeight)
    {
        return Color.FromRgb((byte)Math.Round((source.R * (1 - targetWeight)) + (target.R * targetWeight)),
                             (byte)Math.Round((source.G * (1 - targetWeight)) + (target.G * targetWeight)),
                             (byte)Math.Round((source.B * (1 - targetWeight)) + (target.B * targetWeight)));
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        return (0.2126 * Linearize(color.R / 255.0)) +
               (0.7152 * Linearize(color.G / 255.0)) +
               (0.0722 * Linearize(color.B / 255.0));
    }

    private static double Linearize(double channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static TerminalSettingsApplyStatus GetFailureStatus(bool defaultShellApplied, bool appearanceAttempted,
                                                                bool restoreIncomplete)
    {
        if (defaultShellApplied == false && appearanceAttempted == false)
        {
            return TerminalSettingsApplyStatus.FailedWithoutChange;
        }

        return restoreIncomplete == true
            ? TerminalSettingsApplyStatus.FailedAndRestoreIncomplete
            : TerminalSettingsApplyStatus.FailedAndRestored;
    }

    private static bool IsRecoverableSettingsException(Exception exception)
    {
        return exception is ArgumentException or
               COMException or
               FileNotFoundException or
               FormatException or
               InvalidOperationException or
               NotSupportedException;
    }

    internal static bool IsRecoverableStartupException(Exception exception)
    {
        return exception is WebView2RuntimeNotFoundException or
               InvalidOperationException or
               IOException or
               UnauthorizedAccessException or
               COMException or
               Win32Exception;
    }

    internal static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            WebView2RuntimeNotFoundException =>
                "Microsoft Edge WebView2 Runtime이 없습니다. 다른 PC에서 Microsoft의 Evergreen " +
                "Standalone Installer를 받아 이 PC에 복사해 설치한 뒤 다시 시도하세요. 자세한 절차는 README를 확인하세요.",
            FileNotFoundException => exception.Message,
            DirectoryNotFoundException => "시작 폴더가 없거나 접근할 수 없습니다. 폴더를 변경하거나 홈 폴더에서 다시 시도해 주세요.",
            COMException => "Microsoft Edge WebView2 Runtime을 시작하지 못했습니다.",
            UnauthorizedAccessException => "Starboard 로컬 데이터 폴더에 접근할 수 없습니다.",
            _ => "Terminal session을 시작하지 못했습니다. 로컬 로그를 확인한 뒤 다시 시도해 주세요.",
        };
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private enum ConfirmationKind
    {
        Close,
        Paste,
        PathDrop,
        UrlOpen,
    }

    private sealed record PendingSafetyConfirmation(TerminalConfirmationToken Token, ConfirmationKind Kind,
                                                    TerminalUrlOpenTarget? UrlTarget = null,
                                                    Guid RendererInstanceId = default);

    private sealed class PendingSessionOutput
    {
        internal PendingSessionOutput(long generation)
        {
            Generation = generation;
        }

        internal long Generation { get; }

        internal StringBuilder Data { get; } = new();
    }
}
