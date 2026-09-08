using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Domain;

internal readonly record struct DipRect(
    double Left,
    double Top,
    double Right,
    double Bottom);

internal static class PanelGeometryCalculator
{
    internal static PixelRect CalculateCollapsed(
        TaskbarSnapshot snapshot,
        double requestedHeightDip)
    {
        return CalculateCollapsed(
            snapshot,
            new DisplayDpi(snapshot.Dpi, snapshot.Dpi),
            requestedHeightDip);
    }

    internal static PixelRect CalculateCollapsed(
        TaskbarSnapshot snapshot,
        DisplayDpi dpi,
        double requestedHeightDip)
    {
        var thicknessDpi = snapshot.Edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? dpi.X
            : dpi.Y;
        var requestedThickness = DipToPixels(requestedHeightDip, thicknessDpi);
        var innerEdge = GetTaskbarInnerEdge(snapshot);
        var maximumThickness = snapshot.Edge switch
        {
            TaskbarEdge.Left => snapshot.WorkArea.Right - innerEdge,
            TaskbarEdge.Top => snapshot.WorkArea.Bottom - innerEdge,
            TaskbarEdge.Right => innerEdge - snapshot.WorkArea.Left,
            _ => innerEdge - snapshot.WorkArea.Top,
        };
        if (maximumThickness <= 0)
        {
            return snapshot.WorkArea;
        }

        var thickness = Math.Clamp(requestedThickness, 1, maximumThickness);

        return snapshot.Edge switch
        {
            TaskbarEdge.Left => new PixelRect(
                innerEdge,
                snapshot.WorkArea.Top,
                innerEdge + thickness,
                snapshot.WorkArea.Bottom),
            TaskbarEdge.Top => new PixelRect(
                snapshot.WorkArea.Left,
                innerEdge,
                snapshot.WorkArea.Right,
                innerEdge + thickness),
            TaskbarEdge.Right => new PixelRect(
                innerEdge - thickness,
                snapshot.WorkArea.Top,
                innerEdge,
                snapshot.WorkArea.Bottom),
            _ => new PixelRect(
                snapshot.WorkArea.Left,
                innerEdge - thickness,
                snapshot.WorkArea.Right,
                innerEdge),
        };
    }

    internal static PixelRect CalculateExpanded(TaskbarSnapshot snapshot)
    {
        return snapshot.WorkArea;
    }

    internal static int DipToPixels(double dip, uint dpi)
    {
        var effectiveDpi = dpi == 0 ? DisplayDpi.Default : dpi;
        return (int)Math.Round(
            dip * effectiveDpi / DisplayDpi.Default,
            MidpointRounding.AwayFromZero);
    }

    internal static double PixelsToDip(int pixels, uint dpi)
    {
        var effectiveDpi = dpi == 0 ? DisplayDpi.Default : dpi;
        return pixels * (double)DisplayDpi.Default / effectiveDpi;
    }

    internal static DipRect PixelsToDip(PixelRect rectangle, DisplayDpi dpi)
    {
        return new DipRect(
            PixelsToDip(rectangle.Left, dpi.X),
            PixelsToDip(rectangle.Top, dpi.Y),
            PixelsToDip(rectangle.Right, dpi.X),
            PixelsToDip(rectangle.Bottom, dpi.Y));
    }

    private static int GetTaskbarInnerEdge(TaskbarSnapshot snapshot)
    {
        if (snapshot.TaskbarBounds.IsEmpty == true)
        {
            return snapshot.Edge switch
            {
                TaskbarEdge.Left => snapshot.WorkArea.Left,
                TaskbarEdge.Top => snapshot.WorkArea.Top,
                TaskbarEdge.Right => snapshot.WorkArea.Right,
                _ => snapshot.WorkArea.Bottom,
            };
        }

        return snapshot.Edge switch
        {
            TaskbarEdge.Left => Math.Clamp(
                snapshot.TaskbarBounds.Right,
                snapshot.WorkArea.Left,
                snapshot.WorkArea.Right),
            TaskbarEdge.Top => Math.Clamp(
                snapshot.TaskbarBounds.Bottom,
                snapshot.WorkArea.Top,
                snapshot.WorkArea.Bottom),
            TaskbarEdge.Right => Math.Clamp(
                snapshot.TaskbarBounds.Left,
                snapshot.WorkArea.Left,
                snapshot.WorkArea.Right),
            _ => Math.Clamp(
                snapshot.TaskbarBounds.Top,
                snapshot.WorkArea.Top,
                snapshot.WorkArea.Bottom),
        };
    }
}
