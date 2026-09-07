namespace Starboard.Modules.DesktopIntegration.Contracts;

public sealed record PanelWindowPolicyDecision(
    PanelWindowPolicyPriority AppliedPriority,
    PanelWindowPresentation Presentation,
    PanelWindowGeometryAction GeometryAction,
    PixelRect? TargetBounds,
    PanelWindowActivation Activation,
    PanelWindowZOrder ZOrder);

public enum PanelWindowPresentation
{
    Visible,

    /// <summary>
    /// Hidden by persistent user intent. Environment reconciliation must not clear it.
    /// </summary>
    HiddenByUser,

    /// <summary>
    /// Hidden while an environmental condition applies and eligible for policy
    /// reevaluation when that condition ends.
    /// </summary>
    TemporarilySuppressed,
}

public enum PanelWindowGeometryAction
{
    Preserve,
    FreezeCurrent,
    ApplyCollapsed,
    RestoreCollapsed,
    ApplyExpanded,
}

public enum PanelWindowActivation
{
    PreserveForeground,
    ActivateOnExplicitRequest,
}

public enum PanelWindowZOrder
{
    PreserveNormal,
    BringForwardInNormalBand,
}
