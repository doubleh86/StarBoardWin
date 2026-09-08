using Starboard.Modules.DesktopIntegration.Infrastructure;

namespace Starboard.Modules.DesktopIntegration.Tests.Infrastructure;

[TestClass]
public sealed class DisplaySettingsObserverTests
{
    [TestMethod]
    public void DisposeUnsubscribesAndBlocksSubsequentDisplayNotifications()
    {
        var eventSource = new FakeDisplaySettingsEventSource();
        var observer = new DisplaySettingsObserver(eventSource);
        var notificationCount = 0;
        observer.DisplaySettingsChanged += (_, _) => notificationCount++;

        eventSource.RaiseChanged();
        observer.Dispose();
        observer.Dispose();
        eventSource.RaiseChanged();

        Assert.AreEqual(1, notificationCount);
        Assert.AreEqual(1, eventSource.SubscriptionCount);
        Assert.AreEqual(1, eventSource.UnsubscriptionCount);
    }

    [TestMethod]
    public void DisposeWhenUnsubscribeFailsPreservesSubscriptionAndAllowsRetry()
    {
        var eventSource = new FakeDisplaySettingsEventSource
        {
            UnsubscribeException = new InvalidOperationException(
                "The display event source is temporarily unavailable."),
        };
        var observer = new DisplaySettingsObserver(eventSource);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(observer.Dispose);

        Assert.AreSame(eventSource.UnsubscribeException, exception);
        Assert.AreEqual(1, eventSource.UnsubscriptionCount);

        eventSource.UnsubscribeException = null;
        observer.Dispose();

        Assert.AreEqual(2, eventSource.UnsubscriptionCount);
    }

    private sealed class FakeDisplaySettingsEventSource : IDisplaySettingsEventSource
    {
        private EventHandler? _changed;

        internal int SubscriptionCount { get; private set; }

        internal int UnsubscriptionCount { get; private set; }

        internal Exception? UnsubscribeException { get; set; }

        public event EventHandler? Changed
        {
            add
            {
                SubscriptionCount++;
                _changed += value;
            }

            remove
            {
                UnsubscriptionCount++;

                if (UnsubscribeException is not null)
                {
                    throw UnsubscribeException;
                }

                _changed -= value;
            }
        }

        internal void RaiseChanged()
        {
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
