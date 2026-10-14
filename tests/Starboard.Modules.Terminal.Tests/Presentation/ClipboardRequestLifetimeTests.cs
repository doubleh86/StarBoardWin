using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Presentation;

namespace Starboard.Modules.Terminal.Tests.Presentation;

[TestClass]
public sealed class ClipboardRequestLifetimeTests
{
    [TestMethod]
    public void CaptureMissingGenerationOrUnavailableRendererRejectsLateRequestBeforeConstructingSession()
    {
        var instance = Guid.NewGuid();
        var sessionId = TerminalSessionId.CreateNew();
        Assert.IsNull(ClipboardRequestLifetime.Capture(10, instance, 20, sessionId, 0, true));
        Assert.IsNull(ClipboardRequestLifetime.Capture(10, instance, 20, sessionId, 7, false));
        Assert.IsNull(ClipboardRequestLifetime.Capture(10, Guid.Empty, 20, sessionId, 7, true));
        var captured = ClipboardRequestLifetime.Capture(10, instance, 20, sessionId, 7, true);
        Assert.IsNotNull(captured);
        Assert.AreEqual(7L, captured.Session.Generation);
    }

    [TestMethod]
    public void CanApplyCurrentRequestOnlyPreservesCapturedSessionAndRenderer()
    {
        var instance = Guid.NewGuid();
        var sessionId = TerminalSessionId.CreateNew();
        var request = new ClipboardRequestLifetime(10, instance, 20, new TerminalSessionReference(sessionId.Value, 7));

        Assert.IsTrue(request.CanApply(10, instance, 20, sessionId, true, true, false));
        Assert.IsFalse(request.CanApply(11, instance, 20, sessionId, true, true, false));
        Assert.IsFalse(request.CanApply(10, Guid.NewGuid(), 20, sessionId, true, true, false));
        Assert.IsFalse(request.CanApply(10, instance, 21, sessionId, true, true, false));
        Assert.IsFalse(request.CanApply(10, instance, 20, TerminalSessionId.CreateNew(), true, true, false));
        Assert.IsFalse(request.CanApply(10, instance, 20, null, true, true, false));
        Assert.IsFalse(request.CanApply(10, instance, 20, sessionId, false, true, false));
        Assert.IsFalse(request.CanApply(10, instance, 20, sessionId, true, false, false));
        Assert.IsFalse(request.CanApply(10, instance, 20, sessionId, true, true, true));
        Assert.IsFalse((request with { RendererInstanceId = Guid.Empty }).CanApply(10, Guid.Empty, 20, sessionId, true, true, false));
    }

    [TestMethod]
    public async Task ExecuteAsyncTabChangesAwayAndBackDuringRetryStillDiscardsSnapshot()
    {
        var instance = Guid.NewGuid();
        var sessionId = TerminalSessionId.CreateNew();
        var request = new ClipboardRequestLifetime(10, instance, 20, new TerminalSessionReference(sessionId.Value, 7));
        var currentEpoch = 20L;
        var currentId = sessionId;
        var attempts = 0;
        var access = new ClipboardAccess(() => { }, _ =>
        {
            currentId = TerminalSessionId.CreateNew();
            currentEpoch++;
            currentId = sessionId;
            currentEpoch++;
            return Task.CompletedTask;
        });
        var result = await access.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new SyntheticClipboardBusyException();
        }, () => request.CanApply(10, instance, currentEpoch, currentId, true, true, false), _ => { }, CancellationToken.None);

        Assert.AreEqual(1, attempts);
        Assert.AreEqual(ClipboardAccessStatus.Stale, result.Status);
    }
}

internal sealed class SyntheticClipboardBusyException : System.Runtime.InteropServices.ExternalException
{
    internal SyntheticClipboardBusyException()
    {
        HResult = unchecked((int)0x800401D0);
    }
}
