using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Domain;

namespace Starboard.Modules.DesktopIntegration.Tests.Domain;

[TestClass]
public sealed class PanelWindowPolicyTests
{
    [TestMethod]
    public void DecideWithUserHiddenAfterFullscreenAndExplorerReconciliationKeepsUserHidden()
    {
        var state = CreateState().HideOnExplicitUserRequest();
        var duringFullscreen = PanelWindowPolicy.Decide(CreateInput(state) with
                                                        {
                                                            FullscreenState = PanelFullscreenState.FullscreenOnPanelMonitor,
                                                        });
        var afterExplorerRecovery = PanelWindowPolicy.Decide(CreateInput(state.ReconcileEnvironment()));

        Assert.AreEqual(PanelWindowPresentation.HiddenByUser, duringFullscreen.Presentation);
        Assert.AreEqual(PanelWindowPolicyPriority.UserHidden, duringFullscreen.AppliedPriority);
        Assert.AreEqual(PanelWindowPresentation.HiddenByUser, afterExplorerRecovery.Presentation);
        Assert.AreEqual(PanelWindowPolicyPriority.UserHidden, afterExplorerRecovery.AppliedPriority);
    }

    [TestMethod]
    public void DecideWithFullscreenOverExpandedActivePanelTemporarilySuppressesWithoutMoving()
    {
        var state = CreateState()
            .ToggleMode()
            .SetEngagement(PanelEngagement.Active);

        var result = PanelWindowPolicy.Decide(CreateInput(state) with
                                              {
                                                  FullscreenState = PanelFullscreenState.FullscreenOnPanelMonitor,
                                              });

        Assert.AreEqual(PanelWindowPolicyPriority.FullscreenSuppression, result.AppliedPriority);
        Assert.AreEqual(PanelWindowPresentation.TemporarilySuppressed, result.Presentation);
        Assert.AreEqual(PanelWindowGeometryAction.FreezeCurrent, result.GeometryAction);
        Assert.IsNull(result.TargetBounds);
    }

    [TestMethod]
    public void DecideWithCollapsedActivePanelAndConcealedTaskbarRestoresLastSafeFrame()
    {
        var safeBounds = new PixelRect(0, 840, 1920, 1040);
        var state = CreateState()
            .RememberSafeCollapsedBounds(safeBounds)
            .SetEngagement(PanelEngagement.Active);

        var result = PanelWindowPolicy.Decide(CreateInput(state) with
                                              {
                                                  TaskbarPresence = TaskbarPresence.Concealed,
                                              });

        Assert.AreEqual(PanelWindowPolicyPriority.EngagementHold, result.AppliedPriority);
        Assert.AreEqual(PanelWindowPresentation.Visible, result.Presentation);
        Assert.AreEqual(PanelWindowGeometryAction.RestoreCollapsed, result.GeometryAction);
        Assert.AreEqual(safeBounds, result.TargetBounds);
    }

    [TestMethod]
    public void DecideWithExpandedPanelAndConcealedTaskbarKeepsExpandedWorkArea()
    {
        var state = CreateState().ToggleMode();
        var monitorWorkArea = new PixelRect(0, 0, 1920, 1040);

        var result = PanelWindowPolicy.Decide(CreateInput(state) with
                                              {
                                                  TaskbarPresence = TaskbarPresence.Concealed,
                                              });

        Assert.AreEqual(PanelWindowPolicyPriority.ExpandedHold, result.AppliedPriority);
        Assert.AreEqual(PanelWindowPresentation.Visible, result.Presentation);
        Assert.AreEqual(PanelWindowGeometryAction.ApplyExpanded, result.GeometryAction);
        Assert.AreEqual(monitorWorkArea, result.TargetBounds);
    }

    [TestMethod]
    public void DecideWithIdleCollapsedPanelAndConcealedTaskbarTemporarilySuppressesPanel()
    {
        var result = PanelWindowPolicy.Decide(CreateInput(CreateState()) with
                                              {
                                                  TaskbarPresence = TaskbarPresence.Concealed,
                                              });

        Assert.AreEqual(PanelWindowPolicyPriority.TaskbarSuppression, result.AppliedPriority);
        Assert.AreEqual(PanelWindowPresentation.TemporarilySuppressed, result.Presentation);
        Assert.AreEqual(PanelWindowGeometryAction.Preserve, result.GeometryAction);
    }

    [TestMethod]
    public void DecideWithUnknownTaskbarAndSafeCollapsedFrameKeepsFrameVisible()
    {
        var safeBounds = new PixelRect(-1920, 840, 0, 1040);
        var state = CreateState().RememberSafeCollapsedBounds(safeBounds);

        var result = PanelWindowPolicy.Decide(CreateInput(state) with
                                              {
                                                  TaskbarPresence = TaskbarPresence.Unknown,
                                              });

        Assert.AreEqual(PanelWindowPolicyPriority.Normal, result.AppliedPriority);
        Assert.AreEqual(PanelWindowPresentation.Visible, result.Presentation);
        Assert.AreEqual(PanelWindowGeometryAction.RestoreCollapsed, result.GeometryAction);
        Assert.AreEqual(safeBounds, result.TargetBounds);
    }

    [TestMethod]
    public void HideOnExplicitUserRequestIsOnlyTransitionThatHidesUserVisibility()
    {
        var state = CreateState();
        var engaged = state.SetEngagement(PanelEngagement.Active);
        var expanded = engaged.ToggleMode();
        var reconciled = expanded.ReconcileEnvironment();

        Assert.AreEqual(PanelUserVisibility.Visible, reconciled.UserVisibility);

        var hidden = reconciled.HideOnExplicitUserRequest();

        Assert.AreEqual(PanelUserVisibility.Hidden, hidden.UserVisibility);
        Assert.AreEqual(PanelUserVisibility.Visible, hidden.ShowOnExplicitUserRequest().UserVisibility);
    }

    private static PanelWindowState CreateState()
    {
        return new PanelWindowState(PanelUserVisibility.Visible, PanelMode.Collapsed, PanelEngagement.Idle, null);
    }

    private static PanelWindowPolicyInput CreateInput(PanelWindowState state)
    {
        return new PanelWindowPolicyInput(state.UserVisibility, PanelFullscreenState.Normal, state.Mode,
                                          state.Engagement, TaskbarPresence.Visible, DisplayTrackingState.Tracked,
                                          new MonitorSnapshot(new nint(1), new PixelRect(0, 0, 1920, 1080),
                                                              new PixelRect(0, 0, 1920, 1040), new DisplayDpi(96, 96)),
                                          state.LastSafeCollapsedBounds);
    }
}
