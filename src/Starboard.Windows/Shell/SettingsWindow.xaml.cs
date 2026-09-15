using System.ComponentModel;
using System.Windows;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Windows.Composition;

namespace Starboard.Windows.Shell;

internal partial class SettingsWindow : Window, ISettingsWindow
{
    private readonly SettingsEditorSession session;
    private readonly SettingsApplicationService applicationService;
    private bool isClosed;

    internal SettingsWindow(SettingsEditorSession session, SettingsApplicationService applicationService,
                            ProductBuildInfo buildInfo)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applicationService);
        ArgumentNullException.ThrowIfNull(buildInfo);

        this.session = session;
        this.applicationService = applicationService;
        InitializeComponent();
        BuildInfoText.Text = buildInfo.DisplayText;
        EditorContent.Content = session.Content;
        applicationService.StatusChanged += HandleStatusChanged;
        applicationService.CollapsedHeightChanged += HandleCollapsedHeightChanged;
        Closing += HandleClosing;
        Closed += HandleClosed;
        UpdateStatus();
        _ = ObserveCompletionAsync();
    }

    bool ISettingsWindow.IsMinimized => WindowState == WindowState.Minimized;

    void ISettingsWindow.Restore()
    {
        WindowState = WindowState.Normal;
    }

    void ISettingsWindow.Activate()
    {
        _ = Activate();
    }

    private async Task ObserveCompletionAsync()
    {
        var outcome = await session.Completion;
        _ = outcome;
        if (isClosed == true)
        {
            return;
        }

        await Dispatcher.InvokeAsync(Close);
    }

    private void HandleStatusChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (Dispatcher.CheckAccess() == false)
        {
            _ = Dispatcher.BeginInvoke(UpdateStatus);
            return;
        }

        UpdateStatus();
    }

    private void HandleClosing(object? sender, CancelEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (session.Completion.IsCompleted == false)
        {
            session.Cancel();
        }
    }

    private void HandleClosed(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        isClosed = true;
        applicationService.StatusChanged -= HandleStatusChanged;
        applicationService.CollapsedHeightChanged -= HandleCollapsedHeightChanged;
        Closing -= HandleClosing;
        Closed -= HandleClosed;
        EditorContent.Content = null;
    }

    private void HandleCollapsedHeightChanged(double collapsedHeightDip)
    {
        if (Dispatcher.CheckAccess() == false)
        {
            _ = Dispatcher.BeginInvoke(() => session.SynchronizeCollapsedHeight(collapsedHeightDip));
            return;
        }

        session.SynchronizeCollapsedHeight(collapsedHeightDip);
    }

    private void UpdateStatus()
    {
        var message = applicationService.StatusMessage;
        StatusText.Text = message ?? string.Empty;
        StatusBorder.Visibility = string.IsNullOrWhiteSpace(message) == true
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
