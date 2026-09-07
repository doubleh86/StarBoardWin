namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// Per-user startup intent. The desktop adapter resolves and quotes the current executable path.
/// </summary>
public sealed record StartupSettings(bool StartWithWindows);
