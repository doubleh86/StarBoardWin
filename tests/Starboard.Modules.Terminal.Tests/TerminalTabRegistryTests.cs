using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalTabRegistryTests
{
    [TestMethod]
    public void AddFirstAndSubsequentTabsAssignsUniqueIdentifiersAndPredictableNames()
    {
        var registry = CreateRegistry(3);

        var first = registry.Add();
        var second = registry.Add();
        var snapshot = registry.CreateSnapshot();

        Assert.AreEqual(new TerminalSessionId(CreateGuid(1)), first.SessionId);
        Assert.AreEqual("PowerShell 1", first.Name);
        Assert.AreEqual(new TerminalSessionId(CreateGuid(2)), second.SessionId);
        Assert.AreEqual("PowerShell 2", second.Name);
        Assert.AreEqual(second.SessionId, snapshot.ActiveSessionId);
    }

    [TestMethod]
    public void SelectNextAndPreviousAtBothEndsWrapsInTabOrder()
    {
        var registry = CreateRegistry(3);
        var first = registry.Add();
        var second = registry.Add();
        var third = registry.Add();

        Assert.IsTrue(registry.Select(first.SessionId));
        Assert.IsTrue(registry.SelectPrevious());
        Assert.AreEqual(third.SessionId, registry.ActiveSessionId);

        Assert.IsTrue(registry.SelectNext());
        Assert.AreEqual(first.SessionId, registry.ActiveSessionId);

        Assert.IsTrue(registry.Select(second.SessionId));
        Assert.AreEqual(second.SessionId, registry.ActiveSessionId);
    }

    [TestMethod]
    public void CloseActiveMiddleAndLastTabsSelectsRightThenLeftNeighbor()
    {
        var registry = CreateRegistry(4);
        _ = registry.Add();
        var second = registry.Add();
        var third = registry.Add();
        var fourth = registry.Add();

        Assert.IsTrue(registry.Select(second.SessionId));
        _ = registry.Close(second.SessionId);
        Assert.AreEqual(third.SessionId, registry.ActiveSessionId);

        Assert.IsTrue(registry.Select(fourth.SessionId));
        _ = registry.Close(fourth.SessionId);
        Assert.AreEqual(third.SessionId, registry.ActiveSessionId);
    }

    [TestMethod]
    public void CloseInactiveTabKeepsCurrentActiveTab()
    {
        var registry = CreateRegistry(3);
        var first = registry.Add();
        _ = registry.Add();
        var third = registry.Add();

        var result = registry.Close(first.SessionId);

        Assert.IsNotNull(result);
        Assert.AreEqual(third.SessionId, registry.ActiveSessionId);
    }

    [TestMethod]
    public void CloseLastTabCreatesAndActivatesNewDefaultTab()
    {
        var registry = CreateRegistry(2);
        var first = registry.Add();

        var result = registry.Close(first.SessionId);
        var snapshot = registry.CreateSnapshot();

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.ReplacementTab);
        Assert.AreEqual("PowerShell 2", result.ReplacementTab.Name);
        Assert.AreNotEqual(first.SessionId, result.ReplacementTab.SessionId);
        Assert.AreEqual(result.ReplacementTab.SessionId, snapshot.ActiveSessionId);
        Assert.AreEqual(1, snapshot.Tabs.Count);
    }

    [TestMethod]
    public void AddAtMaximumTabCountRejectsBeforeRequestingAnotherIdentifier()
    {
        var identifiersRequested = 0;
        var registry = new TerminalTabRegistry(() => new TerminalSessionId(CreateGuid(++identifiersRequested)), 2);
        _ = registry.Add();
        _ = registry.Add();

        Assert.ThrowsExactly<InvalidOperationException>(() => registry.Add());
        Assert.AreEqual(2, identifiersRequested);
    }

    [TestMethod]
    public void AddAssignsConfigurationIdentitySeparatelyFromRuntimeSessionIdentity()
    {
        var sessionId = new TerminalSessionId(CreateGuid(1));
        var configurationId = new TerminalTabConfigurationId(CreateGuid(2));
        var registry = new TerminalTabRegistry(() => sessionId, 2, () => configurationId);

        var tab = registry.Add();

        Assert.AreEqual(sessionId, tab.SessionId);
        Assert.AreEqual(configurationId, tab.ConfigurationId);
        Assert.AreNotEqual(tab.SessionId.ToString(), tab.ConfigurationId.ToString());
    }

    [TestMethod]
    public void RenameTrimsValidKoreanNameAndPreservesSessionIdentity()
    {
        var registry = CreateRegistry(2);
        var tab = registry.Add();

        var renamed = registry.Rename(tab.SessionId, "  서버 운영  ");

        Assert.IsTrue(renamed);
        Assert.AreEqual(tab.SessionId, registry.CreateSnapshot().Tabs[0].SessionId);
        Assert.AreEqual("서버 운영", registry.CreateSnapshot().Tabs[0].Name);
    }

    [TestMethod]
    [DataRow("   ")]
    [DataRow("first\nsecond")]
    [DataRow("first\u0001second")]
    public void RenameWithEmptyOrControlNameRejectsAndKeepsExistingName(string name)
    {
        var registry = CreateRegistry(2);
        var tab = registry.Add();

        var renamed = registry.Rename(tab.SessionId, name);

        Assert.IsFalse(renamed);
        Assert.AreEqual("PowerShell 1", registry.CreateSnapshot().Tabs[0].Name);
    }

    [TestMethod]
    public void RenameWithMoreThanThirtyTwoTextElementsRejectsName()
    {
        var registry = CreateRegistry(2);
        var tab = registry.Add();

        var renamed = registry.Rename(tab.SessionId, new string('가', 33));

        Assert.IsFalse(renamed);
        Assert.AreEqual("PowerShell 1", registry.CreateSnapshot().Tabs[0].Name);
    }

    [TestMethod]
    public void MoveLeftAndRightReordersTabsWhileKeepingActiveSessionIdentifier()
    {
        var registry = CreateRegistry(3);
        var first = registry.Add();
        var second = registry.Add();
        var third = registry.Add();
        Assert.IsTrue(registry.Select(second.SessionId));

        Assert.IsTrue(registry.MoveRight(second.SessionId));
        Assert.IsFalse(registry.MoveRight(second.SessionId));
        Assert.IsTrue(registry.MoveLeft(second.SessionId));

        var snapshot = registry.CreateSnapshot();
        CollectionAssert.AreEqual(new[] { first.SessionId, second.SessionId, third.SessionId },
                                  snapshot.Tabs.Select(tab => tab.SessionId).ToArray());
        Assert.AreEqual(second.SessionId, snapshot.ActiveSessionId);
    }

    private static TerminalTabRegistry CreateRegistry(int identifierCount)
    {
        var identifiers = new Queue<TerminalSessionId>(Enumerable.Range(1, identifierCount)
                                                           .Select(value => new TerminalSessionId(CreateGuid(value))));
        return new TerminalTabRegistry(identifiers.Dequeue);
    }

    private static Guid CreateGuid(int value)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    }
}
