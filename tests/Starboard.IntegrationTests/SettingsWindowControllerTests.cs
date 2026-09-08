using Starboard.Windows.Composition;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class SettingsWindowControllerTests
{
    [TestMethod]
    public void OpenOnExplicitUserRequestExistingWindowActivatesSingleWindow()
    {
        var createdWindows = new List<FakeSettingsWindow>();
        using var controller = new SettingsWindowController(() =>
        {
            var window = new FakeSettingsWindow();
            createdWindows.Add(window);
            return window;
        });

        controller.OpenOnExplicitUserRequest();
        controller.OpenOnExplicitUserRequest();

        Assert.AreEqual(1, createdWindows.Count);
        Assert.AreEqual(1, createdWindows[0].ShowCount);
        Assert.AreEqual(2, createdWindows[0].ActivateCount);
    }

    [TestMethod]
    public void OpenOnExplicitUserRequestPreviousWindowClosedCreatesNewWindowOnlyOnNextRequest()
    {
        var createdWindows = new List<FakeSettingsWindow>();
        using var controller = new SettingsWindowController(() =>
        {
            var window = new FakeSettingsWindow();
            createdWindows.Add(window);
            return window;
        });

        controller.OpenOnExplicitUserRequest();
        createdWindows[0].RaiseClosed();

        Assert.AreEqual(1, createdWindows.Count);
        controller.OpenOnExplicitUserRequest();
        Assert.AreEqual(2, createdWindows.Count);
    }

    [TestMethod]
    public void DisposeActiveSettingsWindowClosesWithoutActivatingAnotherWindow()
    {
        var window = new FakeSettingsWindow();
        var controller = new SettingsWindowController(() => window);
        controller.OpenOnExplicitUserRequest();

        controller.Dispose();

        Assert.AreEqual(1, window.CloseCount);
        Assert.AreEqual(1, window.ActivateCount);
    }

    private sealed class FakeSettingsWindow : ISettingsWindow
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }

        public bool IsMinimized { get; set; }

        internal int ShowCount { get; private set; }

        internal int ActivateCount { get; private set; }

        internal int CloseCount { get; private set; }

        public void Show()
        {
            IsVisible = true;
            ShowCount++;
        }

        public void Restore()
        {
            IsMinimized = false;
        }

        public void Activate()
        {
            ActivateCount++;
        }

        public void Close()
        {
            CloseCount++;
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        internal void RaiseClosed()
        {
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
