namespace Starboard.Modules.Terminal.Contracts;

public sealed record TerminalOptions(string? ShellExecutable, string FontFamily, double FontSize, TerminalTheme Theme);
