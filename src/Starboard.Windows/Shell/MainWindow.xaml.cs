using System.Windows;
using System.Windows.Interop;
using Starboard.Modules.DesktopIntegration;

namespace Starboard.Windows.Shell;

public partial class MainWindow : Window
{
    private DesktopIntegrationModule? desktopIntegration;

    internal MainWindow()
    {
        InitializeComponent();
    }

    internal void SetTerminalContent(UIElement content)
    {
        TerminalContent.Content = content;
    }

    internal nint AttachDesktopIntegration(DesktopIntegrationModule module)
    {
        desktopIntegration = module;
        var helper = new WindowInteropHelper(this);
        var source = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("The Starboard window source is unavailable.");
        source.AddHook(WindowProcedure);
        return helper.Handle;
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        _ = windowHandle;
        _ = longParameter;

        if (desktopIntegration?.HandleWindowMessage(message, wordParameter) == true)
        {
            handled = true;
        }

        return 0;
    }
}
