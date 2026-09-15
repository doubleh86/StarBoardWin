using System.Text;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class ShellCommandLifecycleIntegrationTests
{
    private static readonly ShellLaunchSpec PowerShell = new("pwsh.exe", ["-NoLogo"], Path.GetTempPath());
    private static readonly ShellLaunchSpec Cmd = new("cmd.exe", ["/Q"], Path.GetTempPath());

    [TestMethod]
    public async Task PowerShellUsesPrivateControlChannelAndIgnoresNestedPromptAsCompletion()
    {
        await using var integration = new PowerShellCommandLifecycleIntegration(
            PowerShell, new NullDiagnosticLog(), "starboard-command-test");
        var signals = new List<TerminalSessionCommandSignal>();
        integration.SignalReceived += signals.Add;

        Assert.AreEqual(PowerShell, integration.LaunchSpec);
        Assert.IsNotNull(integration.BootstrapInput);
        const string encodedPrefix = "FromBase64String('";
        var encodedStart = integration.BootstrapInput.IndexOf(encodedPrefix, StringComparison.Ordinal) +
                           encodedPrefix.Length;
        var encodedEnd = integration.BootstrapInput.IndexOf("')", encodedStart, StringComparison.Ordinal);
        var encodedCommand = integration.BootstrapInput[encodedStart..encodedEnd];
        var bootstrap = Encoding.Unicode.GetString(Convert.FromBase64String(encodedCommand));
        StringAssert.Contains(bootstrap, "NamedPipeClientStream");
        StringAssert.Contains(bootstrap, "starboard-command-test");
        Assert.AreEqual("한글 출력", integration.FilterOutput("한글 출력"));

        integration.ProcessControlMessage("P|0|0");
        integration.BeginInputWrite("Write-Output '한글'\r");
        integration.CompleteInputWrite(true);
        var commandSignals = GetCommandSignals(signals);
        var executionId = commandSignals.Single().ExecutionId;

        integration.ProcessControlMessage("P|1|0");
        Assert.HasCount(1, GetCommandSignals(signals), "A nested prompt only suspends the outer command.");

        integration.ProcessControlMessage("P|0|1");
        commandSignals = GetCommandSignals(signals);
        Assert.HasCount(2, commandSignals);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Started, commandSignals[0].Kind);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Finished, commandSignals[1].Kind);
        Assert.AreEqual(executionId, commandSignals[1].ExecutionId);
        Assert.AreEqual(1, commandSignals[1].ExitCode);
    }

    [TestMethod]
    public async Task CmdRemovesOnlyAuthenticatedPromptFramesAndDoesNotTreatOutputAsCompletion()
    {
        const string nonce = "abcdef0123456789";
        var marker = $"\u001b]633;Starboard;{nonce};Prompt\u0007";
        await using var integration = new CmdCommandLifecycleIntegration(Cmd, nonce);
        var signals = new List<TerminalSessionCommandSignal>();
        integration.SignalReceived += signals.Add;

        Assert.AreEqual(Cmd, integration.LaunchSpec);
        StringAssert.Contains(integration.BootstrapInput, nonce);
        Assert.AreEqual("banner\r\n", integration.FilterOutput($"banner\r\n{marker[..7]}"));
        Assert.AreEqual("C:\\work>", integration.FilterOutput($"{marker[7..]}C:\\work>"));
        Assert.IsEmpty(GetCommandSignals(signals),
                       "The initial prompt establishes readiness and is not a completion.");

        integration.BeginInputWrite("long-running-tool\r");
        integration.CompleteInputWrite(true);
        var commandSignals = GetCommandSignals(signals);
        var executionId = commandSignals.Single().ExecutionId;
        Assert.AreEqual("중간 출력", integration.FilterOutput("중간 출력"));
        Assert.HasCount(1, GetCommandSignals(signals), "Ordinary output must not complete the command.");

        Assert.AreEqual(string.Empty, integration.FilterOutput(marker));
        commandSignals = GetCommandSignals(signals);
        Assert.HasCount(2, commandSignals);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Finished, commandSignals[1].Kind);
        Assert.AreEqual(executionId, commandSignals[1].ExecutionId);
        Assert.IsNull(commandSignals[1].ExitCode, "cmd PROMPT cannot safely expose a dynamic ERRORLEVEL.");
    }

    [TestMethod]
    public async Task BracketedPasteAndPromptInterruptDoNotCreateFalseStarts()
    {
        const string nonce = "0123456789abcdef";
        var marker = $"\u001b]633;Starboard;{nonce};Prompt\u0007";
        await using var integration = new CmdCommandLifecycleIntegration(Cmd, nonce);
        var signals = new List<TerminalSessionCommandSignal>();
        integration.SignalReceived += signals.Add;
        _ = integration.FilterOutput(marker);

        integration.BeginInputWrite("\u0003");
        integration.CompleteInputWrite(true);
        _ = integration.FilterOutput(marker);
        integration.BeginInputWrite("\r");
        integration.CompleteInputWrite(true);
        integration.BeginInputWrite("\u001b[200~첫째\r둘째\u001b[201~");
        integration.CompleteInputWrite(true);

        Assert.IsEmpty(GetCommandSignals(signals));

        integration.BeginInputWrite("\r");
        integration.CompleteInputWrite(true);
        var commandSignals = GetCommandSignals(signals);
        Assert.HasCount(1, commandSignals);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Started, commandSignals[0].Kind);
    }

    [TestMethod]
    public async Task ControlCCompletesOnlyTheAlreadyActiveCommand()
    {
        const string nonce = "1234567890abcdef";
        var marker = $"\u001b]633;Starboard;{nonce};Prompt\u0007";
        await using var integration = new CmdCommandLifecycleIntegration(Cmd, nonce);
        var signals = new List<TerminalSessionCommandSignal>();
        integration.SignalReceived += signals.Add;
        _ = integration.FilterOutput(marker);

        integration.BeginInputWrite("long-running-tool\r");
        integration.CompleteInputWrite(true);
        integration.BeginInputWrite("\u0003");
        integration.CompleteInputWrite(true);
        _ = integration.FilterOutput(marker);

        var commandSignals = GetCommandSignals(signals);
        Assert.HasCount(2, commandSignals);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Started, commandSignals[0].Kind);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Finished, commandSignals[1].Kind);
        Assert.AreEqual(commandSignals[0].ExecutionId, commandSignals[1].ExecutionId);
    }

    [TestMethod]
    public async Task PromptObservedDuringInputWriteIsPublishedAfterItsMatchingStart()
    {
        const string nonce = "fedcba0987654321";
        var marker = $"\u001b]633;Starboard;{nonce};Prompt\u0007";
        await using var integration = new CmdCommandLifecycleIntegration(Cmd, nonce);
        var signals = new List<TerminalSessionCommandSignal>();
        integration.SignalReceived += signals.Add;
        _ = integration.FilterOutput(marker);

        integration.BeginInputWrite("fast-command\r");
        _ = integration.FilterOutput(marker);
        integration.CompleteInputWrite(true);

        var commandSignals = GetCommandSignals(signals);
        Assert.HasCount(2, commandSignals);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Started, commandSignals[0].Kind);
        Assert.AreEqual(TerminalSessionCommandSignalKind.Finished, commandSignals[1].Kind);
        Assert.AreEqual(commandSignals[0].ExecutionId, commandSignals[1].ExecutionId);
    }

    [TestMethod]
    public async Task WaitingPowerShellControlChannelDisposesWithinDeadlineAndIsIdempotent()
    {
        var integration = new PowerShellCommandLifecycleIntegration(
            PowerShell, new NullDiagnosticLog(), "starboard-command-dispose-test");
        integration.Start();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await integration.DisposeAsync();
        await integration.DisposeAsync();

        stopwatch.Stop();
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    [DataRow(TerminalShellKind.Pwsh)]
    [DataRow(TerminalShellKind.PowerShell)]
    [Timeout(15_000)]
    public async Task PowerShellBootstrapRunsOnSupportedShellAndReportsTwoPrompts(TerminalShellKind shellKind)
    {
        ShellLaunchSpec shell;
        try
        {
            shell = ShellResolver.Resolve(shellKind) with { WorkingDirectory = Path.GetTempPath() };
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive($"{shellKind} is not installed on this test machine.");
            return;
        }

        await using var integration = new PowerShellCommandLifecycleIntegration(
            shell, new NullDiagnosticLog(), $"starboard-command-runtime-{Guid.NewGuid():N}");
        var finished = new TaskCompletionSource<TerminalSessionCommandSignal>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        integration.SignalReceived += signal =>
        {
            if (signal.Kind == TerminalSessionCommandSignalKind.Ready)
            {
                integration.BeginInputWrite("runtime-check\r");
                integration.CompleteInputWrite(true);
            }
            else if (signal.Kind == TerminalSessionCommandSignalKind.Finished)
            {
                finished.TrySetResult(signal);
            }
        };
        integration.Start();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(shell.ExecutablePath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(integration.BootstrapInput +
                                           "; prompt | Out-Null; Start-Sleep -Milliseconds 100; prompt | Out-Null");
        Assert.IsTrue(process.Start());

        var finishSignal = await finished.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var standardError = await process.StandardError.ReadToEndAsync();

        Assert.AreEqual(0, process.ExitCode, standardError);
        Assert.AreEqual(0, finishSignal.ExitCode);
    }

    [TestMethod]
    public async Task IntegrationLossPermanentlySuppressesLateSignals()
    {
        await using var integration = new PowerShellCommandLifecycleIntegration(
            PowerShell, new NullDiagnosticLog(), "starboard-command-loss-test");
        var signals = new List<TerminalSessionCommandSignal>();
        integration.SignalReceived += signals.Add;

        integration.ProcessControlMessage("invalid");
        integration.ProcessControlMessage("P|0|0");
        integration.BeginInputWrite("Get-Location\r");
        integration.CompleteInputWrite(true);

        Assert.HasCount(1, signals);
        Assert.AreEqual(TerminalSessionCommandSignalKind.IntegrationLost, signals[0].Kind);
    }

    private static List<TerminalSessionCommandSignal> GetCommandSignals(
        IEnumerable<TerminalSessionCommandSignal> signals)
    {
        return signals.Where(signal => signal.Kind != TerminalSessionCommandSignalKind.Ready).ToList();
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
