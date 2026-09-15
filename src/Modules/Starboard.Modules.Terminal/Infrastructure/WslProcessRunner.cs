using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Starboard.Modules.Terminal.Application;

namespace Starboard.Modules.Terminal.Infrastructure;

internal sealed class WslProcessRunner : IWslProcessRunner
{
    public async Task<WslProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments,
                                                 TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executablePath, arguments),
        };
        if (process.Start() == false)
        {
            throw new InvalidOperationException("The WSL query process did not start.");
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token);
        using var output = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, linkedCancellation.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedCancellation.Token);
        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested == true &&
                                                  cancellationToken.IsCancellationRequested == false)
        {
            Kill(process);
            await ObserveAfterCancellationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw new TimeoutException("The WSL distribution query exceeded its deadline.");
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await ObserveAfterCancellationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        return new WslProcessResult(process.ExitCode, output.ToArray());
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (process.HasExited == false)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The process exited between the state check and the bounded cleanup request.
        }
    }

    private static async Task ObserveAfterCancellationAsync(Process process, Task outputTask, Task errorTask)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            // Process disposal below is the final bounded cleanup path.
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            // Stream copies use the same cancellation lifetime as the process wait.
        }
    }
}
