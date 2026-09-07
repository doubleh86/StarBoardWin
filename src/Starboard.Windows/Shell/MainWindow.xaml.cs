using System.Windows;
using System.Windows.Interop;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Windows.Shell;

public partial class MainWindow : Window
{
    private DesktopIntegrationModule? desktopIntegration;
    private HwndSource? windowSource;

    internal MainWindow()
    {
        InitializeComponent();
        Activated += HandleActivated;
        Deactivated += HandleDeactivated;
        Closed += HandleClosed;
    }

    internal void SetTerminalContent(UIElement content)
    {
        TerminalContent.Content = content;
    }

    internal nint AttachDesktopIntegration(DesktopIntegrationModule module)
    {
        desktopIntegration = module;
        var helper = new WindowInteropHelper(this);
        windowSource = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("The Starboard window source is unavailable.");
        windowSource.AddHook(WindowProcedure);
        return helper.Handle;
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        var module = desktopIntegration;
        if (module is null)
        {
            return 0;
        }

        var windowMessage = new WindowMessage(
            windowHandle,
            message,
            unchecked((nuint)wordParameter),
            longParameter);
        if (module.HandleWindowMessage(windowMessage) == true)
        {
            handled = true;
        }

        if (message == DesktopIntegrationModule.WindowMessageDpiChanged)
        {
            _ = Dispatcher.BeginInvoke(RefreshDesktopIntegrationAfterDpiChange);
        }

        return 0;
    }

    private void HandleActivated(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        desktopIntegration?.SetPanelEngaged(true);
    }

    private void RefreshDesktopIntegrationAfterDpiChange()
    {
        desktopIntegration?.Refresh();
    }

    private void HandleDeactivated(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        desktopIntegration?.SetPanelEngaged(false);
    }

    private void HandleClosed(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;

        windowSource?.RemoveHook(WindowProcedure);
        windowSource = null;
        desktopIntegration = null;
        Activated -= HandleActivated;
        Deactivated -= HandleDeactivated;
        Closed -= HandleClosed;
    }
}
