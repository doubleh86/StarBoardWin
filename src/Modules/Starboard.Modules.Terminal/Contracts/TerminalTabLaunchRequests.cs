namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Identifies one renderer tab launch intent. A renderer retry reuses this value, while a distinct
/// user action creates a new value.
/// </summary>
public readonly record struct TerminalTabRequestId
{
    public TerminalTabRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The terminal tab request identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalTabRequestId CreateNew()
    {
        return new TerminalTabRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}

/// <summary>
/// Correlates a tab action with the exact session lifetime from which the renderer issued it.
/// </summary>
public readonly record struct TerminalTabRequestToken
{
    public TerminalTabRequestToken(TerminalTabRequestId requestId, TerminalSessionReference session)
    {
        if (requestId.Value == Guid.Empty)
        {
            throw new ArgumentException("A tab request token requires a valid request identifier.", nameof(requestId));
        }

        if (session.SessionId == Guid.Empty || session.Generation < 1)
        {
            throw new ArgumentException("A tab request token requires a valid session reference.", nameof(session));
        }

        RequestId = requestId;
        Session = session;
    }

    public TerminalTabRequestId RequestId { get; }

    public TerminalSessionReference Session { get; }

    public bool IsCurrent(TerminalTabRequestToken pendingToken, TerminalSessionReference currentSession)
    {
        return this == pendingToken && Session == currentSession;
    }
}

public sealed record TerminalNewTabRequest
{
    public TerminalNewTabRequest(TerminalTabRequestId requestId, TerminalSessionReference session,
                                 TerminalLaunchProfileId profileId)
        : this(new TerminalTabRequestToken(requestId, session), profileId)
    {
    }

    public TerminalNewTabRequest(TerminalTabRequestToken token, TerminalLaunchProfileId profileId)
    {
        if (token.RequestId.Value == Guid.Empty || token.Session.SessionId == Guid.Empty ||
            token.Session.Generation < 1)
        {
            throw new ArgumentException("A new tab request requires a valid correlation token.", nameof(token));
        }

        if (profileId.Value is null)
        {
            throw new ArgumentException("A new tab request requires a valid launch profile identifier.",
                                        nameof(profileId));
        }

        Token = token;
        ProfileId = profileId;
    }

    public TerminalTabRequestToken Token { get; }

    public TerminalTabRequestId RequestId => Token.RequestId;

    public TerminalSessionReference Session => Token.Session;

    public Guid SessionId => Token.Session.SessionId;

    public long SessionGeneration => Token.Session.Generation;

    public TerminalLaunchProfileId ProfileId { get; }

    public bool CanApplyTo(TerminalNewTabRequest? pendingRequest, TerminalSessionReference currentSession)
    {
        return pendingRequest is not null && ProfileId == pendingRequest.ProfileId &&
               Token.IsCurrent(pendingRequest.Token, currentSession);
    }
}

/// <summary>
/// Requests a new independent session using only the source tab's launch configuration. Runtime
/// process state, environment, output, history, and jobs are intentionally absent.
/// </summary>
public sealed record TerminalTabDuplicateRequest
{
    public TerminalTabDuplicateRequest(TerminalTabRequestId requestId, TerminalSessionReference sourceSession)
        : this(new TerminalTabRequestToken(requestId, sourceSession))
    {
    }

    public TerminalTabDuplicateRequest(TerminalTabRequestToken token)
    {
        if (token.RequestId.Value == Guid.Empty || token.Session.SessionId == Guid.Empty ||
            token.Session.Generation < 1)
        {
            throw new ArgumentException("A tab duplicate request requires a valid correlation token.", nameof(token));
        }

        Token = token;
    }

    public TerminalTabRequestToken Token { get; }

    public TerminalTabRequestId RequestId => Token.RequestId;

    public TerminalSessionReference SourceSession => Token.Session;

    public Guid SessionId => Token.Session.SessionId;

    public long SessionGeneration => Token.Session.Generation;

    public bool CanApplyTo(TerminalTabDuplicateRequest? pendingRequest,
                           TerminalSessionReference currentSourceSession)
    {
        return pendingRequest is not null && Token.IsCurrent(pendingRequest.Token, currentSourceSession);
    }
}
