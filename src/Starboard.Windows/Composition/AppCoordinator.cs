using System.Windows;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Terminal;
using Starboard.Modules.Terminal.Contracts;
using Starboard.SharedKernel.Diagnostics;
using Starboard.Windows.Infrastructure;
using Starboard.Windows.Shell;
using Application = System.Windows.Application;

namespace Starboard.Windows.Composition;

internal sealed class AppCoordinator : IDisposable
{
    private readonly FileDiagnosticLog diagnosticLog;
    private readonly PreferencesModule preferencesModule;
    private readonly DesktopIntegrationModule desktopIntegrationModule;
    private readonly TerminalModule terminalModule;
    private readonly CommandCompletionNotificationCoordinator commandCompletionNotificationCoordinator;
    private readonly CancellationTokenSource lifetimeCancellation = new();

    private MainWindow? mainWindow;
    private SettingsApplicationService? settingsApplicationService;
    private SettingsWindowController? settingsWindowController;
    private Task? shutdownTask;
    private bool isDisposed;

    internal AppCoordinator()
    {
        diagnosticLog = new FileDiagnosticLog();
        preferencesModule = new PreferencesModule(diagnosticLog);
        desktopIntegrationModule = new DesktopIntegrationModule(diagnosticLog, ApplyPanelOpacity);
        terminalModule = new TerminalModule(diagnosticLog, ApplyCollapsedHeightChangeAsync);
        commandCompletionNotificationCoordinator = new CommandCompletionNotificationCoordinator(
            terminalModule.IsCurrentSession,
            desktopIntegrationModule.SetCommandCompletionNotificationSettings,
            desktopIntegrationModule.NotifyCommandCompletion);
        terminalModule.CommandCompleted += HandleCommandCompleted;
        desktopIntegrationModule.PanelVisibilityToggleRequested += HandlePanelVisibilityToggleRequested;
        desktopIntegrationModule.PanelActivationToggleRequested += HandlePanelActivationToggleRequested;
        desktopIntegrationModule.PanelSummonRequested += HandlePanelSummonRequested;
        desktopIntegrationModule.PanelPresentationRequested += HandlePanelPresentationRequested;
        desktopIntegrationModule.PanelCollapsedHeightChangeRequested += HandlePanelCollapsedHeightChangeRequested;
        desktopIntegrationModule.SettingsRequested += HandleSettingsRequested;
        desktopIntegrationModule.ExitRequested += HandleExitRequested;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var smokeResult = PortableReleaseSmokeCheck.TryRun(Environment.GetCommandLineArgs(), ProductBuildInfo.Current,
                                                           AppContext.BaseDirectory);
        if (smokeResult is not null)
        {
            var application = Application.Current
                ?? throw new InvalidOperationException("The WPF application is unavailable.");
            application.Shutdown(smokeResult.ExitCode);

            return;
        }

        var loadResult = await preferencesModule.LoadAsync(cancellationToken);
        var settings = loadResult.Settings;

        mainWindow = new MainWindow
        {
            Height = settings.CollapsedHeightDip,
            Opacity = settings.Opacity,
        };
        mainWindow.Show();

        var windowHandle = mainWindow.AttachDesktopIntegration(desktopIntegrationModule);
        desktopIntegrationModule.Attach(windowHandle, new PanelOptions(settings.CollapsedHeightDip));

        var terminalOptions = SettingsApplicationService.ToTerminalOptions(settings);
        mainWindow.SetTerminalContent(terminalModule.Surface);
        await terminalModule.StartAsync(terminalOptions, cancellationToken);

        var desktopResult = desktopIntegrationModule.ApplySettings(SettingsApplicationService.ToDesktopSettings(settings));
        var effectiveSettings = SettingsApplicationService.WithDesktopSettings(settings,
                                                                               desktopResult.EffectiveSettings);
        ApplyHostSettings(effectiveSettings);

        settingsApplicationService = new SettingsApplicationService(preferencesModule, terminalModule,
                                                                    desktopIntegrationModule, settings,
                                                                    effectiveSettings, ApplyHostSettings,
                                                                    diagnosticLog);
        settingsWindowController = new SettingsWindowController(CreateSettingsWindow);

        if (desktopResult.Status != DesktopSettingsApplyStatus.Applied)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Host", "ApplyStartupSettings",
                                "Saved desktop settings could not be fully applied during startup.");
        }

        if (loadResult.RecoveryMessage is not null)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Host", "SettingsRecovery", loadResult.RecoveryMessage);
        }
    }

    public void Dispose()
    {
        ShutdownAsync().GetAwaiter().GetResult();
    }

    internal Task ShutdownAsync()
    {
        shutdownTask ??= ShutdownCoreAsync();

        return shutdownTask;
    }

    private async Task ShutdownCoreAsync()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        lifetimeCancellation.Cancel();
        terminalModule.CommandCompleted -= HandleCommandCompleted;
        commandCompletionNotificationCoordinator.Stop();
        desktopIntegrationModule.PanelVisibilityToggleRequested -= HandlePanelVisibilityToggleRequested;
        desktopIntegrationModule.PanelActivationToggleRequested -= HandlePanelActivationToggleRequested;
        desktopIntegrationModule.PanelSummonRequested -= HandlePanelSummonRequested;
        desktopIntegrationModule.PanelPresentationRequested -= HandlePanelPresentationRequested;
        desktopIntegrationModule.PanelCollapsedHeightChangeRequested -= HandlePanelCollapsedHeightChangeRequested;
        desktopIntegrationModule.SettingsRequested -= HandleSettingsRequested;
        desktopIntegrationModule.ExitRequested -= HandleExitRequested;
        settingsWindowController?.Dispose();
        var workspaceResult = await terminalModule.ShutdownAsync();
        LogWorkspaceShutdownResult(workspaceResult);
        settingsApplicationService?.Dispose();
        desktopIntegrationModule.Dispose();
        mainWindow?.Close();
        lifetimeCancellation.Dispose();
    }

    private void LogWorkspaceShutdownResult(TerminalWorkspacePersistenceResult result)
    {
        var level = result.Status == TerminalWorkspacePersistenceStatus.Failed
            ? DiagnosticLevel.Warning
            : DiagnosticLevel.Information;
        var message = result.Status switch
        {
            TerminalWorkspacePersistenceStatus.Succeeded => "The latest terminal workspace was flushed during shutdown.",
            TerminalWorkspacePersistenceStatus.Failed =>
                result.FailureDetail ?? "The latest terminal workspace could not be flushed during shutdown.",
            TerminalWorkspacePersistenceStatus.Skipped =>
                result.FailureDetail ?? "Terminal workspace flush was not required during shutdown.",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        diagnosticLog.Write(level, "Host", result.Operation.ToString(), message);
    }

    private void HandlePanelVisibilityToggleRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess() == false)
        {
            application.Dispatcher.Invoke(TogglePanelVisibilityFromTray);
            return;
        }

        TogglePanelVisibilityFromTray();
    }

    private void HandleCommandCompleted(object? sender, TerminalCommandCompletedEventArgs eventArguments)
    {
        _ = sender;
        commandCompletionNotificationCoordinator.HandleCompletion(eventArguments.Completion);
    }

    private void HandlePanelActivationToggleRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess() == false)
        {
            application.Dispatcher.Invoke(TogglePanelActivation);
            return;
        }

        TogglePanelActivation();
    }

    private void HandlePanelSummonRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess() == false)
        {
            application.Dispatcher.Invoke(ShowAndActivatePanel);
            return;
        }

        ShowAndActivatePanel();
    }

    private void HandlePanelPresentationRequested(bool isVisible)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess() == false)
        {
            application.Dispatcher.Invoke(() => ApplyPanelPresentation(isVisible));
            return;
        }

        ApplyPanelPresentation(isVisible);
    }

    private void HandlePanelCollapsedHeightChangeRequested(
        object? sender, PanelCollapsedHeightChangeEventArgs eventArguments)
    {
        _ = sender;
        var phase = eventArguments.Phase switch
        {
            PanelCollapsedHeightChangePhase.Preview => TerminalCollapsedHeightChangePhase.Preview,
            PanelCollapsedHeightChangePhase.Commit => TerminalCollapsedHeightChangePhase.Commit,
            _ => throw new ArgumentOutOfRangeException(nameof(eventArguments)),
        };
        var request = new TerminalCollapsedHeightChangeRequest(
            new TerminalCollapsedHeightChangeRequestId(eventArguments.RequestId), phase,
            eventArguments.RequestedHeightDip, eventArguments.LastSavedHeightDip);
        _ = ProcessPanelCollapsedHeightChangeAsync(request);
    }

    private async Task ProcessPanelCollapsedHeightChangeAsync(TerminalCollapsedHeightChangeRequest request)
    {
        try
        {
            _ = await terminalModule.RequestCollapsedHeightChangeAsync(request, lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested == true)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            if (isDisposed == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Host", "ChangeCollapsedHeight",
                                    "The panel height request could not be completed.", exception);
            }
        }
    }

    private ValueTask<TerminalCollapsedHeightChangeResult> ApplyCollapsedHeightChangeAsync(
        TerminalCollapsedHeightChangeRequest request, CancellationToken cancellationToken)
    {
        var applicationService = settingsApplicationService;
        if (applicationService is null || isDisposed == true)
        {
            return ValueTask.FromResult(new TerminalCollapsedHeightChangeResult(
                request.RequestId, TerminalCollapsedHeightChangeStatus.Failed, request.LastSavedHeightDip,
                "패널 높이 저장 기능을 사용할 수 없습니다."));
        }

        return applicationService.ApplyCollapsedHeightChangeAsync(request, cancellationToken);
    }

    private void HandleExitRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess() == false)
        {
            application.Dispatcher.Invoke(application.Shutdown);
            return;
        }

        application.Shutdown();
    }

    private void HandleSettingsRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess() == false)
        {
            application.Dispatcher.Invoke(OpenSettingsOnExplicitRequest);
            return;
        }

        OpenSettingsOnExplicitRequest();
    }

    private void TogglePanelActivation()
    {
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.IsVisible == true && mainWindow.IsActive == true)
        {
            HidePanel();
            return;
        }

        ShowAndActivatePanel();
    }

    private void TogglePanelVisibilityFromTray()
    {
        var isVisible = desktopIntegrationModule.TogglePanelVisibility();
        if (isVisible == true)
        {
            desktopIntegrationModule.ActivatePanel();
        }
    }

    private void HidePanel()
    {
        if (mainWindow is null)
        {
            return;
        }

        desktopIntegrationModule.SetPanelVisible(false);
    }

    private void ShowAndActivatePanel()
    {
        if (mainWindow is null)
        {
            return;
        }

        desktopIntegrationModule.SetPanelVisible(true);
        desktopIntegrationModule.ActivatePanel();
    }

    private void ApplyPanelPresentation(bool isVisible)
    {
        if (mainWindow is null)
        {
            return;
        }

        terminalModule.NotifyPanelVisibilityChanged(isVisible);

        if (isVisible == true)
        {
            if (mainWindow.IsVisible == false)
            {
                mainWindow.Show();
            }

            return;
        }

        if (mainWindow.IsVisible == true)
        {
            mainWindow.Hide();
        }
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var applicationService = settingsApplicationService
            ?? throw new InvalidOperationException("Settings application is unavailable before startup completes.");
        var session = PreferencesModule.CreateSettingsEditor(applicationService.PersistedSettings, applicationService);

        return new SettingsWindow(session, applicationService, ProductBuildInfo.Current);
    }

    private void OpenSettingsOnExplicitRequest()
    {
        settingsWindowController?.OpenOnExplicitUserRequest();
    }

    private void ApplyHostAppearance(AppSettings settings)
    {
        var window = mainWindow
            ?? throw new InvalidOperationException("The main window is unavailable.");
        window.ApplyAppearance(settings, PreferencesModule.GetTheme(settings.Theme));
    }

    private void ApplyHostSettings(AppSettings settings)
    {
        ApplyHostAppearance(settings);
        commandCompletionNotificationCoordinator.ApplySettings(settings.CommandCompletionNotificationsEnabled);
    }

    private void ApplyPanelOpacity(double opacity)
    {
        var window = mainWindow
            ?? throw new InvalidOperationException("The main window is unavailable.");
        window.Dispatcher.VerifyAccess();
        window.Opacity = opacity;
    }
}
