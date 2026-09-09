using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalAppearanceStateTests
{
    [TestMethod]
    public void UpdateBeforeRendererReadyPreservesOnlyLatestSnapshotForInitialization()
    {
        var initial = CreateAppearance("Initial", 12, "#111111");
        var firstUpdate = CreateAppearance("First", 14, "#222222");
        var latestUpdate = CreateAppearance("Latest", 16, "#333333");
        var state = new TerminalAppearanceState(initial);

        var firstDelivery = state.Update(firstUpdate);
        var latestDelivery = state.Update(latestUpdate);
        var readyDelivery = state.MarkRendererReady();

        Assert.IsNull(firstDelivery);
        Assert.IsNull(latestDelivery);
        Assert.AreSame(latestUpdate, readyDelivery);
        Assert.AreSame(latestUpdate, state.Current);
    }

    [TestMethod]
    public void RendererReconnectDeliversLatestAppearanceWithoutLosingLiveUpdates()
    {
        var initial = CreateAppearance("Initial", 12, "#111111");
        var liveUpdate = CreateAppearance("Live", 14, "#222222");
        var reconnectUpdate = CreateAppearance("Reconnect", 16, "#333333");
        var state = new TerminalAppearanceState(initial);
        _ = state.MarkRendererReady();

        var liveDelivery = state.Update(liveUpdate);
        state.MarkRendererUnavailable();
        var unavailableDelivery = state.Update(reconnectUpdate);
        var reconnectDelivery = state.MarkRendererReady();

        Assert.AreSame(liveUpdate, liveDelivery);
        Assert.IsNull(unavailableDelivery);
        Assert.AreSame(reconnectUpdate, reconnectDelivery);
    }

    private static TerminalAppearanceSettings CreateAppearance(string fontFamily, double fontSize, string canvas)
    {
        var palette = Enumerable.Repeat("#808080", 16).ToArray();
        var theme = new TerminalTheme(canvas, "#eeeeee", "#999999", "#00aaff", "#eeeeee", "#444444", palette);

        return new TerminalAppearanceSettings(fontFamily, fontSize, theme);
    }
}
