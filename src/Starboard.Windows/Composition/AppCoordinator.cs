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

    private MainWindow? mainWindow;
    private SettingsApplicationService? settingsApplicationService;
    private SettingsWindowController? settingsWindowController;
    private bool isDisposed;

    internal AppCoordinator()
    {
        diagnosticLog = new FileDiagnosticLog();
        preferencesModule = new PreferencesModule(diagnosticLog);
        desktopIntegrationModule = new DesktopIntegrationModule(diagnosticLog, ApplyPanelOpacity);
        terminalModule = new TerminalModule(diagnosticLog);
        desktopIntegrationModule.PanelVisibilityToggleRequested += HandlePanelVisibilityToggleRequested;
        desktopIntegrationModule.PanelActivationToggleRequested += HandlePanelActivationToggleRequested;
        desktopIntegrationModule.PanelSummonRequested += HandlePanelSummonRequested;
        desktopIntegrationModule.PanelPresentationRequested += HandlePanelPresentationRequested;
        desktopIntegrationModule.SettingsRequested += HandleSettingsRequested;
        desktopIntegrationModule.ExitRequested += HandleExitRequested;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var smokeResult = PortableReleaseSmokeCheck.TryRun(
            Environment.GetCommandLineArgs(),
            ProductBuildInfo.Current,
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
        desktopIntegrationModule.Attach(
            windowHandle,
            new PanelOptions(settings.CollapsedHeightDip));

        var terminalSettings = SettingsApplicationService.ToTerminalSettings(settings);
        var terminalOptions = new TerminalOptions(
            settings.ShellExecutable,
            settings.FontFamily,
            settings.FontSize,
            terminalSettings.Appearance.Theme);
        mainWindow.SetTerminalContent(terminalModule.Surface);
        await terminalModule.StartAsync(terminalOptions, cancellationToken);

        var desktopResult = desktopIntegrationModule.ApplySettings(
            SettingsApplicationService.ToDesktopSettings(settings));
        var effectiveSettings = SettingsApplicationService.WithDesktopSettings(
            settings,
            desktopResult.EffectiveSettings);
        ApplyHostAppearance(effectiveSettings);

        settingsApplicationService = new SettingsApplicationService(
            preferencesModule,
            terminalModule,
            desktopIntegrationModule,
            settings,
            effectiveSettings,
            ApplyHostAppearance,
            diagnosticLog);
        settingsWindowController = new SettingsWindowController(CreateSettingsWindow);

        if (desktopResult.Status != DesktopSettingsApplyStatus.Applied)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Host",
                "ApplyStartupSettings",
                "Saved desktop settings could not be fully applied during startup.");
        }

        if (loadResult.RecoveryMessage is not null)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Host",
                "SettingsRecovery",
                loadResult.RecoveryMessage);
        }
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        desktopIntegrationModule.PanelVisibilityToggleRequested -= HandlePanelVisibilityToggleRequested;
        desktopIntegrationModule.PanelActivationToggleRequested -= HandlePanelActivationToggleRequested;
        desktopIntegrationModule.PanelSummonRequested -= HandlePanelSummonRequested;
        desktopIntegrationModule.PanelPresentationRequested -= HandlePanelPresentationRequested;
        desktopIntegrationModule.SettingsRequested -= HandleSettingsRequested;
        desktopIntegrationModule.ExitRequested -= HandleExitRequested;
        settingsWindowController?.Dispose();
        settingsApplicationService?.Dispose();
        terminalModule.Dispose();
        desktopIntegrationModule.Dispose();
        mainWindow?.Close();
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
        var session = PreferencesModule.CreateSettingsEditor(
            applicationService.PersistedSettings,
            applicationService);
        return new SettingsWindow(
            session,
            applicationService,
            ProductBuildInfo.Current);
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

    private void ApplyPanelOpacity(double opacity)
    {
        var window = mainWindow
            ?? throw new InvalidOperationException("The main window is unavailable.");
        window.Dispatcher.VerifyAccess();
        window.Opacity = opacity;
    }
}
