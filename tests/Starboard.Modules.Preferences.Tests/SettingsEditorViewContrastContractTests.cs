using Starboard.Modules.Preferences.Presentation;
using PreferencesContracts = Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Tests;

[TestClass]
public sealed class SettingsEditorViewContrastContractTests
{
    private static readonly string[] BuiltInThemes = ["Dark", "Light", "One Dark", "Tokyo Night"];

    [TestMethod]
    public void EditorDeclaresReadableForegroundForAllControlStates()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(
            "src/Modules/Starboard.Modules.Preferences/Presentation/SettingsEditorView.xaml"));

        StringAssert.Contains(xaml, "x:Key=\"DisabledForegroundBrush\"");
        StringAssert.Contains(xaml, "x:Key=\"InputErrorBrush\"");
        StringAssert.Contains(xaml, "x:Key=\"InputErrorBackgroundBrush\"");
        StringAssert.Contains(xaml, "TargetType=\"CheckBox\"");
        StringAssert.Contains(xaml, "TargetType=\"ComboBoxItem\"");
        StringAssert.Contains(xaml, "Property=\"Validation.HasError\"");
        StringAssert.Contains(xaml, "Property=\"IsHighlighted\"");
        StringAssert.Contains(xaml, "Content=\"명령 완료 알림\"");
        StringAssert.Contains(xaml, "CommandCompletionNotificationsEnabled");
        Assert.IsFalse(xaml.Contains("Foreground=\"Black\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EditorKeepsTheFourBuiltInThemeChoices()
    {
        var viewModel = new SettingsEditorViewModel(new PreferencesContracts.AppSettings(), new NoOpSaveHandler());

        CollectionAssert.AreEquivalent(BuiltInThemes,
                                       viewModel.Themes.ToArray());
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate) == true)
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not locate repository file '{relativePath}'.");
        return string.Empty;
    }

    private sealed class NoOpSaveHandler : PreferencesContracts.ISettingsEditorSaveHandler
    {
        public Task SaveAsync(PreferencesContracts.AppSettings settings, CancellationToken cancellationToken)
        {
            _ = settings;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
