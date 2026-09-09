namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Appearance that can be replaced across all tabs without recreating shell sessions.
/// </summary>
public sealed record TerminalAppearanceSettings(string FontFamily, double FontSize, TerminalTheme Theme);
