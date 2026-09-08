using System.Windows;

namespace Starboard.Modules.Preferences.Contracts;

/// <summary>
/// A host-owned settings-window session. Closing the host window must call
/// <see cref="Cancel"/> so unsaved values are discarded.
/// </summary>
public sealed class SettingsEditorSession
{
    private readonly Action cancel;

    internal SettingsEditorSession(
        FrameworkElement content,
        Task<SettingsEditorOutcome> completion,
        Action cancel)
    {
        Content = content;
        Completion = completion;
        this.cancel = cancel;
    }

    public FrameworkElement Content { get; }

    public Task<SettingsEditorOutcome> Completion { get; }

    public void Cancel()
    {
        cancel();
    }
}
