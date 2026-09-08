namespace Starboard.Modules.Preferences.Contracts;

/// <summary>
/// Applies a validated settings snapshot. The host owns cross-module live apply
/// and uses this callback to persist only after that work has succeeded.
/// </summary>
public interface ISettingsEditorSaveHandler
{
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
