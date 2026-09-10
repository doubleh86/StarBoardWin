using System.Windows;
using System.Windows.Input;
using Starboard.Modules.DesktopIntegration.Contracts;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Starboard.Windows.Shell;

internal partial class ShortcutGuideWindow : Window
{
    internal ShortcutGuideWindow(GlobalShortcutRegistrationSnapshot registrationSnapshot)
    {
        InitializeComponent();
        UpdateRegistrationSnapshot(registrationSnapshot);
    }

    internal void UpdateRegistrationSnapshot(GlobalShortcutRegistrationSnapshot registrationSnapshot)
    {
        ArgumentNullException.ThrowIfNull(registrationSnapshot);
        ActivationShortcutText.Text = FormatRegistration(registrationSnapshot.Activation);
        ExpandShortcutText.Text = FormatRegistration(registrationSnapshot.Expand);
    }

    private void HandleCloseClick(object sender, RoutedEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        Close();
    }

    private void HandlePreviewKeyDown(object sender, KeyEventArgs eventArguments)
    {
        _ = sender;
        if (eventArguments.Key != Key.Escape)
        {
            return;
        }

        // This modeless window cannot rely on a modal dialog's IsCancel behavior.
        eventArguments.Handled = true;
        Close();
    }

    private static string FormatRegistration(GlobalShortcutRegistrationState state)
    {
        return state.Status switch
        {
            GlobalShortcutRegistrationStatus.Registered =>
                $"{state.EffectiveGesture} (등록됨)",
            GlobalShortcutRegistrationStatus.NotRegistered =>
                $"{state.ConfiguredGesture} (등록 실패: {state.FailureMessage ?? "Windows 상태를 확인하세요."})",
            GlobalShortcutRegistrationStatus.Unknown =>
                $"{state.ConfiguredGesture} (등록 상태 미확인)",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }
}
