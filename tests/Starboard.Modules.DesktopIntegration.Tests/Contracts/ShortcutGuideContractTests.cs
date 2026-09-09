using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Tests.TestDoubles;

namespace Starboard.Modules.DesktopIntegration.Tests.Contracts;

[TestClass]
public sealed class ShortcutGuideContractTests
{
    [TestMethod]
    public void RegistrationSnapshotSeparatesConfiguredGestureFromEffectiveRegistration()
    {
        var snapshot = new GlobalShortcutRegistrationSnapshot(
            new GlobalShortcutRegistrationState("Ctrl+Alt+E", "Ctrl+Alt+E",
                                                GlobalShortcutRegistrationStatus.Registered),
            new GlobalShortcutRegistrationState("Ctrl+Alt+S", null,
                                                GlobalShortcutRegistrationStatus.NotRegistered,
                                                "The shortcut is already in use."));

        Assert.AreEqual("Ctrl+Alt+E", snapshot.Expand.EffectiveGesture);
        Assert.AreEqual("Ctrl+Alt+S", snapshot.Activation.ConfiguredGesture);
        Assert.IsNull(snapshot.Activation.EffectiveGesture);
        Assert.AreEqual(GlobalShortcutRegistrationStatus.NotRegistered, snapshot.Activation.Status);
        Assert.AreEqual("The shortcut is already in use.", snapshot.Activation.FailureMessage);
    }

    [TestMethod]
    public void ShortcutGuideRequestCanBeCoordinatedByHostWithCurrentSnapshot()
    {
        var snapshot = new GlobalShortcutRegistrationSnapshot(
            new GlobalShortcutRegistrationState("Ctrl+Alt+E", null,
                                                GlobalShortcutRegistrationStatus.Unknown),
            new GlobalShortcutRegistrationState("Ctrl+Alt+S", "Ctrl+Alt+S",
                                                GlobalShortcutRegistrationStatus.Registered));
        var host = new ShortcutGuideContractTestDouble();

        host.HandleRequest(this, new ShortcutGuideRequestEventArgs(snapshot));

        Assert.AreEqual(1, host.RequestCount);
        Assert.AreSame(snapshot, host.LastSnapshot);
    }

    [TestMethod]
    public void RegistrationStateRejectsStatusThatContradictsEffectiveGesture()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new GlobalShortcutRegistrationState("Ctrl+Alt+E", null,
                                                GlobalShortcutRegistrationStatus.Registered));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new GlobalShortcutRegistrationState("Ctrl+Alt+E", "Ctrl+Alt+E",
                                                GlobalShortcutRegistrationStatus.NotRegistered));
    }
}
