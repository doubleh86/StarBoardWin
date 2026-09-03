namespace Starboard.Modules.Terminal.Domain;

internal enum TerminalSessionState
{
    Starting,
    Running,
    Restarting,
    Exited,
    Failed,
}
