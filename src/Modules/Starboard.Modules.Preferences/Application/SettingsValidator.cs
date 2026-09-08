using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Application;

internal static class SettingsValidator
{
    internal static AppSettings Normalize(AppSettings? candidate)
    {
        if (candidate is null)
        {
            return new AppSettings();
        }

        var theme = ThemeCatalog.IsKnown(candidate.Theme) == true
            ? candidate.Theme
            : "Tokyo Night";
        var collapsedHeightDip = candidate.CollapsedHeightDip;
        if (candidate.SchemaVersion < 2 && collapsedHeightDip == 96)
        {
            collapsedHeightDip = 116;
        }

        if (candidate.SchemaVersion < 3 && collapsedHeightDip == 116)
        {
            collapsedHeightDip = 148;
        }

        if (candidate.SchemaVersion < 4 && collapsedHeightDip == 148)
        {
            collapsedHeightDip = 200;
        }

        return candidate with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            Theme = theme,
            CollapsedHeightDip = Math.Clamp(collapsedHeightDip, 96, 720),
            FontFamily = string.IsNullOrWhiteSpace(candidate.FontFamily) == true
                ? "Cascadia Mono"
                : candidate.FontFamily.Trim(),
            FontSize = Math.Clamp(candidate.FontSize, 8, 32),
            Opacity = Math.Clamp(candidate.Opacity, 0.72, 1),
            PreferredMonitor = string.IsNullOrWhiteSpace(candidate.PreferredMonitor) == true
                ? "Taskbar"
                : candidate.PreferredMonitor.Trim(),
            ExpandShortcut = string.IsNullOrWhiteSpace(candidate.ExpandShortcut) == true
                ? "Ctrl+Alt+E"
                : candidate.ExpandShortcut.Trim(),
            ActivationShortcut = string.IsNullOrWhiteSpace(candidate.ActivationShortcut) == true
                ? "Ctrl+Alt+S"
                : candidate.ActivationShortcut.Trim(),
        };
    }
}
