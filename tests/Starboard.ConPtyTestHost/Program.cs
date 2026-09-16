using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.ConPtyTestHost;

internal static class Program
{
    private const string LifecycleMode = "lifecycle";
    private const string PowerShellBootstrapPwshMode = "powershell-bootstrap-pwsh";
    private const string PowerShellBootstrapWindowsMode = "powershell-bootstrap-windows";
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
        var progress = new TestHostProgress(resultPath);

        try
        {
            switch (mode)
            {
                case LifecycleMode:
                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
                    {
                        await RunLifecycleScenarioAsync(cancellation.Token);
                    }
                    break;
                case PowerShellBootstrapPwshMode:
                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    {
                        await RunPowerShellBootstrapScenarioAsync(TerminalShellKind.Pwsh, progress,
                                                                  cancellation.Token);
                    }
                    break;
                case PowerShellBootstrapWindowsMode:
                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    {
                        await RunPowerShellBootstrapScenarioAsync(TerminalShellKind.PowerShell, progress,
                                                                  cancellation.Token);
                    }
                    break;
                case TabsMode:
                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        await RunTabsScenarioAsync(progress, cancellation.Token);
                    }
                    break;
                default:
                    return 2;
            }

            progress.Complete();

            return 0;
        }
        catch (Exception exception)
        {
            progress.Fail(exception);
            return 1;
        }
    }

    private static async Task RunLifecycleScenarioAsync(CancellationToken cancellationToken)
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

        await session.WriteAsync("cd /d \"%TEMP%\" & echo STARBOARD_DIRECTORY_SET\r\n", cancellationToken);
        await WaitForSignalAsync(firstCommandReceived.Task, "The first lifecycle command", cancellationToken);
        await session.WriteAsync($"if /I \"%CD%\"==\"%TEMP%\" echo {Marker}\r\n", cancellationToken);

        await WaitForSignalAsync(markerReceived.Task, "The lifecycle round-trip marker", cancellationToken);
    }

    private static async Task RunPowerShellBootstrapScenarioAsync(TerminalShellKind shellKind,
                                                                  TestHostProgress progress,
                                                                  CancellationToken cancellationToken)
    {
        const string profileOutput = "STARBOARD_PROFILE_OUTPUT";
        const string profilePrompt = "STARBOARD_PROFILE_PROMPT> ";
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var temporaryProfileRoot = Path.Combine(Path.GetTempPath(), $"starboard-conpty-profile-{Guid.NewGuid():N}");
        var profileDirectoryName = shellKind == TerminalShellKind.Pwsh ? "PowerShell" : "WindowsPowerShell";
        var profileDirectory = Path.Combine(temporaryProfileRoot, "Documents", profileDirectoryName);
        var profilePath = Path.Combine(profileDirectory, "Microsoft.PowerShell_profile.ps1");

        Directory.CreateDirectory(profileDirectory);
        await File.WriteAllTextAsync(profilePath,
                                     $"Start-Sleep -Milliseconds 250{Environment.NewLine}" +
                                     $"Write-Output '{profileOutput}'{Environment.NewLine}" +
                                     $"function global:prompt {{ '{profilePrompt}' }}{Environment.NewLine}",
                                     new UTF8Encoding(false), cancellationToken);
        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", temporaryProfileRoot);
            Environment.SetEnvironmentVariable("HOME", temporaryProfileRoot);
            var shell = ShellResolver.Resolve(shellKind) with { WorkingDirectory = Path.GetTempPath() };
            var output = new StringBuilder();
            var nestedPromptReady = CreateCompletionSource();
            var signalProbe = new CommandSignalProbe();

            await using var session = ConPtySession.Start(shell, 100, 30, new NullDiagnosticLog());
            session.OutputReceived += data =>
            {
                lock (output)
                {
                    output.Append(data);
                    if (CountOccurrences(output, profilePrompt) >= 2)
                    {
                        nestedPromptReady.TrySetResult();
                    }
                }
            };
            session.CommandLifecycleChanged += signalProbe.Receive;
            session.BeginReading();
            progress.MarkStage("powershell-session-started");

            await session.WriteAsync("Write-Output 'STARBOARD_BOOTSTRAP_FIRST'; $host.EnterNestedPrompt()\r",
                                     cancellationToken);
            progress.MarkStage("powershell-first-input-written");
            await WaitForSignalAsync(nestedPromptReady.Task, "The nested PowerShell prompt", cancellationToken);
            await session.WriteAsync("exit\r", cancellationToken);
            var firstFinished = await WaitForSignalAsync(signalProbe.FirstFinished,
                                                         "The first PowerShell completion signal", cancellationToken);
            progress.MarkStage("powershell-first-command-finished");
            await session.WriteAsync("Write-Error 'STARBOARD_EXPECTED_FAILURE' -ErrorAction SilentlyContinue\r",
                                     cancellationToken);
            progress.MarkStage("powershell-second-input-written");
            var secondFinished = await WaitForSignalAsync(signalProbe.SecondFinished,
                                                          "The second PowerShell completion signal", cancellationToken);
            progress.MarkStage("powershell-second-command-finished");

            var signals = signalProbe.Snapshot();
            Ensure(signals.Count(signal => signal.Kind == TerminalSessionCommandSignalKind.Ready) == 1,
                   "The PowerShell bootstrap did not publish exactly one initial ready signal.");
            Ensure(signals.Count(signal => signal.Kind == TerminalSessionCommandSignalKind.Started) == 2,
                   "The PowerShell session did not publish exactly two command starts.");
            Ensure(signals.Count(signal => signal.Kind == TerminalSessionCommandSignalKind.Finished) == 2,
                   "The PowerShell session did not publish exactly two command finishes.");
            Ensure(signals.Any(signal => signal.Kind == TerminalSessionCommandSignalKind.IntegrationLost) == false,
                   "The PowerShell command lifecycle integration became unavailable.");
            Ensure(firstFinished.ExitCode == 0, "The first PowerShell command did not report success.");
            Ensure(secondFinished.ExitCode == 1, "The second PowerShell command did not report failure.");

            string visibleOutput;
            lock (output)
            {
                visibleOutput = output.ToString();
            }

            Ensure(visibleOutput.Contains(profileOutput, StringComparison.Ordinal),
                   "The PowerShell profile output was not preserved.");
            Ensure(CountOccurrences(visibleOutput, profilePrompt) >= 3,
                   "The PowerShell profile prompt was not preserved across nested and completed commands.");
            Ensure(visibleOutput.Contains("STARBOARD_BOOTSTRAP_FIRST", StringComparison.Ordinal),
                   "The first PowerShell command did not reach the interactive ConPTY session.");
            Ensure(visibleOutput.Contains(">>", StringComparison.Ordinal) == false,
                   "PowerShell remained in a continuation prompt after bootstrap.");
            Ensure(visibleOutput.Contains("__StarboardCommand", StringComparison.Ordinal) == false &&
                   visibleOutput.Contains("NamedPipeClientStream", StringComparison.Ordinal) == false &&
                   visibleOutput.Contains("NamedPipeServerStream", StringComparison.Ordinal) == false &&
                   visibleOutput.Contains("Invoke-Expression", StringComparison.Ordinal) == false,
                   "The PowerShell bootstrap implementation was exposed in terminal output.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            if (Directory.Exists(temporaryProfileRoot) == true)
            {
                Directory.Delete(temporaryProfileRoot, true);
            }
        }
    }

    private static int CountOccurrences(StringBuilder value, string marker)
    {
        return CountOccurrences(value.ToString(), marker);
    }

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var markerIndex = value.IndexOf(marker, startIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return count;
            }

            count++;
            startIndex = markerIndex + marker.Length;
        }

        return count;
    }

    private static async Task RunTabsScenarioAsync(TestHostProgress progress, CancellationToken cancellationToken)
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
            var first = await coordinator.StartAsync(shell, 100, 30, cancellationToken);
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
                                    "FIRST_READY", cancellationToken);
            var firstProcessId = outputProbe.GetIntegerAfter(first.SessionId, "FIRST_PID:");

            var second = await coordinator.AddAsync(cancellationToken);
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
                                    "SECOND_READY", cancellationToken);
            secondProcessId = outputProbe.GetIntegerAfter(second.SessionId, "SECOND_PID:");
            Ensure(firstProcessId != secondProcessId, "The two tabs unexpectedly shared one shell process.");
            outputProbe.EnsureContains(second.SessionId, "SECOND_ENVIRONMENT_ISOLATED:True");
            outputProbe.EnsureContains(second.SessionId, "SECOND_JOBS_ISOLATED:True");
            outputProbe.EnsureContains(second.SessionId, "SECOND_HISTORY_ISOLATED:True");

            var third = await coordinator.AddAsync(cancellationToken);
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
                                    "THIRD_READY", cancellationToken);
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
                                    "FIRST_PRESERVED:True", cancellationToken);

            await coordinator.WriteActiveAsync("exit\r\n", cancellationToken);
            _ = await WaitForSignalAsync(firstExited.Task, "The original first session exit", cancellationToken);
            await EnsureProcessExitedAsync(firstProcessId, cancellationToken);

            Ensure(coordinator.Select(second.SessionId), "The second tab could not be selected.");
            await AssertSecondSessionPreservedAsync(coordinator, outputProbe, second.SessionId,
                                                    "SECOND_AFTER_FIRST_EXIT", cancellationToken);

            await coordinator.RestartAsync(first.SessionId, cancellationToken);
            Ensure(coordinator.Select(first.SessionId), "The restarted first tab could not be selected.");
            await WriteAndWaitAsync(coordinator, outputProbe, first.SessionId,
                                    "$fresh=[string]::IsNullOrEmpty($env:STARBOARD_TAB_MARKER) -and " +
                                    "@((Get-Job -ErrorAction SilentlyContinue)).Count -eq 0 -and " +
                                    "@((Get-History | Where-Object CommandLine -Like '*FIRST_HISTORY_TOKEN*')).Count -eq 0; " +
                                    "Write-Output ('RESTART_'+'PID:'+$PID); Write-Output ('RESTART_FRESH:'+$fresh)",
                                    "RESTART_FRESH:True", cancellationToken);
            var restartedFirstProcessId = outputProbe.GetIntegerAfter(first.SessionId, "RESTART_PID:");
            Ensure(restartedFirstProcessId != firstProcessId &&
                   restartedFirstProcessId != secondProcessId,
                   "Restart did not create a distinct shell process.");

            Ensure(coordinator.Select(second.SessionId), "The second tab could not be reselected.");
            var closingSession = coordinator.NewOutputState.States
                .Single(state => state.Session.SessionId == first.SessionId.Value)
                .Session;
            progress.BeginRestartedSessionClose(closingSession, restartedFirstProcessId);
            var closeRequest = coordinator.RequestCloseConfirmation(first.SessionId);
            Ensure(closeRequest is not null, "The restarted first tab did not produce a close confirmation.");
            Ensure(closeRequest!.Token.Session == closingSession,
                   "The close confirmation did not target the restarted session generation.");

            var tabRemoved = WaitForTabRemovalAsync(coordinator, closingSession, progress, cancellationToken);
            var processExited = ObserveProcessExitAsync(restartedFirstProcessId, progress, cancellationToken);
            var closeResponse = new TerminalConfirmationResponse(closeRequest.Token,
                                                                 TerminalConfirmationResult.Confirmed);
            var closed = await coordinator.ApplyCloseConfirmationAsync(closeResponse, cancellationToken);
            Ensure(closed, "The restarted first tab close confirmation was not applied.");
            await WaitForSignalAsync(tabRemoved, "The restarted first tab removal", cancellationToken);
            await WaitForSignalAsync(processExited, "The restarted first process exit", cancellationToken);
            Ensure(coordinator.Snapshot.Tabs.All(tab => tab.SessionId != first.SessionId),
                   "The restarted first tab remained in the workspace after close completed.");
            progress.CompleteRestartedSessionClose();
            await AssertSecondSessionPreservedAsync(coordinator, outputProbe, second.SessionId,
                                                    "SECOND_AFTER_FIRST_CLOSE", cancellationToken);
            Ensure(coordinator.Select(third.SessionId), "The third tab could not be selected.");
            await WriteAndWaitAsync(coordinator, outputProbe, third.SessionId,
                                    "$preserved=$env:STARBOARD_TAB_MARKER -eq 'THIRD' -and " +
                                    "(Get-Location).Path -eq (Get-Item $env:ProgramFiles).FullName -and " +
                                    "@((Get-History | Where-Object CommandLine -Like '*THIRD_HISTORY_TOKEN*')).Count -ge 1; " +
                                    "Write-Output ('THIRD_AFTER_FIRST_CLOSE:'+$preserved)",
                                    "THIRD_AFTER_FIRST_CLOSE:True", cancellationToken);
        }
        finally
        {
            cleanupStopwatch.Start();
            await coordinator.DisposeAsync();
            cleanupStopwatch.Stop();
            if (secondProcessId is not null)
            {
                await EnsureProcessExitedAsync(secondProcessId.Value, CancellationToken.None);
            }

            if (thirdProcessId is not null)
            {
                await EnsureProcessExitedAsync(thirdProcessId.Value, CancellationToken.None);
            }
        }

        Ensure(cleanupStopwatch.Elapsed < TimeSpan.FromSeconds(8),
               "The tab workspace did not complete bounded cleanup.");
    }

    private static async Task AssertSecondSessionPreservedAsync(TerminalSessionCoordinator coordinator,
                                                                SessionOutputProbe outputProbe,
                                                                TerminalSessionId secondSessionId, string resultMarker,
                                                                CancellationToken cancellationToken)
    {
        await WriteAndWaitAsync(coordinator, outputProbe, secondSessionId,
                                "$preserved=$env:STARBOARD_TAB_MARKER -eq 'SECOND' -and " +
                                "(Get-Location).Path -eq (Get-Item $env:WINDIR).FullName -and " +
                                "@((Get-Job -Name StarboardJobSecond -ErrorAction SilentlyContinue)).Count -eq 1 -and " +
                                "@((Get-History | Where-Object CommandLine -Like '*SECOND_HISTORY_TOKEN*')).Count -ge 1; " +
                                $"Write-Output ('{resultMarker}:'+$preserved)",
                                $"{resultMarker}:True", cancellationToken);
    }

    private static async Task WriteAndWaitAsync(TerminalSessionCoordinator coordinator, SessionOutputProbe outputProbe,
                                                TerminalSessionId sessionId, string command, string expectedMarker,
                                                CancellationToken cancellationToken)
    {
        var markerReceived = outputProbe.WaitForAsync(sessionId, expectedMarker);
        await coordinator.WriteAsync(sessionId, command + "\r\n", cancellationToken);
        await WaitForSignalAsync(markerReceived, $"Session {sessionId} marker {expectedMarker}", cancellationToken);
    }

    private static void Ensure(bool condition, string message)
    {
        if (condition == false)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task EnsureProcessExitedAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await WaitForSignalAsync(process.WaitForExitAsync(cancellationToken), $"Process {processId} exit",
                                     cancellationToken, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            // The process already exited before it could be opened for observation.
        }
    }

    private static async Task WaitForTabRemovalAsync(TerminalSessionCoordinator coordinator,
                                                     TerminalSessionReference session,
                                                     TestHostProgress progress,
                                                     CancellationToken cancellationToken)
    {
        var completion = CreateCompletionSource();
        void Observe(TerminalWorkspaceSnapshot snapshot)
        {
            if (snapshot.Tabs.Any(tab => tab.SessionId.Value == session.SessionId) == false)
            {
                progress.MarkTabRemoved();
                completion.TrySetResult();
            }
        }

        coordinator.WorkspaceChanged += Observe;
        try
        {
            Observe(coordinator.Snapshot);
            await WaitForSignalAsync(completion.Task, "The restarted first tab removal", cancellationToken);
        }
        finally
        {
            coordinator.WorkspaceChanged -= Observe;
        }
    }

    private static async Task ObserveProcessExitAsync(int processId, TestHostProgress progress,
                                                      CancellationToken cancellationToken)
    {
        await EnsureProcessExitedAsync(processId, cancellationToken);
        progress.MarkProcessExited();
    }

    private static async Task<T> WaitForSignalAsync<T>(Task<T> task, string description,
                                                       CancellationToken cancellationToken,
                                                       TimeSpan? timeout = null)
    {
        await WaitForSignalAsync((Task)task, description, cancellationToken, timeout);
        return await task;
    }

    private static async Task WaitForSignalAsync(Task task, string description, CancellationToken cancellationToken,
                                                 TimeSpan? timeout = null)
    {
        var signalTimeout = timeout ?? TimeSpan.FromSeconds(10);
        try
        {
            await task.WaitAsync(signalTimeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{description} did not complete within {signalTimeout}.", exception);
        }
    }

    private static TaskCompletionSource CreateCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record TestHostResult(bool Succeeded, string? ErrorType, string? ErrorMessage,
                                         TestHostObservation Observation);

    private sealed record TestHostObservation(int HostProcessId, string HostExecutablePath, string Stage,
                                              Guid? SessionId, long? SessionGeneration, int? ShellProcessId,
                                              bool TabRemoved, bool ProcessExited);

    private sealed class TestHostProgress
    {
        private readonly Lock _progressLock = new();
        private readonly string _resultPath;
        private TestHostObservation _observation;
        private bool _isFinal;

        internal TestHostProgress(string resultPath)
        {
            _resultPath = resultPath;
            _observation = new TestHostObservation(Environment.ProcessId, Environment.ProcessPath ?? string.Empty,
                                                   "starting", null, null, null, false, false);
            Write(false, "InProgress", null);
        }

        internal void BeginRestartedSessionClose(TerminalSessionReference session, int processId)
        {
            lock (_progressLock)
            {
                _observation = _observation with
                {
                    Stage = "restarted-session-close-requested",
                    SessionId = session.SessionId,
                    SessionGeneration = session.Generation,
                    ShellProcessId = processId,
                    TabRemoved = false,
                    ProcessExited = false,
                };
                WriteLocked(false, "InProgress", null);
            }
        }

        internal void MarkTabRemoved()
        {
            UpdateCondition(tabRemoved: true, processExited: null);
        }

        internal void MarkProcessExited()
        {
            UpdateCondition(tabRemoved: null, processExited: true);
        }

        internal void CompleteRestartedSessionClose()
        {
            lock (_progressLock)
            {
                _observation = _observation with { Stage = "restarted-session-close-complete" };
                WriteLocked(false, "InProgress", null);
            }
        }

        internal void MarkStage(string stage)
        {
            lock (_progressLock)
            {
                _observation = _observation with { Stage = stage };
                WriteLocked(false, "InProgress", null);
            }
        }

        internal void Complete()
        {
            lock (_progressLock)
            {
                _isFinal = true;
                _observation = _observation with { Stage = "complete" };
                WriteLocked(true, null, null);
            }
        }

        internal void Fail(Exception exception)
        {
            lock (_progressLock)
            {
                _isFinal = true;
                WriteLocked(false, exception.GetType().Name, exception.Message);
            }
        }

        private void UpdateCondition(bool? tabRemoved, bool? processExited)
        {
            lock (_progressLock)
            {
                if (_isFinal == true)
                {
                    return;
                }

                _observation = _observation with
                {
                    TabRemoved = tabRemoved ?? _observation.TabRemoved,
                    ProcessExited = processExited ?? _observation.ProcessExited,
                };
                WriteLocked(false, "InProgress", null);
            }
        }

        private void Write(bool succeeded, string? errorType, string? errorMessage)
        {
            lock (_progressLock)
            {
                WriteLocked(succeeded, errorType, errorMessage);
            }
        }

        private void WriteLocked(bool succeeded, string? errorType, string? errorMessage)
        {
            var result = new TestHostResult(succeeded, errorType, errorMessage, _observation);
            var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
            var temporaryPath = $"{_resultPath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temporaryPath, json, Encoding.UTF8);
            File.Move(temporaryPath, _resultPath, true);
        }
    }

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

    private sealed class CommandSignalProbe
    {
        private readonly Lock signalLock = new();
        private readonly List<TerminalSessionCommandSignal> signals = [];
        private readonly TaskCompletionSource<TerminalSessionCommandSignal> firstFinished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<TerminalSessionCommandSignal> secondFinished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int finishedCount;

        internal Task<TerminalSessionCommandSignal> FirstFinished => firstFinished.Task;

        internal Task<TerminalSessionCommandSignal> SecondFinished => secondFinished.Task;

        internal void Receive(TerminalSessionCommandSignal signal)
        {
            lock (signalLock)
            {
                signals.Add(signal);
                if (signal.Kind != TerminalSessionCommandSignalKind.Finished)
                {
                    return;
                }

                finishedCount++;
                if (finishedCount == 1)
                {
                    firstFinished.TrySetResult(signal);
                }
                else if (finishedCount == 2)
                {
                    secondFinished.TrySetResult(signal);
                }
            }
        }

        internal TerminalSessionCommandSignal[] Snapshot()
        {
            lock (signalLock)
            {
                return signals.ToArray();
            }
        }
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
