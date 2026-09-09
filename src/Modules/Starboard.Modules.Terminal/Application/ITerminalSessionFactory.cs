using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal interface ITerminalSessionFactory
{
    ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows);
}
