namespace Starboard.Modules.DesktopIntegration.Contracts;

public sealed record PanelWindowPolicyInput(PanelUserVisibility UserVisibility, PanelFullscreenState FullscreenState,
                                            PanelMode Mode, PanelEngagement Engagement, TaskbarPresence TaskbarPresence,
                                            DisplayTrackingState TrackingState, MonitorSnapshot Monitor,
                                            PixelRect? LastSafeCollapsedBounds);

public enum PanelUserVisibility
{
    Visible,

    /// <summary>
    /// A persistent user intent that only another explicit user request may clear.
    /// </summary>
    Hidden,
}

public enum PanelFullscreenState
{
    Normal,
    FullscreenOnPanelMonitor,
}

public enum PanelMode
{
    Collapsed,
    Expanded,
}

public enum PanelEngagement
{
    Idle,
    Active,
}

public enum TaskbarPresence
{
    Unknown,
    Visible,
    Concealed,
}

public enum DisplayTrackingState
{
    Tracked,
    Fallback,
}
