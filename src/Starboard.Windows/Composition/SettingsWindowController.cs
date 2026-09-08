namespace Starboard.Windows.Composition;

internal interface ISettingsWindow
{
    event EventHandler? Closed;

    bool IsVisible { get; }

    bool IsMinimized { get; }

    void Show();

    void Restore();

    void Activate();

    void Close();
}

internal sealed class SettingsWindowController : IDisposable
{
    private readonly Func<ISettingsWindow> createWindow;
    private ISettingsWindow? currentWindow;
    private bool isDisposed;

    internal SettingsWindowController(Func<ISettingsWindow> createWindow)
    {
        ArgumentNullException.ThrowIfNull(createWindow);
        this.createWindow = createWindow;
    }

    internal void OpenOnExplicitUserRequest()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var window = currentWindow;
        if (window is null)
        {
            window = createWindow();
            currentWindow = window;
            window.Closed += HandleClosed;
        }

        if (window.IsVisible == false)
        {
            window.Show();
        }

        if (window.IsMinimized == true)
        {
            window.Restore();
        }

        window.Activate();
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        var window = currentWindow;
        currentWindow = null;
        if (window is not null)
        {
            window.Closed -= HandleClosed;
            window.Close();
        }
    }

    private void HandleClosed(object? sender, EventArgs eventArguments)
    {
        _ = eventArguments;
        if (ReferenceEquals(sender, currentWindow) == false)
        {
            return;
        }

        currentWindow!.Closed -= HandleClosed;
        currentWindow = null;
    }
}
