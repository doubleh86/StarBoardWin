using System.Windows;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences;
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
    private bool isDisposed;

    internal AppCoordinator()
    {
        diagnosticLog = new FileDiagnosticLog();
        preferencesModule = new PreferencesModule(diagnosticLog);
        desktopIntegrationModule = new DesktopIntegrationModule(diagnosticLog);
        terminalModule = new TerminalModule(diagnosticLog);
        desktopIntegrationModule.PanelVisibilityToggleRequested += HandlePanelVisibilityToggleRequested;
        desktopIntegrationModule.PanelActivationToggleRequested += HandlePanelActivationToggleRequested;
        desktopIntegrationModule.PanelSummonRequested += HandlePanelSummonRequested;
        desktopIntegrationModule.ExitRequested += HandleExitRequested;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
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

        var theme = PreferencesModule.GetTheme(settings.Theme);
        var terminalTheme = new TerminalTheme(
            theme.Canvas,
            theme.Foreground,
            theme.Muted,
            theme.Accent,
            theme.Cursor,
            theme.Selection,
            theme.AnsiPalette);
        var terminalOptions = new TerminalOptions(
            settings.ShellExecutable,
            settings.FontFamily,
            settings.FontSize,
            terminalTheme);
        mainWindow.SetTerminalContent(terminalModule.Surface);
        await terminalModule.StartAsync(terminalOptions, cancellationToken);

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
        desktopIntegrationModule.ExitRequested -= HandleExitRequested;
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
            application.Dispatcher.Invoke(TogglePanelVisibility);
            return;
        }

        TogglePanelVisibility();
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

    private void TogglePanelVisibility()
    {
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.IsVisible == true)
        {
            HidePanel();
            return;
        }

        ShowAndActivatePanel();
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

    private void HidePanel()
    {
        if (mainWindow is null)
        {
            return;
        }

        desktopIntegrationModule.SetPanelVisible(false);
        mainWindow.Hide();
    }

    private void ShowAndActivatePanel()
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.Show();
        desktopIntegrationModule.SetPanelVisible(true);
        _ = mainWindow.Activate();
        desktopIntegrationModule.ActivatePanel();
    }
}
