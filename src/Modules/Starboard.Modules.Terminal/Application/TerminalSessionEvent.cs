using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal readonly record struct TerminalSessionOutput(TerminalSessionId SessionId, string Data);

internal readonly record struct TerminalSessionExit(TerminalSessionId SessionId, uint ExitCode);
