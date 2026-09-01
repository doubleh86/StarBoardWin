namespace Starboard.Modules.Terminal.Contracts;

public sealed record TerminalTheme(
    string Canvas,
    string Foreground,
    string Muted,
    string Accent,
    string Cursor,
    string Selection,
    IReadOnlyList<string> AnsiPalette);
