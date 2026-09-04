namespace Starboard.Modules.Terminal.Domain;

internal sealed record TerminalWorkspaceSnapshot(
    IReadOnlyList<TerminalTab> Tabs,
    TerminalSessionId? ActiveSessionId);
