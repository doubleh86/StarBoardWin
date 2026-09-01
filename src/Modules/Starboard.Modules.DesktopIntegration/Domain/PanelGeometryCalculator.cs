using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Domain;

internal static class PanelGeometryCalculator
{
    private const double DefaultDpi = 96;

    internal static PixelRect CalculateCollapsed(
        TaskbarSnapshot snapshot,
        double requestedHeightDip)
    {
        var scale = Math.Max(DefaultDpi, snapshot.Dpi) / DefaultDpi;
        var requestedThickness = (int)Math.Round(
            requestedHeightDip * scale,
            MidpointRounding.AwayFromZero);
        var maximumThickness = snapshot.Edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? snapshot.WorkArea.Width
            : snapshot.WorkArea.Height;
        var thickness = Math.Clamp(requestedThickness, 1, Math.Max(1, maximumThickness));

        return snapshot.Edge switch
        {
            TaskbarEdge.Left => new PixelRect(
                snapshot.WorkArea.Left,
                snapshot.WorkArea.Top,
                snapshot.WorkArea.Left + thickness,
                snapshot.WorkArea.Bottom),
            TaskbarEdge.Top => new PixelRect(
                snapshot.WorkArea.Left,
                snapshot.WorkArea.Top,
                snapshot.WorkArea.Right,
                snapshot.WorkArea.Top + thickness),
            TaskbarEdge.Right => new PixelRect(
                snapshot.WorkArea.Right - thickness,
                snapshot.WorkArea.Top,
                snapshot.WorkArea.Right,
                snapshot.WorkArea.Bottom),
            _ => new PixelRect(
                snapshot.WorkArea.Left,
                snapshot.WorkArea.Bottom - thickness,
                snapshot.WorkArea.Right,
                snapshot.WorkArea.Bottom),
        };
    }

    internal static PixelRect CalculateExpanded(TaskbarSnapshot snapshot)
    {
        return snapshot.WorkArea;
    }
}
