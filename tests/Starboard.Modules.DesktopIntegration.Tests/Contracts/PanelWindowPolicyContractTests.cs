using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Tests.Contracts;

[TestClass]
public sealed class PanelWindowPolicyContractTests
{
    [TestMethod]
    public void SelectWithUserHiddenDuringFullscreenAndExpandedReturnsUserHidden()
    {
        var input = CreateInput() with
        {
            UserVisibility = PanelUserVisibility.Hidden,
            FullscreenState = PanelFullscreenState.FullscreenOnPanelMonitor,
            Mode = PanelMode.Expanded,
        };

        var result = PanelWindowPolicyPrecedence.Select(input);

        Assert.AreEqual(PanelWindowPolicyPriority.UserHidden, result);
    }

    [TestMethod]
    public void SelectWithFullscreenOnPanelMonitorWhileExpandedReturnsFullscreenSuppression()
    {
        var input = CreateInput() with
        {
            FullscreenState = PanelFullscreenState.FullscreenOnPanelMonitor,
            Mode = PanelMode.Expanded,
        };

        var result = PanelWindowPolicyPrecedence.Select(input);

        Assert.AreEqual(PanelWindowPolicyPriority.FullscreenSuppression, result);
    }

    [TestMethod]
    public void SelectWithExpandedAndConcealedTaskbarReturnsExpandedHold()
    {
        var input = CreateInput() with
        {
            Mode = PanelMode.Expanded,
            TaskbarPresence = TaskbarPresence.Concealed,
        };

        var result = PanelWindowPolicyPrecedence.Select(input);

        Assert.AreEqual(PanelWindowPolicyPriority.ExpandedHold, result);
    }

    [TestMethod]
    public void SelectWithActiveAndConcealedTaskbarReturnsEngagementHold()
    {
        var input = CreateInput() with
        {
            Engagement = PanelEngagement.Active,
            TaskbarPresence = TaskbarPresence.Concealed,
        };

        var result = PanelWindowPolicyPrecedence.Select(input);

        Assert.AreEqual(PanelWindowPolicyPriority.EngagementHold, result);
    }

    [TestMethod]
    public void SelectWithIdleCollapsedAndConcealedTaskbarReturnsTaskbarSuppression()
    {
        var input = CreateInput() with
        {
            TaskbarPresence = TaskbarPresence.Concealed,
        };

        var result = PanelWindowPolicyPrecedence.Select(input);

        Assert.AreEqual(PanelWindowPolicyPriority.TaskbarSuppression, result);
    }

    [TestMethod]
    public void DecisionKeepsUserHiddenAndTemporarySuppressionDistinct()
    {
        var hidden = CreateDecision(PanelWindowPresentation.HiddenByUser);
        var suppressed = CreateDecision(PanelWindowPresentation.TemporarilySuppressed);

        Assert.AreNotEqual(hidden, suppressed);
        Assert.AreEqual(PanelWindowPresentation.HiddenByUser, hidden.Presentation);
        Assert.AreEqual(PanelWindowPresentation.TemporarilySuppressed, suppressed.Presentation);
    }

    private static PanelWindowPolicyInput CreateInput()
    {
        return new PanelWindowPolicyInput(PanelUserVisibility.Visible, PanelFullscreenState.Normal, PanelMode.Collapsed,
                                          PanelEngagement.Idle, TaskbarPresence.Visible, DisplayTrackingState.Tracked,
                                          new MonitorSnapshot(new nint(7), new PixelRect(0, 0, 1920, 1080),
                                                              new PixelRect(0, 0, 1920, 1040), new DisplayDpi(96, 96)),
                                          new PixelRect(0, 840, 1920, 1040));
    }

    private static PanelWindowPolicyDecision CreateDecision(PanelWindowPresentation presentation)
    {
        return new PanelWindowPolicyDecision(PanelWindowPolicyPriority.UserHidden, presentation,
                                             PanelWindowGeometryAction.Preserve, null,
                                             PanelWindowActivation.PreserveForeground, PanelWindowZOrder.PreserveNormal);
    }
}
