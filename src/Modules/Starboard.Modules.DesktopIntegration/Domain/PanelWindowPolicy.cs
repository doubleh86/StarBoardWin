using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Domain;

internal static class PanelWindowPolicy
{
    internal static PanelWindowPolicyDecision Decide(PanelWindowPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var priority = PanelWindowPolicyPrecedence.Select(input);

        return priority switch
        {
            PanelWindowPolicyPriority.UserHidden => CreateDecision(
                priority,
                PanelWindowPresentation.HiddenByUser,
                PanelWindowGeometryAction.Preserve,
                null),
            PanelWindowPolicyPriority.FullscreenSuppression => CreateDecision(
                priority,
                PanelWindowPresentation.TemporarilySuppressed,
                PanelWindowGeometryAction.FreezeCurrent,
                null),
            PanelWindowPolicyPriority.ExpandedHold => CreateDecision(
                priority,
                PanelWindowPresentation.Visible,
                PanelWindowGeometryAction.ApplyExpanded,
                input.Monitor.WorkArea),
            PanelWindowPolicyPriority.EngagementHold => CreateLastSafeFrameDecision(
                priority,
                input.LastSafeCollapsedBounds),
            PanelWindowPolicyPriority.TaskbarSuppression => CreateDecision(
                priority,
                PanelWindowPresentation.TemporarilySuppressed,
                PanelWindowGeometryAction.Preserve,
                null),
            _ => CreateNormalDecision(input),
        };
    }

    private static PanelWindowPolicyDecision CreateLastSafeFrameDecision(
        PanelWindowPolicyPriority priority,
        PixelRect? lastSafeCollapsedBounds)
    {
        if (lastSafeCollapsedBounds is not PixelRect bounds)
        {
            return CreateDecision(
                priority,
                PanelWindowPresentation.Visible,
                PanelWindowGeometryAction.FreezeCurrent,
                null);
        }

        return CreateDecision(
            priority,
            PanelWindowPresentation.Visible,
            PanelWindowGeometryAction.RestoreCollapsed,
            bounds);
    }

    private static PanelWindowPolicyDecision CreateNormalDecision(PanelWindowPolicyInput input)
    {
        if (input.TaskbarPresence == TaskbarPresence.Unknown)
        {
            return CreateLastSafeFrameDecision(
                PanelWindowPolicyPriority.Normal,
                input.LastSafeCollapsedBounds);
        }

        return CreateDecision(
            PanelWindowPolicyPriority.Normal,
            PanelWindowPresentation.Visible,
            PanelWindowGeometryAction.ApplyCollapsed,
            null);
    }

    private static PanelWindowPolicyDecision CreateDecision(
        PanelWindowPolicyPriority priority,
        PanelWindowPresentation presentation,
        PanelWindowGeometryAction geometryAction,
        PixelRect? targetBounds)
    {
        return new PanelWindowPolicyDecision(
            priority,
            presentation,
            geometryAction,
            targetBounds,
            PanelWindowActivation.PreserveForeground,
            PanelWindowZOrder.PreserveNormal);
    }
}
