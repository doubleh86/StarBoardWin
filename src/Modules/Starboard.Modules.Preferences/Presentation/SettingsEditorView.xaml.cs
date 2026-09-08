using System.Windows.Controls;

namespace Starboard.Modules.Preferences.Presentation;

internal partial class SettingsEditorView : UserControl
{
    private readonly SettingsEditorViewModel viewModel;

    internal SettingsEditorView(SettingsEditorViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void RestoreDefaultsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        viewModel.RestoreDefaults();
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        viewModel.Cancel();
    }

    private async void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await viewModel.SaveAsync(CancellationToken.None);
    }
}
