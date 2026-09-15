using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Presentation;

internal static class SettingsEditorSessionFactory
{
    internal static SettingsEditorSession Create(AppSettings currentSettings, ISettingsEditorSaveHandler saveHandler)
    {
        var viewModel = new SettingsEditorViewModel(currentSettings, saveHandler);
        var view = new SettingsEditorView(viewModel);

        return new SettingsEditorSession(view, viewModel.Completion, viewModel.Cancel,
                                         viewModel.SynchronizeCollapsedHeight);
    }
}
