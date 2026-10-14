namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>Fresh physical bounds and effective DPI for the attached panel HWND.</summary>
public readonly record struct PanelClientLayout(PixelRect WindowBounds, int ClientWidthPixel, int ClientHeightPixel,
                                                DisplayDpi WindowDpi)
{
    public bool TryGetSizeDip(double wpfScaleX, double wpfScaleY, out double widthDip, out double heightDip)
    {
        widthDip = 0;
        heightDip = 0;
        if (ClientWidthPixel <= 0 || ClientHeightPixel <= 0 || WindowDpi.X == 0 || WindowDpi.Y == 0 ||
            double.IsFinite(wpfScaleX) == false || double.IsFinite(wpfScaleY) == false ||
            Math.Abs(wpfScaleX - WindowDpi.ScaleX) > 0.001 || Math.Abs(wpfScaleY - WindowDpi.ScaleY) > 0.001)
        {
            return false;
        }

        widthDip = ClientWidthPixel / WindowDpi.ScaleX;
        heightDip = ClientHeightPixel / WindowDpi.ScaleY;

        return true;
    }
}
