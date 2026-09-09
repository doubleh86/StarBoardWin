namespace Starboard.Modules.DesktopIntegration.Contracts;

public sealed record MonitorSnapshot(nint Handle, PixelRect Bounds, PixelRect WorkArea, DisplayDpi Dpi);
