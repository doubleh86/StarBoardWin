namespace Starboard.Modules.Terminal.Application;

internal sealed record WslProcessResult(int ExitCode, byte[] StandardOutput);

internal interface IWslProcessRunner
{
    Task<WslProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments,
                                    TimeSpan timeout, CancellationToken cancellationToken);
}
