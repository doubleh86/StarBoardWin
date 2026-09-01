using System.IO;
using System.Text;
using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.ConPtyTestHost;

internal static class Program
{
    private const string Marker = "STARBOARD_CONPTY_TEST";

    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 1)
        {
            return 2;
        }

        var resultPath = arguments[0];

        try
        {
            var resolvedShell = ShellResolver.Resolve("cmd.exe");
            var shell = new ShellLaunchSpec(
                resolvedShell.ExecutablePath,
                "/D /Q",
                resolvedShell.WorkingDirectory);
            var output = new StringBuilder();
            var firstCommandReceived = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var markerReceived = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await using var session = ConPtySession.Start(
                shell,
                80,
                24,
                new NullDiagnosticLog());
            session.OutputReceived += data =>
            {
                lock (output)
                {
                    output.Append(data);
                    if (output.ToString().Contains(
                        "STARBOARD_DIRECTORY_SET",
                        StringComparison.Ordinal) == true)
                    {
                        firstCommandReceived.TrySetResult();
                    }

                    if (output.ToString().Contains(Marker, StringComparison.Ordinal) == true)
                    {
                        markerReceived.TrySetResult();
                    }
                }
            };
            session.BeginReading();

            await session.WriteAsync(
                "cd /d \"%TEMP%\" & echo STARBOARD_DIRECTORY_SET\r\n",
                CancellationToken.None);
            await firstCommandReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await session.WriteAsync(
                $"if /I \"%CD%\"==\"%TEMP%\" echo {Marker}\r\n",
                CancellationToken.None);

            await markerReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            WriteResult(resultPath, true, null);
            return 0;
        }
        catch (Exception exception)
        {
            WriteResult(resultPath, false, exception.GetType().Name);
            return 1;
        }
    }

    private static void WriteResult(
        string resultPath,
        bool succeeded,
        string? errorType)
    {
        var json = JsonSerializer.Serialize(
            new TestHostResult(succeeded, errorType),
            JsonSerializerOptions.Web);
        File.WriteAllText(resultPath, json, Encoding.UTF8);
    }

    private sealed record TestHostResult(bool Succeeded, string? ErrorType);

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(
            DiagnosticLevel level,
            string subsystem,
            string operation,
            string message,
            Exception? exception = null)
        {
            _ = level;
            _ = subsystem;
            _ = operation;
            _ = message;
            _ = exception;
        }
    }
}
