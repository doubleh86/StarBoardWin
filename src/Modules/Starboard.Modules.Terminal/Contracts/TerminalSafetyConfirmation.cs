namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Identifies one pending close, paste, or path-drop confirmation. The value is never reused.
/// </summary>
public readonly record struct TerminalConfirmationRequestId
{
    public TerminalConfirmationRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The terminal confirmation request identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalConfirmationRequestId CreateNew()
    {
        return new TerminalConfirmationRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}

/// <summary>
/// Identifies one lifetime of a runtime session. Generation starts at one and changes whenever
/// the shell backing the session is replaced.
/// </summary>
public readonly record struct TerminalSessionReference
{
    public TerminalSessionReference(Guid sessionId, long generation)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("The runtime terminal session identifier cannot be empty.", nameof(sessionId));
        }

        if (generation < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation,
                                                  "The runtime terminal session generation must be positive.");
        }

        SessionId = sessionId;
        Generation = generation;
    }

    public Guid SessionId { get; }

    public long Generation { get; }
}

/// <summary>
/// Correlates a confirmation with the exact session lifetime that produced it.
/// </summary>
public readonly record struct TerminalConfirmationToken
{
    public TerminalConfirmationToken(TerminalConfirmationRequestId requestId, TerminalSessionReference session)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("The confirmation token requires a valid request identifier.",
                                        nameof(requestId));
        }

        if (session.SessionId == Guid.Empty || session.Generation < 1)
        {
            throw new ArgumentException("The confirmation token requires a valid runtime session reference.",
                                        nameof(session));
        }

        RequestId = requestId;
        Session = session;
    }

    public TerminalConfirmationRequestId RequestId { get; }

    public TerminalSessionReference Session { get; }

    public bool IsCurrent(TerminalConfirmationToken pendingToken, TerminalSessionReference currentSession)
    {
        return this == pendingToken && Session == currentSession;
    }
}

public sealed record TerminalCloseConfirmationRequest
{
    public TerminalCloseConfirmationRequest(TerminalConfirmationToken token, string sessionName)
    {
        ValidateToken(token);
        if (string.IsNullOrWhiteSpace(sessionName) == true)
        {
            throw new ArgumentException("The close confirmation requires a session name.", nameof(sessionName));
        }

        Token = token;
        SessionName = sessionName;
    }

    public TerminalConfirmationToken Token { get; }

    public string SessionName { get; }

    private static void ValidateToken(TerminalConfirmationToken token)
    {
        if (token.RequestId.Value == Guid.Empty || token.Session.SessionId == Guid.Empty ||
            token.Session.Generation < 1)
        {
            throw new ArgumentException("The close confirmation requires a valid correlation token.", nameof(token));
        }
    }
}

/// <summary>
/// Owns the clipboard snapshot shown to the user. The clipboard must not be read again after this
/// request is created.
/// </summary>
public sealed record TerminalPasteConfirmationRequest
{
    public TerminalPasteConfirmationRequest(TerminalConfirmationToken token, string sessionName,
                                            string clipboardText)
    {
        if (token.RequestId.Value == Guid.Empty || token.Session.SessionId == Guid.Empty ||
            token.Session.Generation < 1)
        {
            throw new ArgumentException("The paste confirmation requires a valid correlation token.", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(sessionName) == true)
        {
            throw new ArgumentException("The paste confirmation requires a session name.", nameof(sessionName));
        }

        ArgumentNullException.ThrowIfNull(clipboardText);
        if (clipboardText.Contains('\r') == false && clipboardText.Contains('\n') == false)
        {
            throw new ArgumentException("Paste confirmation is only valid for text containing a line break.",
                                        nameof(clipboardText));
        }

        Token = token;
        SessionName = sessionName;
        ClipboardText = clipboardText;
    }

    public TerminalConfirmationToken Token { get; }

    public string SessionName { get; }

    public string ClipboardText { get; }
}

/// <summary>
/// Owns the already quoted path input shown to the user. The source files and folders are never opened,
/// and the quoted input cannot contain a command-terminating line break.
/// </summary>
public sealed record TerminalPathDropConfirmationRequest
{
    public TerminalPathDropConfirmationRequest(TerminalConfirmationToken token, string sessionName,
                                               string quotedInput)
    {
        if (token.RequestId.Value == Guid.Empty || token.Session.SessionId == Guid.Empty ||
            token.Session.Generation < 1)
        {
            throw new ArgumentException("The path-drop confirmation requires a valid correlation token.",
                                        nameof(token));
        }

        if (string.IsNullOrWhiteSpace(sessionName) == true)
        {
            throw new ArgumentException("The path-drop confirmation requires a session name.", nameof(sessionName));
        }

        if (string.IsNullOrWhiteSpace(quotedInput) == true || quotedInput.Contains('\r') == true ||
            quotedInput.Contains('\n') == true)
        {
            throw new ArgumentException("Path-drop input must be non-empty and cannot contain a line break.",
                                        nameof(quotedInput));
        }

        Token = token;
        SessionName = sessionName;
        QuotedInput = quotedInput;
    }

    public TerminalConfirmationToken Token { get; }

    public string SessionName { get; }

    public string QuotedInput { get; }
}

public enum TerminalConfirmationResult
{
    Confirmed,
    Cancelled,
}

/// <summary>
/// Returns the user's decision together with its original correlation data. Consumers must call
/// <see cref="CanApplyTo"/> against both the pending request and current session before acting.
/// </summary>
public sealed record TerminalConfirmationResponse
{
    public TerminalConfirmationResponse(TerminalConfirmationToken token, TerminalConfirmationResult result)
    {
        if (token.RequestId.Value == Guid.Empty || token.Session.SessionId == Guid.Empty ||
            token.Session.Generation < 1)
        {
            throw new ArgumentException("The confirmation response requires a valid correlation token.", nameof(token));
        }

        if (Enum.IsDefined(result) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(result), result,
                                                  "The confirmation result is not supported.");
        }

        Token = token;
        Result = result;
    }

    public TerminalConfirmationToken Token { get; }

    public TerminalConfirmationResult Result { get; }

    public bool CanApplyTo(TerminalConfirmationToken pendingToken, TerminalSessionReference currentSession)
    {
        return Token.IsCurrent(pendingToken, currentSession);
    }
}
