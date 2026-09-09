using System.Text.Json;
using Starboard.Modules.Preferences.Application;
using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Tests.Contracts;

[TestClass]
public sealed class AppSettingsContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void DefaultsForNewSettingsProvideCompleteSchemaSixSnapshot()
    {
        var settings = new AppSettings();

        Assert.AreEqual(6, settings.SchemaVersion);
        Assert.IsNull(settings.ShellExecutable);
        Assert.AreEqual("Tokyo Night", settings.Theme);
        Assert.AreEqual(200, settings.CollapsedHeightDip);
        Assert.AreEqual("Cascadia Mono", settings.FontFamily);
        Assert.AreEqual(13, settings.FontSize);
        Assert.AreEqual(0.97, settings.Opacity);
        Assert.IsFalse(settings.StartWithWindows);
        Assert.AreEqual("Taskbar", settings.PreferredMonitor);
        Assert.AreEqual("Ctrl+Alt+E", settings.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", settings.ActivationShortcut);
        Assert.IsFalse(settings.RestoreWorkspaceOnLaunch);
    }

    [TestMethod]
    public void NormalizePartialJsonUsesCurrentDefaultsForMissingProperties()
    {
        const string Json = """
            {
              "theme": "Dark",
              "fontSize": 16
            }
            """;
        var candidate = JsonSerializer.Deserialize<AppSettings>(Json, SerializerOptions);

        var settings = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(6, settings.SchemaVersion);
        Assert.AreEqual("Dark", settings.Theme);
        Assert.AreEqual(16, settings.FontSize);
        Assert.AreEqual(200, settings.CollapsedHeightDip);
        Assert.AreEqual("Cascadia Mono", settings.FontFamily);
        Assert.AreEqual("Ctrl+Alt+E", settings.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", settings.ActivationShortcut);
        Assert.IsFalse(settings.RestoreWorkspaceOnLaunch);
    }

    [TestMethod]
    public void NormalizeSchemaFourJsonAddsActivationShortcutAndPreservesValues()
    {
        const string Json = """
            {
              "schemaVersion": 4,
              "shellExecutable": "pwsh.exe",
              "theme": "One Dark",
              "collapsedHeightDip": 240,
              "fontFamily": "Consolas",
              "fontSize": 15,
              "opacity": 0.9,
              "startWithWindows": true,
              "preferredMonitor": "Taskbar",
              "expandShortcut": "Ctrl+Shift+E"
            }
            """;
        var candidate = JsonSerializer.Deserialize<AppSettings>(Json, SerializerOptions);

        var settings = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(6, settings.SchemaVersion);
        Assert.AreEqual("pwsh.exe", settings.ShellExecutable);
        Assert.AreEqual("One Dark", settings.Theme);
        Assert.AreEqual(240, settings.CollapsedHeightDip);
        Assert.AreEqual("Consolas", settings.FontFamily);
        Assert.AreEqual(15, settings.FontSize);
        Assert.AreEqual(0.9, settings.Opacity);
        Assert.IsTrue(settings.StartWithWindows);
        Assert.AreEqual("Taskbar", settings.PreferredMonitor);
        Assert.AreEqual("Ctrl+Shift+E", settings.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", settings.ActivationShortcut);
        Assert.IsFalse(settings.RestoreWorkspaceOnLaunch);
    }

    [TestMethod]
    public void NormalizeSchemaFiveJsonDisablesWorkspaceRestoreUntilUserEnablesIt()
    {
        const string Json = """
            {
              "schemaVersion": 5,
              "theme": "Dark"
            }
            """;
        var candidate = JsonSerializer.Deserialize<AppSettings>(Json, SerializerOptions);

        var settings = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(6, settings.SchemaVersion);
        Assert.AreEqual("Dark", settings.Theme);
        Assert.IsFalse(settings.RestoreWorkspaceOnLaunch);
    }
}
