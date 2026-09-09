using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Starboard.Modules.DesktopIntegration;
using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Windows.Shell;

public partial class MainWindow : Window
{
    private DesktopIntegrationModule? desktopIntegration;
    private ShortcutGuideWindow? shortcutGuideWindow;
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

    internal void ApplyAppearance(AppSettings settings, ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);

        var canvasBrush = CreateBrush(theme.Canvas);
        var foregroundBrush = CreateBrush(theme.Foreground);
        var ruleBrush = CreateBrush(theme.Muted);
        Resources["CanvasBrush"] = canvasBrush;
        Resources["ForegroundBrush"] = foregroundBrush;
        Resources["RuleBrush"] = ruleBrush;
        Resources["AccentBrush"] = CreateBrush(theme.Accent);
        Background = canvasBrush;
        Foreground = foregroundBrush;
        PanelBorder.BorderBrush = ruleBrush;
        Height = settings.CollapsedHeightDip;
        Opacity = settings.Opacity;
    }

    internal nint AttachDesktopIntegration(DesktopIntegrationModule module)
    {
        desktopIntegration = module;
        desktopIntegration.ShortcutGuideRequested += HandleShortcutGuideRequested;
        var helper = new WindowInteropHelper(this);
        windowSource = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("The Starboard window source is unavailable.");
        windowSource.AddHook(WindowProcedure);

        return helper.Handle;
    }

    private nint WindowProcedure(nint windowHandle, int message, nint wordParameter, nint longParameter,
                                 ref bool handled)
    {
        var module = desktopIntegration;
        if (module is null)
        {
            return 0;
        }

        var windowMessage = new WindowMessage(windowHandle, message, unchecked((nuint)wordParameter), longParameter);
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
        if (desktopIntegration is not null)
        {
            desktopIntegration.ShortcutGuideRequested -= HandleShortcutGuideRequested;
        }

        shortcutGuideWindow?.Close();
        shortcutGuideWindow = null;
        desktopIntegration = null;
        Activated -= HandleActivated;
        Deactivated -= HandleDeactivated;
        Closed -= HandleClosed;
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var converted = System.Windows.Media.ColorConverter.ConvertFromString(color);
        if (converted is not System.Windows.Media.Color parsedColor)
        {
            throw new ArgumentException("The theme contains an invalid color.", nameof(color));
        }

        var brush = new SolidColorBrush(parsedColor);
        brush.Freeze();

        return brush;
    }

    private void HandleShortcutGuideRequested(object? sender, ShortcutGuideRequestEventArgs eventArguments)
    {
        _ = sender;
        if (Dispatcher.CheckAccess() == false)
        {
            _ = Dispatcher.BeginInvoke(() => ShowShortcutGuide(eventArguments.RegistrationSnapshot));
            return;
        }

        ShowShortcutGuide(eventArguments.RegistrationSnapshot);
    }

    private void ShowShortcutGuide(GlobalShortcutRegistrationSnapshot registrationSnapshot)
    {
        var guideWindow = shortcutGuideWindow;
        if (guideWindow is null)
        {
            guideWindow = new ShortcutGuideWindow(registrationSnapshot);
            guideWindow.Closed += HandleShortcutGuideClosed;
            shortcutGuideWindow = guideWindow;
            guideWindow.Show();
        }
        else
        {
            guideWindow.UpdateRegistrationSnapshot(registrationSnapshot);
        }

        if (guideWindow.WindowState == WindowState.Minimized)
        {
            guideWindow.WindowState = WindowState.Normal;
        }

        _ = guideWindow.Activate();
    }

    private void HandleShortcutGuideClosed(object? sender, EventArgs eventArguments)
    {
        _ = eventArguments;
        if (ReferenceEquals(sender, shortcutGuideWindow) == false)
        {
            return;
        }

        shortcutGuideWindow!.Closed -= HandleShortcutGuideClosed;
        shortcutGuideWindow = null;
    }
}
