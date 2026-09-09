using System.ComponentModel;
using Starboard.Modules.DesktopIntegration.Infrastructure;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

[TestClass]
public sealed class ForegroundWindowObserverTests
{
    [TestMethod]
    public void DisposeUnhooksOnceAndBlocksSubsequentForegroundNotifications()
    {
        var nativeApi = new FakeWinEventHookNativeApi();
        var observer = new ForegroundWindowObserver(nativeApi);
        var notificationCount = 0;
        observer.ForegroundChanged += (_, _) => notificationCount++;

        nativeApi.RaiseForegroundChanged();
        observer.Dispose();
        observer.Dispose();
        nativeApi.RaiseForegroundChanged();

        Assert.AreEqual(1, notificationCount);
        Assert.AreEqual(1, nativeApi.UnregisterCount);
        Assert.AreEqual(0, observer.ReleaseErrorCode);
    }

    [TestMethod]
    public void DisposeWhenUnhookFailsPreservesNativeErrorAndAllowsOwnerThreadRetry()
    {
        var nativeApi = new FakeWinEventHookNativeApi
        {
            UnregisterResult = false,
            UnregisterErrorCode = 5,
        };
        var observer = new ForegroundWindowObserver(nativeApi);

        var exception = Assert.ThrowsExactly<Win32Exception>(observer.Dispose);

        Assert.AreEqual(5, exception.NativeErrorCode);
        Assert.AreEqual(1, nativeApi.UnregisterCount);
        Assert.AreEqual(5, observer.ReleaseErrorCode);

        nativeApi.UnregisterResult = true;
        nativeApi.UnregisterErrorCode = 0;
        observer.Dispose();

        Assert.AreEqual(2, nativeApi.UnregisterCount);
        Assert.AreEqual(0, observer.ReleaseErrorCode);
    }

    [TestMethod]
    public void DisposeFromDifferentThreadFailsWithoutLosingHookOwnership()
    {
        var nativeApi = new FakeWinEventHookNativeApi();
        var observer = new ForegroundWindowObserver(nativeApi);
        Exception? disposalFailure = null;
        var disposalThread = new Thread(() =>
        {
            try
            {
                observer.Dispose();
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }
        });

        disposalThread.Start();
        disposalThread.Join();

        Assert.IsInstanceOfType<InvalidOperationException>(disposalFailure);
        Assert.AreEqual(0, nativeApi.UnregisterCount);

        observer.Dispose();

        Assert.AreEqual(1, nativeApi.UnregisterCount);
    }

    private sealed class FakeWinEventHookNativeApi : IWinEventHookNativeApi
    {
        private WinEventCallback? _callback;

        internal bool UnregisterResult { get; set; } = true;

        internal int UnregisterErrorCode { get; set; }

        internal int UnregisterCount { get; private set; }

        public uint GetCurrentThreadIdentifier()
        {
            return (uint)Environment.CurrentManagedThreadId;
        }

        public nint RegisterForegroundChanged(WinEventCallback callback)
        {
            _callback = callback;
            return new nint(41);
        }

        public bool Unregister(nint hook, out int errorCode)
        {
            Assert.AreEqual(new nint(41), hook);
            UnregisterCount++;
            errorCode = UnregisterErrorCode;

            return UnregisterResult;
        }

        internal void RaiseForegroundChanged()
        {
            _callback?.Invoke(new nint(41), 0x0003, new nint(20), 0, 0, 0, 0);
        }
    }
}
