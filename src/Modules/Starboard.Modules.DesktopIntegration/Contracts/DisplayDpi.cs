namespace Starboard.Modules.DesktopIntegration.Contracts;

public readonly record struct DisplayDpi(uint X, uint Y)
{
    public const uint Default = 96;

    public double ScaleX => X / (double)Default;

    public double ScaleY => Y / (double)Default;
}
