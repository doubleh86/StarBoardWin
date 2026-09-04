using System.Windows;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.Modules.Terminal.Presentation;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal;

public sealed class TerminalModule : IDisposable
{
    private readonly TerminalSessionCoordinator sessionCoordinator;
    private readonly TerminalView terminalView;
    private bool isDisposed;

    public TerminalModule(IDiagnosticLog diagnosticLog)
    {
        sessionCoordinator = new TerminalSessionCoordinator(
            new ConPtySessionFactory(diagnosticLog),
            diagnosticLog);
        terminalView = new TerminalView(diagnosticLog, sessionCoordinator);
    }

    public FrameworkElement Surface => terminalView;

    public Task StartAsync(
        TerminalOptions options,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return terminalView.StartAsync(options, cancellationToken);
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        terminalView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        sessionCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
