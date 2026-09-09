using Starboard.Modules.Preferences.Contracts;

namespace Starboard.Modules.Preferences.Application;

internal static class ThemeCatalog
{
    private static readonly Dictionary<string, ThemeDefinition> Themes =
        new Dictionary<string, ThemeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dark"] = Create("Dark", "#101216", "#E6E8EC", "#979DA8", "#62D6E8", "#E6E8EC", "#294B5A",
                              ["#16191F", "#E05D68", "#78C67A", "#E2C66D", "#6699E8", "#BE79DC", "#62D6E8", "#D7DAE0", "#666D79", "#F27983", "#91D792", "#F1D883", "#82ACF0", "#CC8CE5", "#7CE3F0", "#FFFFFF"]),
            ["Light"] = Create("Light", "#F3F1EB", "#202329", "#5F6570", "#006D77", "#202329", "#C7E4E5",
                               ["#23262D", "#B13A45", "#397844", "#8A6500", "#265F9E", "#7A4B9B", "#006D77", "#E5E2DA", "#6A707B", "#C9515B", "#4E9258", "#A77A00", "#3779BE", "#925DB3", "#158A93", "#FFFFFF"]),
            ["One Dark"] = Create("One Dark", "#282C34", "#ABB2BF", "#939BAA", "#56B6C2", "#528BFF", "#3E4451",
                                  ["#1E2127", "#F07178", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#ABB2BF", "#5C6370", "#E88388", "#A8D18B", "#F0CC86", "#72B9F4", "#D18AE5", "#66C3CD", "#FFFFFF"]),
            ["Tokyo Night"] = Create("Tokyo Night", "#1A1B26", "#C0CAF5", "#8B92B5", "#7DCFFF", "#C0CAF5", "#33467C",
                                     ["#15161E", "#F7768E", "#9ECE6A", "#E0AF68", "#7AA2F7", "#BB9AF7", "#7DCFFF", "#A9B1D6", "#414868", "#F7768E", "#9ECE6A", "#E0AF68", "#7AA2F7", "#BB9AF7", "#7DCFFF", "#C0CAF5"]),
        };

    internal static bool IsKnown(string? name)
    {
        return name is not null && Themes.ContainsKey(name);
    }

    internal static ThemeDefinition Get(string name)
    {
        if (Themes.TryGetValue(name, out var theme) == true)
        {
            return theme;
        }

        return Themes["Tokyo Night"];
    }

    private static ThemeDefinition Create(string name, string canvas, string foreground, string muted, string accent,
                                          string cursor, string selection, IReadOnlyList<string> ansiPalette)
    {
        return new ThemeDefinition(name, canvas, foreground, muted, accent, cursor, selection, ansiPalette);
    }
}
