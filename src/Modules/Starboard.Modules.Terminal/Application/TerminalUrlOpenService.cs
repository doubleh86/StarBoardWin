using System.ComponentModel;
using System.Diagnostics;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal interface ITerminalExternalUrlLauncher
{
    bool Launch(ProcessStartInfo startInfo);
}

internal sealed class TerminalExternalUrlLauncher : ITerminalExternalUrlLauncher
{
    public bool Launch(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}

internal sealed class TerminalUrlOpenService
{
    private readonly ITerminalExternalUrlLauncher launcher;

    internal TerminalUrlOpenService(ITerminalExternalUrlLauncher launcher)
    {
        this.launcher = launcher;
    }

    internal Task<bool> TryOpenAsync(TerminalUrlOpenTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var startInfo = new ProcessStartInfo(target.AbsoluteUri)
        {
            UseShellExecute = true,
        };

        return Task.Run(() => TryLaunch(startInfo), cancellationToken);
    }

    private bool TryLaunch(ProcessStartInfo startInfo)
    {
        try
        {
            return launcher.Launch(startInfo);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or NotSupportedException or
                UnauthorizedAccessException)
        {
            return false;
        }
    }
}
