namespace Starboard.Modules.Terminal.Contracts;

/// <summary>A host recovery burst, identified by a positive, monotonically increasing revision.</summary>
/// <remarks>
/// Reuse the revision for duplicate notifications. Requests are nonblocking and best effort;
/// unavailable surfaces and revisions already seen by this module are ignored, without replay.
/// </remarks>
public readonly record struct TerminalLayoutCheckRequest(long RecoveryRevision);
