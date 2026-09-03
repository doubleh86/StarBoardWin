namespace Starboard.Modules.Terminal.Domain;

internal sealed record TerminalTab(
    TerminalSessionId SessionId,
    string Name,
    TerminalSessionState State,
    uint? ExitCode);
