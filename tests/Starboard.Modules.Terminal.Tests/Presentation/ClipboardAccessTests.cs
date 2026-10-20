using System.Runtime.InteropServices;
using System.Windows.Threading;
using Starboard.Modules.Terminal.Presentation;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

#pragma warning disable CA2201 // Inject the exact native exception types produced by WPF Clipboard.

namespace Starboard.Modules.Terminal.Tests.Presentation;

[TestClass]
public sealed class ClipboardAccessTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExecuteAsyncClipboardBusyThenAvailableCompletesOnce(bool comException)
    {
        var attempts = 0;
        var delays = 0;
        var observations = 0;
        var verifications = 0;
        var access = new ClipboardAccess(() => verifications++, _ =>
        {
            delays++;
            return Task.CompletedTask;
        });
        var result = await access.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw BusyException(comException);
            }

            return "snapshot";
        }, () => true, _ => observations++, CancellationToken.None);

        Assert.AreEqual(ClipboardAccessStatus.Completed, result.Status);
        Assert.AreEqual("snapshot", result.Value);
        Assert.AreEqual(3, attempts);
        Assert.AreEqual(3, verifications);
        Assert.AreEqual(2, delays);
        Assert.AreEqual(2, observations);
    }

    [TestMethod]
    public async Task ExecuteAsyncBusyLimitThenNewRequestRecoversWithoutRestart()
    {
        var attempts = 0;
        var access = new ClipboardAccess(() => { }, _ => Task.CompletedTask);
        var failed = await access.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw BusyException(false);
        }, () => true, _ => { }, CancellationToken.None);
        var recovered = await access.ExecuteAsync(() => "", () => true, _ => { }, CancellationToken.None);

        Assert.AreEqual(3, attempts);
        Assert.AreEqual(ClipboardAccessStatus.Unavailable, failed.Status);
        Assert.AreEqual(ClipboardAccessStatus.Completed, recovered.Status);
        Assert.AreEqual("", recovered.Value);
    }

    [TestMethod]
    [DataRow("tab")]
    [DataRow("generation")]
    [DataRow("renderer")]
    [DataRow("confirmation")]
    [DataRow("shutdown")]
    public async Task ExecuteAsyncRequestChangesDuringDelayNeverRetriesOrDelivers(string boundary)
    {
        var current = true;
        var attempts = 0;
        using var cancellation = new CancellationTokenSource();
        var access = new ClipboardAccess(() => { }, _ =>
        {
            current = false;
            if (boundary == "shutdown")
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            }

            return Task.CompletedTask;
        });
        var result = await access.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw BusyException(false);
        }, () => current, _ => { }, cancellation.Token);

        Assert.AreEqual(ClipboardAccessStatus.Stale, result.Status);
        Assert.AreEqual(1, attempts);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task ExecuteAsyncOleReentrancyInvalidatesTargetDiscardsReadSnapshot()
    {
        var current = true;
        var access = new ClipboardAccess(() => { }, _ => Task.CompletedTask);
        var result = await access.ExecuteAsync(() =>
        {
            current = false;
            return "must not deliver";
        }, () => current, _ => { }, CancellationToken.None);

        Assert.AreEqual(ClipboardAccessStatus.Stale, result.Status);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(3, false)]
    [DataRow(1, true)]
    [DataRow(3, true)]
    public async Task ExecuteAsyncFailedOleCallInvalidatesRequestReturnsStaleWithoutFailureFeedback(int invalidateAttempt,
                                                                                                    bool shutdown)
    {
        var attempts = 0;
        var delays = 0;
        var observations = 0;
        var current = true;
        using var cancellation = new CancellationTokenSource();
        var access = new ClipboardAccess(() => { }, _ =>
        {
            delays++;
            return Task.CompletedTask;
        });
        var result = await access.ExecuteAsync<string>(() =>
        {
            attempts++;
            if (attempts == invalidateAttempt)
            {
                if (shutdown == true)
                {
                    cancellation.Cancel();
                }
                else
                {
                    current = false;
                }
            }

            throw BusyException(false);
        }, () => current, _ => observations++, cancellation.Token);

        Assert.AreEqual(ClipboardAccessStatus.Stale, result.Status);
        Assert.IsNull(result.Value);
        Assert.AreEqual(invalidateAttempt, attempts);
        Assert.AreEqual(invalidateAttempt, observations);
        Assert.AreEqual(invalidateAttempt - 1, delays);
    }

    [TestMethod]
    public async Task ExecuteAsyncUnrelatedExceptionsPropagatesWithoutRetry()
    {
        var access = new ClipboardAccess(() => { }, _ => throw new AssertFailedException("Unexpected retry."));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            access.ExecuteAsync<string>(() => throw new InvalidOperationException("programming error"),
                                        () => true, _ => { }, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ExternalException>(() =>
            access.ExecuteAsync<string>(() => throw new ExternalException("unknown", unchecked((int)0x80004005)),
                                        () => true, _ => { }, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("current")]
    [DataRow("tab")]
    [DataRow("generation")]
    [DataRow("renderer")]
    public async Task ExecuteAsyncRealDispatcherDelayClipboardAdapterStaysOnSta(string boundary)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var attempts = 0;
                    var sessionId = TerminalSessionId.CreateNew();
                    var instance = Guid.NewGuid();
                    var generation = 10L;
                    var epoch = 20L;
                    var request = new ClipboardRequestLifetime(generation, instance, epoch,
                                                               new TerminalSessionReference(sessionId.Value, 7));
                    var invalidateDuringDelay = boundary != "current";
                    var verifications = 0;
                    var observations = 0;
                    var access = new ClipboardAccess(() =>
                    {
                        dispatcher.VerifyAccess();
                        Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                        verifications++;
                    }, async token =>
                    {
                        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        _ = dispatcher.InvokeAsync(() =>
                        {
                            dispatcher.VerifyAccess();
                            if (boundary == "tab")
                            {
                                // Even returning to the same tab must invalidate the captured request.
                                epoch += 2;
                            }
                            else if (boundary == "generation")
                            {
                                generation++;
                            }
                            else if (boundary == "renderer")
                            {
                                instance = Guid.NewGuid();
                            }
                            pending.SetResult();
                        });
                        await Task.Delay(10, token);
                        await pending.Task.WaitAsync(token);
                        dispatcher.VerifyAccess();
                        Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                    });
                    var result = await access.ExecuteAsync(() =>
                    {
                        if (++attempts == 1)
                        {
                            throw BusyException(false);
                        }

                        return "snapshot";
                    }, () => request.CanApply(generation, instance, epoch, sessionId, true, true, false),
                       _ => observations++, CancellationToken.None);
                    Assert.AreEqual(invalidateDuringDelay == true ? ClipboardAccessStatus.Stale : ClipboardAccessStatus.Completed,
                                    result.Status);
                    Assert.AreEqual(invalidateDuringDelay == true ? 1 : 2, attempts);
                    Assert.AreEqual(invalidateDuringDelay == true ? null : "snapshot", result.Value);
                    Assert.AreEqual(2, verifications);
                    Assert.AreEqual(1, observations);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
    }

    private static ExternalException BusyException(bool comException)
    {
        return comException == true
            ? new COMException("synthetic clipboard contention", unchecked((int)0x800401D0))
            : new ExternalException("synthetic clipboard contention", unchecked((int)0x800401D0));
    }
}
