namespace Starboard.Modules.DesktopIntegration.Contracts;

public sealed record TaskbarSnapshot(TaskbarEdge Edge, PixelRect TaskbarBounds, PixelRect MonitorBounds,
                                     PixelRect WorkArea, bool IsAutoHideEnabled, uint Dpi);
