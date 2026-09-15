using System.Windows;

namespace Starboard.Modules.Preferences.Contracts;

/// <summary>
/// A host-owned settings-window session. Closing the host window must call
/// <see cref="Cancel"/> so unsaved values are discarded.
/// </summary>
public sealed class SettingsEditorSession
{
    private readonly Action cancel;
    private readonly Action<double> synchronizeCollapsedHeight;

    internal SettingsEditorSession(FrameworkElement content, Task<SettingsEditorOutcome> completion, Action cancel,
                                   Action<double> synchronizeCollapsedHeight)
    {
        Content = content;
        Completion = completion;
        this.cancel = cancel;
        this.synchronizeCollapsedHeight = synchronizeCollapsedHeight;
    }

    public FrameworkElement Content { get; }

    public Task<SettingsEditorOutcome> Completion { get; }

    public void Cancel()
    {
        cancel();
    }

    public void SynchronizeCollapsedHeight(double collapsedHeightDip)
    {
        synchronizeCollapsedHeight(collapsedHeightDip);
    }
}
