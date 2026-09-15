namespace Starboard.Modules.DesktopIntegration.Contracts;

public enum PanelCollapsedHeightChangePhase
{
    Preview,
    Commit,
}

/// <summary>
/// Describes a native resize session without coupling DesktopIntegration to settings persistence.
/// Preview notifications update dependent surfaces, while Commit is raised once when sizing ends.
/// </summary>
public sealed class PanelCollapsedHeightChangeEventArgs : EventArgs
{
    public PanelCollapsedHeightChangeEventArgs(Guid requestId, PanelCollapsedHeightChangePhase phase,
                                               double requestedHeightDip, double lastSavedHeightDip)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A panel height change requires a valid request identifier.",
                                        nameof(requestId));
        }

        if (Enum.IsDefined(phase) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase,
                                                  "The panel height change phase is not supported.");
        }

        ValidateHeight(requestedHeightDip, nameof(requestedHeightDip));
        ValidateHeight(lastSavedHeightDip, nameof(lastSavedHeightDip));
        RequestId = requestId;
        Phase = phase;
        RequestedHeightDip = requestedHeightDip;
        LastSavedHeightDip = lastSavedHeightDip;
    }

    public Guid RequestId { get; }

    public PanelCollapsedHeightChangePhase Phase { get; }

    public double RequestedHeightDip { get; }

    public double LastSavedHeightDip { get; }

    private static void ValidateHeight(double heightDip, string parameterName)
    {
        if (double.IsFinite(heightDip) == false || heightDip < 96 || heightDip > 720)
        {
            throw new ArgumentOutOfRangeException(parameterName, heightDip,
                                                  "The collapsed panel height must be from 96 through 720 DIP.");
        }
    }
}
