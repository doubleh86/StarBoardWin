using System.IO;
using Starboard.Modules.Preferences.Application;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Preferences.Infrastructure;
using Starboard.Modules.Preferences.Presentation;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Preferences;

public sealed class PreferencesModule
{
    private readonly JsonSettingsStore settingsStore;

    public PreferencesModule(IDiagnosticLog diagnosticLog, string? settingsPath = null)
    {
        var resolvedPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                        "Starboard", "settings.json");

        settingsStore = new JsonSettingsStore(diagnosticLog, resolvedPath);
    }

    public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        return settingsStore.LoadAsync(cancellationToken);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        return settingsStore.SaveAsync(settings, cancellationToken);
    }

    public static SettingsEditorSession CreateSettingsEditor(AppSettings currentSettings,
                                                             ISettingsEditorSaveHandler saveHandler)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(saveHandler);

        return SettingsEditorSessionFactory.Create(SettingsValidator.Normalize(currentSettings), saveHandler);
    }

    public static ThemeDefinition GetTheme(string name)
    {
        return ThemeCatalog.Get(name);
    }
}
