namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// A DesktopIntegration-owned request for the host to display its single shortcut guide window.
/// </summary>
public sealed class ShortcutGuideRequestEventArgs : EventArgs
{
    public ShortcutGuideRequestEventArgs(GlobalShortcutRegistrationSnapshot registrationSnapshot)
    {
        ArgumentNullException.ThrowIfNull(registrationSnapshot);
        RegistrationSnapshot = registrationSnapshot;
    }

    public GlobalShortcutRegistrationSnapshot RegistrationSnapshot { get; }
}

public enum GlobalShortcutRegistrationStatus
{
    Registered,
    NotRegistered,
    Unknown,
}

/// <summary>
/// Separates the configured gesture from the gesture that is actually registered with Windows.
/// </summary>
public sealed record GlobalShortcutRegistrationState
{
    public GlobalShortcutRegistrationState(string configuredGesture, string? effectiveGesture,
                                           GlobalShortcutRegistrationStatus status, string? failureMessage = null)
    {
        if (string.IsNullOrWhiteSpace(configuredGesture) == true)
        {
            throw new ArgumentException("The configured shortcut gesture cannot be empty.", nameof(configuredGesture));
        }

        if (status == GlobalShortcutRegistrationStatus.Registered &&
            string.IsNullOrWhiteSpace(effectiveGesture) == true)
        {
            throw new ArgumentException("A registered shortcut requires its effective gesture.",
                                        nameof(effectiveGesture));
        }

        if (status != GlobalShortcutRegistrationStatus.Registered && effectiveGesture is not null)
        {
            throw new ArgumentException("An unregistered or unknown shortcut cannot have an effective gesture.",
                                        nameof(effectiveGesture));
        }

        ConfiguredGesture = configuredGesture;
        EffectiveGesture = effectiveGesture;
        Status = status;
        FailureMessage = failureMessage;
    }

    public string ConfiguredGesture { get; }

    public string? EffectiveGesture { get; }

    public GlobalShortcutRegistrationStatus Status { get; }

    public string? FailureMessage { get; }
}

/// <summary>
/// Point-in-time registration state for the global shortcuts shown by the shortcut guide.
/// </summary>
public sealed record GlobalShortcutRegistrationSnapshot
{
    public GlobalShortcutRegistrationSnapshot(GlobalShortcutRegistrationState expand,
                                              GlobalShortcutRegistrationState activation)
    {
        ArgumentNullException.ThrowIfNull(expand);
        ArgumentNullException.ThrowIfNull(activation);
        Expand = expand;
        Activation = activation;
    }

    public GlobalShortcutRegistrationState Expand { get; }

    public GlobalShortcutRegistrationState Activation { get; }
}
