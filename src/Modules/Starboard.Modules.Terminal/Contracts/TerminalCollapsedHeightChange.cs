namespace Starboard.Modules.Terminal.Contracts;

public readonly record struct TerminalCollapsedHeightChangeRequestId
{
    public TerminalCollapsedHeightChangeRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The collapsed height change request identifier cannot be empty.",
                                        nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalCollapsedHeightChangeRequestId CreateNew()
    {
        return new TerminalCollapsedHeightChangeRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}

public enum TerminalCollapsedHeightChangePhase
{
    Preview,
    Commit,
}

/// <summary>
/// Requests either a live height preview or a persisted commit. LastSavedHeightDip is the
/// authoritative rollback target if commit fails.
/// </summary>
public sealed record TerminalCollapsedHeightChangeRequest
{
    public TerminalCollapsedHeightChangeRequest(TerminalCollapsedHeightChangeRequestId requestId,
                                                TerminalCollapsedHeightChangePhase phase,
                                                double requestedHeightDip, double lastSavedHeightDip)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A height change requires a valid request identifier.", nameof(requestId));
        }

        if (Enum.IsDefined(phase) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase,
                                                  "The collapsed height change phase is not supported.");
        }

        ValidateHeight(requestedHeightDip, nameof(requestedHeightDip));
        ValidateHeight(lastSavedHeightDip, nameof(lastSavedHeightDip));
        RequestId = requestId;
        Phase = phase;
        RequestedHeightDip = requestedHeightDip;
        LastSavedHeightDip = lastSavedHeightDip;
    }

    public TerminalCollapsedHeightChangeRequestId RequestId { get; }

    public TerminalCollapsedHeightChangePhase Phase { get; }

    public double RequestedHeightDip { get; }

    public double LastSavedHeightDip { get; }

    private static void ValidateHeight(double heightDip, string parameterName)
    {
        if (double.IsFinite(heightDip) == false || heightDip < 96 || heightDip > 720)
        {
            throw new ArgumentOutOfRangeException(parameterName, heightDip,
                                                  "The collapsed terminal height must be from 96 through 720 DIP.");
        }
    }
}

public enum TerminalCollapsedHeightChangeStatus
{
    Applied,
    Saved,
    Reverted,
    Failed,
}

/// <summary>
/// Reports the authoritative height after apply, persistence, or rollback. A persistence failure
/// is represented by Reverted and returns the last saved height instead of leaving modules divergent.
/// </summary>
public sealed record TerminalCollapsedHeightChangeResult
{
    public TerminalCollapsedHeightChangeResult(TerminalCollapsedHeightChangeRequestId requestId,
                                               TerminalCollapsedHeightChangeStatus status,
                                               double appliedHeightDip, string? failureMessage = null)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A height change result requires a valid request identifier.",
                                        nameof(requestId));
        }

        if (Enum.IsDefined(status) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status,
                                                  "The collapsed height change status is not supported.");
        }

        if (double.IsFinite(appliedHeightDip) == false || appliedHeightDip < 96 || appliedHeightDip > 720)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedHeightDip), appliedHeightDip,
                                                  "The applied terminal height must be from 96 through 720 DIP.");
        }

        var failed = status is TerminalCollapsedHeightChangeStatus.Reverted or
                     TerminalCollapsedHeightChangeStatus.Failed;
        if (failed == true && string.IsNullOrWhiteSpace(failureMessage) == true)
        {
            throw new ArgumentException("A failed height change requires a user-safe failure message.",
                                        nameof(failureMessage));
        }

        if (failed == false && failureMessage is not null)
        {
            throw new ArgumentException("A successful height change cannot include a failure message.",
                                        nameof(failureMessage));
        }

        RequestId = requestId;
        Status = status;
        AppliedHeightDip = appliedHeightDip;
        FailureMessage = failureMessage;
    }

    public TerminalCollapsedHeightChangeRequestId RequestId { get; }

    public TerminalCollapsedHeightChangeStatus Status { get; }

    public double AppliedHeightDip { get; }

    public string? FailureMessage { get; }

    public bool CanApplyTo(TerminalCollapsedHeightChangeRequest? pendingRequest)
    {
        return pendingRequest is not null && RequestId == pendingRequest.RequestId;
    }
}

/// <summary>
/// Public coordination seam used by the Terminal module without referencing desktop or preference implementations.
/// </summary>
public delegate ValueTask<TerminalCollapsedHeightChangeResult> TerminalCollapsedHeightChangeCallback(
    TerminalCollapsedHeightChangeRequest request, CancellationToken cancellationToken);
