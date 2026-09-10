namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Correlates exactly one renderer request and response. A renderer retry must reuse this value.
/// </summary>
public readonly record struct TerminalSavedTabRequestId
{
    public TerminalSavedTabRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The saved terminal tab request identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalSavedTabRequestId CreateNew()
    {
        return new TerminalSavedTabRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}

public sealed record TerminalSavedTabCreateRequest
{
    public TerminalSavedTabCreateRequest(TerminalSavedTabRequestId requestId, string name, string startingDirectory,
                                         TerminalShellKind shellKind)
    {
        ValidateRequestId(requestId);
        TerminalSavedTabContractValidator.ValidateDefinition(name, startingDirectory, shellKind);
        RequestId = requestId;
        Name = name;
        StartingDirectory = startingDirectory;
        ShellKind = shellKind;
    }

    public TerminalSavedTabRequestId RequestId { get; }

    public string Name { get; }

    public string StartingDirectory { get; }

    public TerminalShellKind ShellKind { get; }

    private static void ValidateRequestId(TerminalSavedTabRequestId requestId)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab create request requires a valid request identifier.",
                                        nameof(requestId));
        }
    }
}

public sealed record TerminalSavedTabUpdateRequest
{
    public TerminalSavedTabUpdateRequest(TerminalSavedTabRequestId requestId, TerminalSavedTab savedTab)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab update request requires a valid request identifier.",
                                        nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(savedTab);
        RequestId = requestId;
        SavedTab = savedTab;
    }

    public TerminalSavedTabRequestId RequestId { get; }

    public TerminalSavedTab SavedTab { get; }
}

public sealed record TerminalSavedTabDeleteRequest
{
    public TerminalSavedTabDeleteRequest(TerminalSavedTabRequestId requestId, TerminalSavedTabId savedTabId)
    {
        ValidateIdentifiers(requestId, savedTabId);
        RequestId = requestId;
        SavedTabId = savedTabId;
    }

    public TerminalSavedTabRequestId RequestId { get; }

    public TerminalSavedTabId SavedTabId { get; }

    private static void ValidateIdentifiers(TerminalSavedTabRequestId requestId, TerminalSavedTabId savedTabId)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab delete request requires a valid request identifier.",
                                        nameof(requestId));
        }

        if (savedTabId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab delete request requires a valid saved tab identifier.",
                                        nameof(savedTabId));
        }
    }
}

public enum TerminalSavedTabOperation
{
    Create,
    Update,
    Delete,
}

public enum TerminalSavedTabOperationStatus
{
    Succeeded,
    Cancelled,
    SavedTabLimitReached,
    SavedTabNotFound,
    InvalidName,
    InvalidStartingDirectory,
    UnsupportedShell,
    Failed,
}

/// <summary>
/// Result of a create, update, or delete request. Snapshot is the authoritative list after a
/// successful change and is null when persistence did not commit the change.
/// </summary>
public sealed record TerminalSavedTabOperationResult(TerminalSavedTabRequestId RequestId,
                                                     TerminalSavedTabOperation Operation,
                                                     TerminalSavedTabOperationStatus Status,
                                                     TerminalSavedTabsSnapshot? Snapshot,
                                                     string? FailureMessage)
{
    public bool Succeeded => Status == TerminalSavedTabOperationStatus.Succeeded;
}

/// <summary>
/// Requests a new runtime tab from one saved definition. Reusing RequestId identifies a duplicate
/// delivery; choosing the same SavedTabId later with a new RequestId is a distinct launch.
/// </summary>
public sealed record TerminalSavedTabLaunchRequest
{
    public TerminalSavedTabLaunchRequest(TerminalSavedTabRequestId requestId, TerminalSavedTabId savedTabId)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab launch request requires a valid request identifier.",
                                        nameof(requestId));
        }

        if (savedTabId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab launch request requires a valid saved tab identifier.",
                                        nameof(savedTabId));
        }

        RequestId = requestId;
        SavedTabId = savedTabId;
    }

    public TerminalSavedTabRequestId RequestId { get; }

    public TerminalSavedTabId SavedTabId { get; }

    public bool IsDuplicateOf(TerminalSavedTabLaunchRequest pendingRequest)
    {
        ArgumentNullException.ThrowIfNull(pendingRequest);
        return RequestId == pendingRequest.RequestId;
    }
}

/// <summary>
/// Cancels only the matching pending launch. Completion after this request is a late response and
/// must not be applied after the renderer removes the pending request.
/// </summary>
public sealed record TerminalSavedTabLaunchCancellation
{
    public TerminalSavedTabLaunchCancellation(TerminalSavedTabRequestId requestId)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab launch cancellation requires a valid request identifier.",
                                        nameof(requestId));
        }

        RequestId = requestId;
    }

    public TerminalSavedTabRequestId RequestId { get; }

    public bool Cancels(TerminalSavedTabLaunchRequest pendingRequest)
    {
        ArgumentNullException.ThrowIfNull(pendingRequest);
        return RequestId == pendingRequest.RequestId;
    }
}

public enum TerminalSavedTabLaunchStatus
{
    Started,
    DuplicateRequest,
    Cancelled,
    RunningTabLimitReached,
    SavedTabNotFound,
    StartingDirectoryUnavailable,
    ShellUnavailable,
    Failed,
}

/// <summary>
/// Correlated launch completion. A result can be applied only while the exact request is pending;
/// otherwise it is a late or unrelated response and must be ignored.
/// </summary>
public sealed record TerminalSavedTabLaunchResult(TerminalSavedTabRequestId RequestId,
                                                  TerminalSavedTabId SavedTabId,
                                                  TerminalSavedTabLaunchStatus Status,
                                                  TerminalSessionReference? Session,
                                                  string? FailureMessage)
{
    public bool Started => Status == TerminalSavedTabLaunchStatus.Started;

    public bool CanApplyTo(TerminalSavedTabLaunchRequest? pendingRequest)
    {
        return pendingRequest is not null && RequestId == pendingRequest.RequestId &&
               SavedTabId == pendingRequest.SavedTabId;
    }
}
