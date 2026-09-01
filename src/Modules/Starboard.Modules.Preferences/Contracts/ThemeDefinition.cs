namespace Starboard.Modules.Preferences.Contracts;

public sealed record ThemeDefinition(
    string Name,
    string Canvas,
    string Foreground,
    string Muted,
    string Accent,
    string Cursor,
    string Selection,
    IReadOnlyList<string> AnsiPalette);
