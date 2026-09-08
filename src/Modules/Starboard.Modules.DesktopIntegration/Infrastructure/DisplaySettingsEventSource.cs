using Microsoft.Win32;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal interface IDisplaySettingsEventSource
{
    event EventHandler? Changed;
}

internal sealed class DisplaySettingsEventSource : IDisplaySettingsEventSource
{
    internal static DisplaySettingsEventSource Instance { get; } = new();

    private DisplaySettingsEventSource()
    {
    }

    public event EventHandler? Changed
    {
        add => SystemEvents.DisplaySettingsChanged += value;
        remove => SystemEvents.DisplaySettingsChanged -= value;
    }
}
