using System.Windows;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Presentation;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal;

public sealed class TerminalModule : IDisposable
{
    private readonly TerminalView terminalView;
    private bool isDisposed;

    public TerminalModule(IDiagnosticLog diagnosticLog)
    {
        terminalView = new TerminalView(diagnosticLog);
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
        isDisposed = true;
        terminalView.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
