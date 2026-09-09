using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Domain;

internal sealed record TerminalTab(TerminalSessionId SessionId, TerminalTabConfigurationId ConfigurationId, string Name,
                                   string StartingDirectory, TerminalShellKind? ShellKind,
                                   TerminalSessionState State, uint? ExitCode);
