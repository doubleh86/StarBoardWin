using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Infrastructure;

internal sealed class ConPtySessionFactory : ITerminalSessionFactory
{
    private readonly IDiagnosticLog diagnosticLog;

    internal ConPtySessionFactory(IDiagnosticLog diagnosticLog)
    {
        this.diagnosticLog = diagnosticLog;
    }

    public ITerminalSession Start(ShellLaunchSpec shell, int columns, int rows)
    {
        return ConPtySession.Start(shell, columns, rows, diagnosticLog);
    }
}
