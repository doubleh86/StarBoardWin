namespace Starboard.Modules.Terminal.Domain;

internal enum TerminalTabLaunchStatus
{
    Started,
    Failed,
    DuplicateRequest,
    StaleSource,
    TabLimitReached,
    Unavailable,
}

internal sealed record TerminalTabLaunchResult(TerminalTabLaunchStatus Status, TerminalTab? Tab,
                                               string? FailureMessage = null)
{
    internal bool Succeeded => Status == TerminalTabLaunchStatus.Started;
}
