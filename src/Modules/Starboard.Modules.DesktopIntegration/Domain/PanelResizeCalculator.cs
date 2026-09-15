using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Domain;

internal readonly record struct PanelResizeGeometry(double HeightDip, PixelRect Bounds);

internal static class PanelResizeCalculator
{
    internal const double MinimumHeightDip = 96;
    internal const double MaximumHeightDip = 720;
    internal const double DefaultHeightDip = 200;

    private const double _TopHitTargetDip = 6;

    internal static PanelResizeGeometry CalculateTopResize(TaskbarSnapshot snapshot, DisplayDpi dpi,
                                                           int proposedTopPixel)
    {
        var anchorBounds = PanelGeometryCalculator.CalculateCollapsed(snapshot, dpi, MinimumHeightDip);
        var proposedHeightPixels = anchorBounds.Bottom - proposedTopPixel;
        var proposedHeightDip = PanelGeometryCalculator.PixelsToDip(proposedHeightPixels, dpi.Y);

        return CalculateHeight(snapshot, dpi, proposedHeightDip);
    }

    internal static PanelResizeGeometry CalculateHeight(TaskbarSnapshot snapshot, DisplayDpi dpi,
                                                        double requestedHeightDip)
    {
        var anchorBounds = PanelGeometryCalculator.CalculateCollapsed(snapshot, dpi, MinimumHeightDip);
        var availableHeightPixels = Math.Max(0, anchorBounds.Bottom - snapshot.WorkArea.Top);
        var availableHeightDip = PanelGeometryCalculator.PixelsToDip(availableHeightPixels, dpi.Y);
        var maximumHeightDip = Math.Min(MaximumHeightDip, availableHeightDip);
        var clampedHeightDip = maximumHeightDip < MinimumHeightDip
            ? MinimumHeightDip
            : Math.Clamp(requestedHeightDip, MinimumHeightDip, maximumHeightDip);
        var bounds = PanelGeometryCalculator.CalculateCollapsed(snapshot, dpi, clampedHeightDip);

        return new PanelResizeGeometry(clampedHeightDip, bounds);
    }

    internal static bool IsTopResizeHit(PixelRect bounds, DisplayDpi dpi, int screenPixelX, int screenPixelY)
    {
        if (bounds.IsEmpty == true || screenPixelX < bounds.Left || screenPixelX >= bounds.Right)
        {
            return false;
        }

        var hitTargetPixels = Math.Max(1, PanelGeometryCalculator.DipToPixels(_TopHitTargetDip, dpi.Y));

        return screenPixelY >= bounds.Top && screenPixelY < Math.Min(bounds.Bottom, bounds.Top + hitTargetPixels);
    }
}
