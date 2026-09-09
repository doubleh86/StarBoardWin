using System.Diagnostics;
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
    private const string LifecycleMode = "lifecycle";
    private const string TabsMode = "tabs";
    private const string Marker = "STARBOARD_CONPTY_TEST";

    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            return 2;
        }

        var mode = arguments[0];
        var resultPath = arguments[1];

        try
        {
            switch (mode)
            {
                case LifecycleMode:
                    await RunLifecycleScenarioAsync();
                    break;
                case TabsMode:
                    await RunTabsScenarioAsync();
                    break;
                default:
                    return 2;
            }

            WriteResult(resultPath, true, null, null);

            return 0;
        }
        catch (Exception exception)
        {
            WriteResult(resultPath, false, exception.GetType().Name, exception.Message);
            return 1;
        }
    }

    private static async Task RunLifecycleScenarioAsync()
    {
        var resolvedShell = ShellResolver.Resolve("cmd.exe");
        var shell = new ShellLaunchSpec(resolvedShell.ExecutablePath, "/D /Q", resolvedShell.WorkingDirectory);
        var output = new StringBuilder();
        var firstCommandReceived = CreateCompletionSource();
        var markerReceived = CreateCompletionSource();

        await using var session = ConPtySession.Start(shell, 80, 24, new NullDiagnosticLog());
        session.OutputReceived += data =>
        {
            lock (output)
            {
                output.Append(data);
                if (output.ToString().Contains("STARBOARD_DIRECTORY_SET", StringComparison.Ordinal) == true)
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

        await session.WriteAsync("cd /d \"%TEMP%\" & echo STARBOARD_DIRECTORY_SET\r\n", CancellationToken.None);
        await firstCommandReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await session.WriteAsync($"if /I \"%CD%\"==\"%TEMP%\" echo {Marker}\r\n", CancellationToken.None);

        await markerReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task RunTabsScenarioAsync()
    {
        var diagnosticLog = new NullDiagnosticLog();
        var resolvedShell = ShellResolver.Resolve("pwsh.exe");
        var shell = new ShellLaunchSpec(resolvedShell.ExecutablePath, "-NoLogo -NoProfile",
                                        resolvedShell.WorkingDirectory);
        var outputProbe = new SessionOutputProbe();
        var firstExited = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new TerminalSessionCoordinator(new ConPtySessionFactory(diagnosticLog), diagnosticLog);
        coordinator.OutputReceived += outputProbe.Receive;

        var cleanupStopwatch = new Stopwatch();
        int? secondProcessId = null;
        int? thirdProcessId = null;
        try
        {
            var first = await coordinator.StartAsync(shell, 100, 30, CancellationToken.None);
            coordinator.SessionExited += sessionExit =>
            {
                if (sessionExit.SessionId == first.SessionId)
                {
                    firstExited.TrySetResult(sessionExit.ExitCode);
                }
            };

            await WriteAndWaitAsync(coordinator, outputProbe, first.SessionId,
                                    "$env:STARBOARD_TAB_MARKER='FIRST'; Set-Location $env:TEMP; " +
                                    "Start-Job -Name StarboardJobFirst { Start-Sleep -Seconds 60 } | Out-Null; " +
                                    "$firstHistoryToken='FIRST_HISTORY_TOKEN'; " +
                                    "Write-Output ('FIRST_'+'PID:'+$PID); Write-Output ('FIRST_'+'READY')",
                                    "FIRST_READY");
            var firstProcessId = outputProbe.GetIntegerAfter(first.SessionId, "FIRST_PID:");

            var second = await coordinator.AddAsync(CancellationToken.None);
            await WriteAndWaitAsync(coordinator, outputProbe, second.SessionId,
                                    "$environmentIsolated=[string]::IsNullOrEmpty($env:STARBOARD_TAB_MARKER); " +
                                    "$jobsIsolated=@((Get-Job -ErrorAction SilentlyContinue)).Count -eq 0; " +
                                    "$historyIsolated=@((Get-History | Where-Object CommandLine -Like '*FIRST_HISTORY_TOKEN*')).Count -eq 0; " +
                                    "Write-Output ('SECOND_ENVIRONMENT_ISOLATED:'+$environmentIsolated); " +
                                    "Write-Output ('SECOND_JOBS_ISOLATED:'+$jobsIsolated); " +
                                    "Write-Output ('SECOND_HISTORY_ISOLATED:'+$historyIsolated); " +
                                    "$env:STARBOARD_TAB_MARKER='SECOND'; Set-Location $env:WINDIR; " +
                                    "Start-Job -Name StarboardJobSecond { Start-Sleep -Seconds 60 } | Out-Null; " +
                                    "$secondHistoryToken='SECOND_HISTORY_TOKEN'; " +
                                    "Write-Output ('SECOND_'+'PID:'+$PID); Write-Output ('SECOND_'+'READY')",
                                    "SECOND_READY");
            secondProcessId = outputProbe.GetIntegerAfter(second.SessionId, "SECOND_PID:");
            Ensure(firstProcessId != secondProcessId, "The two tabs unexpectedly shared one shell process.");
            outputProbe.EnsureContains(second.SessionId, "SECOND_ENVIRONMENT_ISOLATED:True");
            outputProbe.EnsureContains(second.SessionId, "SECOND_JOBS_ISOLATED:True");
            outputProbe.EnsureContains(second.SessionId, "SECOND_HISTORY_ISOLATED:True");

            var third = await coordinator.AddAsync(CancellationToken.None);
            await WriteAndWaitAsync(coordinator, outputProbe, third.SessionId,
                                    "$environmentIsolated=[string]::IsNullOrEmpty($env:STARBOARD_TAB_MARKER); " +
                                    "$jobsIsolated=@((Get-Job -ErrorAction SilentlyContinue)).Count -eq 0; " +
                                    "$historyIsolated=@((Get-History | Where-Object CommandLine " +
                                    "-Like '*HISTORY_TOKEN*')).Count -eq 0; " +
                                    "$env:STARBOARD_TAB_MARKER='THIRD'; Set-Location $env:ProgramFiles; " +
                                    "$thirdHistoryToken='THIRD_HISTORY_TOKEN'; " +
                                    "Write-Output ('THIRD_ENVIRONMENT_ISOLATED:'+$environmentIsolated); " +
                                    "Write-Output ('THIRD_JOBS_ISOLATED:'+$jobsIsolated); " +
                                    "Write-Output ('THIRD_HISTORY_ISOLATED:'+$historyIsolated); " +
                                    "Write-Output ('THIRD_'+'PID:'+$PID); Write-Output ('THIRD_'+'READY')",
                                    "THIRD_READY");
            thirdProcessId = outputProbe.GetIntegerAfter(third.SessionId, "THIRD_PID:");
            Ensure(thirdProcessId != firstProcessId && thirdProcessId != secondProcessId,
                   "The third tab unexpectedly shared a shell process.");
            outputProbe.EnsureContains(third.SessionId, "THIRD_ENVIRONMENT_ISOLATED:True");
            outputProbe.EnsureContains(third.SessionId, "THIRD_JOBS_ISOLATED:True");
            outputProbe.EnsureContains(third.SessionId, "THIRD_HISTORY_ISOLATED:True");

            Ensure(coordinator.Select(first.SessionId), "The first tab could not be selected.");
            await WriteAndWaitAsync(coordinator, outputProbe, first.SessionId,
                                    "$preserved=$env:STARBOARD_TAB_MARKER -eq 'FIRST' -and " +
                                    "(Get-Location).Path -eq (Get-Item $env:TEMP).FullName -and " +
                                    "@((Get-Job -Name StarboardJobFirst -ErrorAction SilentlyContinue)).Count -eq 1 -and " +
                                    "@((Get-History | Where-Object CommandLine -Like '*FIRST_HISTORY_TOKEN*')).Count -ge 1; " +
                                    "Write-Output ('FIRST_PRESERVED:'+$preserved)",
                                    "FIRST_PRESERVED:True");

            await coordinator.WriteActiveAsync("exit\r\n", CancellationToken.None);
            _ = await firstExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await EnsureProcessExitedAsync(firstProcessId);

            Ensure(coordinator.Select(second.SessionId), "The second tab could not be selected.");
            await AssertSecondSessionPreservedAsync(coordinator, outputProbe, second.SessionId,
                                                    "SECOND_AFTER_FIRST_EXIT");

            await coordinator.RestartAsync(first.SessionId, CancellationToken.None);
            Ensure(coordinator.Select(first.SessionId), "The restarted first tab could not be selected.");
            await WriteAndWaitAsync(coordinator, outputProbe, first.SessionId,
                                    "$fresh=[string]::IsNullOrEmpty($env:STARBOARD_TAB_MARKER) -and " +
                                    "@((Get-Job -ErrorAction SilentlyContinue)).Count -eq 0 -and " +
                                    "@((Get-History | Where-Object CommandLine -Like '*FIRST_HISTORY_TOKEN*')).Count -eq 0; " +
                                    "Write-Output ('RESTART_'+'PID:'+$PID); Write-Output ('RESTART_FRESH:'+$fresh)",
                                    "RESTART_FRESH:True");
            var restartedFirstProcessId = outputProbe.GetIntegerAfter(first.SessionId, "RESTART_PID:");
            Ensure(restartedFirstProcessId != firstProcessId &&
                   restartedFirstProcessId != secondProcessId,
                   "Restart did not create a distinct shell process.");

            Ensure(coordinator.Select(second.SessionId), "The second tab could not be reselected.");
            Ensure(await coordinator.CloseAsync(first.SessionId, CancellationToken.None),
                   "The restarted first tab could not be closed.");
            await EnsureProcessExitedAsync(restartedFirstProcessId);
            await AssertSecondSessionPreservedAsync(coordinator, outputProbe, second.SessionId,
                                                    "SECOND_AFTER_FIRST_CLOSE");
            Ensure(coordinator.Select(third.SessionId), "The third tab could not be selected.");
            await WriteAndWaitAsync(coordinator, outputProbe, third.SessionId,
                                    "$preserved=$env:STARBOARD_TAB_MARKER -eq 'THIRD' -and " +
                                    "(Get-Location).Path -eq (Get-Item $env:ProgramFiles).FullName -and " +
                                    "@((Get-History | Where-Object CommandLine -Like '*THIRD_HISTORY_TOKEN*')).Count -ge 1; " +
                                    "Write-Output ('THIRD_AFTER_FIRST_CLOSE:'+$preserved)",
                                    "THIRD_AFTER_FIRST_CLOSE:True");
        }
        finally
        {
            cleanupStopwatch.Start();
            await coordinator.DisposeAsync();
            cleanupStopwatch.Stop();
            if (secondProcessId is not null)
            {
                await EnsureProcessExitedAsync(secondProcessId.Value);
            }

            if (thirdProcessId is not null)
            {
                await EnsureProcessExitedAsync(thirdProcessId.Value);
            }
        }

        Ensure(cleanupStopwatch.Elapsed < TimeSpan.FromSeconds(8),
               "The tab workspace did not complete bounded cleanup.");
    }

    private static async Task AssertSecondSessionPreservedAsync(TerminalSessionCoordinator coordinator,
                                                                SessionOutputProbe outputProbe,
                                                                TerminalSessionId secondSessionId, string resultMarker)
    {
        await WriteAndWaitAsync(coordinator, outputProbe, secondSessionId,
                                "$preserved=$env:STARBOARD_TAB_MARKER -eq 'SECOND' -and " +
                                "(Get-Location).Path -eq (Get-Item $env:WINDIR).FullName -and " +
                                "@((Get-Job -Name StarboardJobSecond -ErrorAction SilentlyContinue)).Count -eq 1 -and " +
                                "@((Get-History | Where-Object CommandLine -Like '*SECOND_HISTORY_TOKEN*')).Count -ge 1; " +
                                $"Write-Output ('{resultMarker}:'+$preserved)",
                                $"{resultMarker}:True");
    }

    private static async Task WriteAndWaitAsync(TerminalSessionCoordinator coordinator, SessionOutputProbe outputProbe,
                                                TerminalSessionId sessionId, string command, string expectedMarker)
    {
        var markerReceived = outputProbe.WaitForAsync(sessionId, expectedMarker);
        await coordinator.WriteActiveAsync(command + "\r\n", CancellationToken.None);
        await markerReceived.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void Ensure(bool condition, string message)
    {
        if (condition == false)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task EnsureProcessExitedAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            // The process already exited before it could be opened for observation.
        }
    }

    private static void WriteResult(string resultPath, bool succeeded, string? errorType, string? errorMessage)
    {
        var json = JsonSerializer.Serialize(new TestHostResult(succeeded, errorType, errorMessage),
                                            JsonSerializerOptions.Web);
        File.WriteAllText(resultPath, json, Encoding.UTF8);
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record TestHostResult(bool Succeeded, string? ErrorType, string? ErrorMessage);

    private sealed class SessionOutputProbe
    {
        private readonly Lock outputLock = new();
        private readonly Dictionary<TerminalSessionId, StringBuilder> outputBySession = [];
        private readonly List<MarkerWaiter> waiters = [];

        internal void Receive(TerminalSessionOutput output)
        {
            lock (outputLock)
            {
                if (outputBySession.TryGetValue(output.SessionId, out var sessionOutput) == false)
                {
                    sessionOutput = new StringBuilder();
                    outputBySession.Add(output.SessionId, sessionOutput);
                }

                sessionOutput.Append(output.Data);
                foreach (var waiter in waiters)
                {
                    if (waiter.SessionId == output.SessionId &&
                        sessionOutput.ToString().Contains(waiter.Marker, StringComparison.Ordinal) == true)
                    {
                        waiter.Completion.TrySetResult();
                    }
                }
            }
        }

        internal Task WaitForAsync(TerminalSessionId sessionId, string marker)
        {
            lock (outputLock)
            {
                if (Contains(sessionId, marker) == true)
                {
                    return Task.CompletedTask;
                }

                var completion = CreateCompletionSource();
                waiters.Add(new MarkerWaiter(sessionId, marker, completion));

                return completion.Task;
            }
        }

        internal void EnsureContains(TerminalSessionId sessionId, string marker)
        {
            lock (outputLock)
            {
                Ensure(Contains(sessionId, marker), $"Session {sessionId} output did not contain marker {marker}.");
            }
        }

        internal int GetIntegerAfter(TerminalSessionId sessionId, string marker)
        {
            lock (outputLock)
            {
                if (outputBySession.TryGetValue(sessionId, out var output) == false)
                {
                    throw new InvalidOperationException($"Session {sessionId} produced no output.");
                }

                var text = output.ToString();
                var markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    throw new InvalidOperationException($"Session {sessionId} output did not contain marker {marker}.");
                }

                var valueStart = markerIndex + marker.Length;
                var valueEnd = valueStart;
                while (valueEnd < text.Length && char.IsAsciiDigit(text[valueEnd]) == true)
                {
                    valueEnd++;
                }

                if (valueEnd == valueStart ||
                    int.TryParse(text.AsSpan(valueStart, valueEnd - valueStart), out var value) == false)
                {
                    throw new InvalidOperationException($"Session {sessionId} marker {marker} had no integer value.");
                }

                return value;
            }
        }

        private bool Contains(TerminalSessionId sessionId, string marker)
        {
            return outputBySession.TryGetValue(sessionId, out var output) == true &&
                   output.ToString().Contains(marker, StringComparison.Ordinal);
        }

        private sealed record MarkerWaiter(TerminalSessionId SessionId, string Marker, TaskCompletionSource Completion);
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
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
