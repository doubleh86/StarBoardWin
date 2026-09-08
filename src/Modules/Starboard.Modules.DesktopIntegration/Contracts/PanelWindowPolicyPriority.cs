namespace Starboard.Modules.DesktopIntegration.Contracts;

public enum PanelWindowPolicyPriority
{
    UserHidden,
    FullscreenSuppression,
    ExpandedHold,
    EngagementHold,
    TaskbarSuppression,
    Normal,
}

public static class PanelWindowPolicyPrecedence
{
    /// <summary>
    /// Selects the first applicable rule in product priority order. Geometry and
    /// presentation actions remain the responsibility of the window policy reducer.
    /// </summary>
    public static PanelWindowPolicyPriority Select(PanelWindowPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.UserVisibility == PanelUserVisibility.Hidden)
        {
            return PanelWindowPolicyPriority.UserHidden;
        }

        if (input.FullscreenState == PanelFullscreenState.FullscreenOnPanelMonitor)
        {
            return PanelWindowPolicyPriority.FullscreenSuppression;
        }

        if (input.Mode == PanelMode.Expanded)
        {
            return PanelWindowPolicyPriority.ExpandedHold;
        }

        if (input.Engagement == PanelEngagement.Active)
        {
            return PanelWindowPolicyPriority.EngagementHold;
        }

        if (input.TaskbarPresence == TaskbarPresence.Concealed)
        {
            return PanelWindowPolicyPriority.TaskbarSuppression;
        }

        return PanelWindowPolicyPriority.Normal;
    }
}
