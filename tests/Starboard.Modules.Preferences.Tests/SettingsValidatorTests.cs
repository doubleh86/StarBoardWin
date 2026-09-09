using Starboard.Modules.Preferences.Application;
using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Tests;

[TestClass]
public sealed class SettingsValidatorTests
{
    [TestMethod]
    [DataRow("Dark")]
    [DataRow("Light")]
    [DataRow("One Dark")]
    [DataRow("Tokyo Night")]
    public void GetThemeWithBuiltInNameReturnsCompleteAccessiblePalette(string name)
    {
        var theme = PreferencesModule.GetTheme(name);

        Assert.AreEqual(name, theme.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Canvas));
        Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Foreground));
        Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Muted));
        Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Accent));
        Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Cursor));
        Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Selection));
        Assert.AreEqual(16, theme.AnsiPalette.Count);
        Assert.IsTrue(ContrastRatio(theme.Foreground, theme.Canvas) >= 4.5);
        Assert.IsTrue(ContrastRatio(theme.Muted, theme.Canvas) >= 4.5);
        Assert.IsTrue(ContrastRatio(theme.Accent, theme.Canvas) >= 3.0);
        Assert.IsTrue(ContrastRatio(theme.AnsiPalette[1], theme.Canvas) >= 4.5);
    }

    [TestMethod]
    public void NormalizeWithMissingDocumentReturnsDefaults()
    {
        var result = SettingsValidator.Normalize(null);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual("Tokyo Night", result.Theme);
        Assert.AreEqual(200, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithSchemaOneDefaultHeightMigratesToEightLineHeight()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = 1,
            CollapsedHeightDip = 96,
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual(200, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithSchemaTwoDefaultHeightAddsTabStripHeight()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = 2,
            CollapsedHeightDip = 116,
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual(200, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithSchemaThreeDefaultHeightAddsThreeTerminalLines()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = 3,
            CollapsedHeightDip = 148,
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual(200, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithPreviousSchemaCustomHeightPreservesConfiguredHeight()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = 3,
            CollapsedHeightDip = 180,
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual(180, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithSchemaTwoCustomHeightPreservesConfiguredHeight()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = 2,
            CollapsedHeightDip = 164,
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual(164, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithCurrentSchemaMinimumHeightPreservesConfiguredHeight()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            CollapsedHeightDip = 96,
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(96, result.CollapsedHeightDip);
    }

    [TestMethod]
    public void NormalizeWithOutOfRangeValuesClampsAndRepairsValues()
    {
        var candidate = new AppSettings
        {
            SchemaVersion = 0,
            Theme = "Unknown",
            CollapsedHeightDip = 5000,
            FontFamily = " ",
            FontSize = 2,
            Opacity = 0.1,
            PreferredMonitor = " ",
            ExpandShortcut = " ",
        };

        var result = SettingsValidator.Normalize(candidate);

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.AreEqual("Tokyo Night", result.Theme);
        Assert.AreEqual(720, result.CollapsedHeightDip);
        Assert.AreEqual("Cascadia Mono", result.FontFamily);
        Assert.AreEqual(8, result.FontSize);
        Assert.AreEqual(0.72, result.Opacity);
        Assert.AreEqual("Taskbar", result.PreferredMonitor);
        Assert.AreEqual("Ctrl+Alt+E", result.ExpandShortcut);
        Assert.AreEqual("Ctrl+Alt+S", result.ActivationShortcut);
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var red = Convert.ToInt32(color[1..3], 16) / 255.0;
        var green = Convert.ToInt32(color[3..5], 16) / 255.0;
        var blue = Convert.ToInt32(color[5..7], 16) / 255.0;

        return (0.2126 * Linearize(red)) +
               (0.7152 * Linearize(green)) +
               (0.0722 * Linearize(blue));
    }

    private static double Linearize(double channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
