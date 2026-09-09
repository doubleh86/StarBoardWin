using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Domain;

/// <summary>
/// Stores panel state that must survive environmental reconciliation. Taskbar,
/// display, and fullscreen observations are deliberately not persisted here.
/// </summary>
internal sealed record PanelWindowState(PanelUserVisibility UserVisibility, PanelMode Mode, PanelEngagement Engagement,
                                        PixelRect? LastSafeCollapsedBounds)
{
    internal PanelWindowState ShowOnExplicitUserRequest()
    {
        return this with
        {
            UserVisibility = PanelUserVisibility.Visible,
        };
    }

    internal PanelWindowState HideOnExplicitUserRequest()
    {
        return this with
        {
            UserVisibility = PanelUserVisibility.Hidden,
        };
    }

    internal PanelWindowState ToggleMode()
    {
        var nextMode = Mode == PanelMode.Collapsed
            ? PanelMode.Expanded
            : PanelMode.Collapsed;

        return this with
        {
            Mode = nextMode,
        };
    }

    internal PanelWindowState SetEngagement(PanelEngagement engagement)
    {
        return this with
        {
            Engagement = engagement,
        };
    }

    internal PanelWindowState RememberSafeCollapsedBounds(PixelRect bounds)
    {
        if (bounds.IsEmpty == true)
        {
            throw new ArgumentException("A safe collapsed frame must have a non-zero size.", nameof(bounds));
        }

        return this with
        {
            LastSafeCollapsedBounds = bounds,
        };
    }

    internal PanelWindowState ReconcileEnvironment()
    {
        return this;
    }
}
