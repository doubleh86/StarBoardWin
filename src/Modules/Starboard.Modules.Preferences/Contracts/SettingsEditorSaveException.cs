namespace Starboard.Modules.Preferences.Contracts;

/// <summary>
/// Keeps the settings editor open after the settings snapshot was saved but a
/// related host operation needs user-visible retry guidance.
/// </summary>
public sealed class SettingsEditorSaveException : Exception
{
    public SettingsEditorSaveException(string userMessage)
        : base(userMessage)
    {
    }
}
