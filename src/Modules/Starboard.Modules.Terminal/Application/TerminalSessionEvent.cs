using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal readonly record struct TerminalSessionOutput(TerminalSessionReference Session, string Data)
{
    internal TerminalSessionId SessionId => new(Session.SessionId);
}

internal readonly record struct TerminalSessionExit(TerminalSessionReference Session, uint ExitCode)
{
    internal TerminalSessionId SessionId => new(Session.SessionId);
}
