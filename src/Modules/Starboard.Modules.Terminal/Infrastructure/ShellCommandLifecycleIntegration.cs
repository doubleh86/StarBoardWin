using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Infrastructure;

internal interface IShellCommandLifecycleIntegration : IAsyncDisposable
{
    event Action<TerminalSessionCommandSignal>? SignalReceived;

    ShellLaunchSpec LaunchSpec { get; }

    string? BootstrapInput { get; }

    void Start();

    void BeginInputWrite(string data);

    void CompleteInputWrite(bool succeeded);

    string FilterOutput(string data);

    void NotifySessionExited();
}

internal static class ShellCommandLifecycleIntegrationFactory
{
    internal static IShellCommandLifecycleIntegration Create(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog)
    {
        var fileName = Path.GetFileName(shell.ExecutablePath);
        if (fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) == true ||
            fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new PowerShellCommandLifecycleIntegration(shell, diagnosticLog);
        }

        if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new CmdCommandLifecycleIntegration(shell);
        }

        return new NullShellCommandLifecycleIntegration(shell);
    }
}

internal abstract class ShellCommandLifecycleIntegration : IShellCommandLifecycleIntegration
{
    private const string BracketedPasteStart = "\u001b[200~";
    private const string BracketedPasteEnd = "\u001b[201~";

    private readonly Lock stateLock = new();
    private TerminalCommandExecutionId? activeExecutionId;
    private int? queuedFinishExitCode;
    private bool hasQueuedFinish;
    private bool isReady;
    private bool isBracketedPaste;
    private bool isLineStateUnknown;
    private bool isInputWritePending;
    private bool pendingSubmission;
    private bool isAvailable = true;
    private bool isDisposed;
    private int typedCharacterCount;

    protected ShellCommandLifecycleIntegration(ShellLaunchSpec launchSpec)
    {
        LaunchSpec = launchSpec;
    }

    public event Action<TerminalSessionCommandSignal>? SignalReceived;

    public ShellLaunchSpec LaunchSpec { get; }

    public virtual string? BootstrapInput => null;

    public virtual void Start()
    {
    }

    public void BeginInputWrite(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (isInputWritePending == true)
            {
                throw new InvalidOperationException("A shell integration input write is already pending.");
            }

            isInputWritePending = true;
            pendingSubmission = isAvailable == true && isReady == true && ContainsCommandSubmission(data);
            queuedFinishExitCode = null;
            hasQueuedFinish = false;
        }
    }

    public void CompleteInputWrite(bool succeeded)
    {
        TerminalSessionCommandSignal? startedSignal = null;
        TerminalSessionCommandSignal? finishedSignal = null;
        TerminalSessionCommandSignal? lostSignal = null;

        lock (stateLock)
        {
            if (isInputWritePending == false)
            {
                return;
            }

            isInputWritePending = false;
            if (succeeded == false)
            {
                pendingSubmission = false;
                queuedFinishExitCode = null;
                hasQueuedFinish = false;
                if (isAvailable == true)
                {
                    MarkLostLocked();
                    lostSignal = TerminalSessionCommandSignal.IntegrationLost();
                }
            }
            else if (pendingSubmission == true && isAvailable == true)
            {
                var executionId = TerminalCommandExecutionId.CreateNew();
                activeExecutionId = executionId;
                isReady = false;
                pendingSubmission = false;
                startedSignal = TerminalSessionCommandSignal.Started(executionId);

                if (hasQueuedFinish == true)
                {
                    finishedSignal = TerminalSessionCommandSignal.Finished(executionId, queuedFinishExitCode);
                    activeExecutionId = null;
                    queuedFinishExitCode = null;
                    hasQueuedFinish = false;
                    isReady = true;
                    isBracketedPaste = false;
                }
            }
        }

        Raise(startedSignal);
        Raise(finishedSignal);
        Raise(lostSignal);
    }

    public virtual string FilterOutput(string data)
    {
        return data;
    }

    public void NotifySessionExited()
    {
        var shouldRaise = false;
        lock (stateLock)
        {
            if (isDisposed == false && isAvailable == true)
            {
                MarkLostLocked();
                shouldRaise = true;
            }
        }

        if (shouldRaise == true)
        {
            SignalReceived?.Invoke(TerminalSessionCommandSignal.IntegrationLost());
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        lock (stateLock)
        {
            if (isDisposed == true)
            {
                return ValueTask.CompletedTask;
            }

            isDisposed = true;
            if (isAvailable == true)
            {
                MarkLostLocked();
            }
        }

        return ValueTask.CompletedTask;
    }

    protected void ObservePrompt(int? exitCode)
    {
        TerminalSessionCommandSignal? signal = null;
        lock (stateLock)
        {
            if (isDisposed == true || isAvailable == false)
            {
                return;
            }

            isBracketedPaste = false;
            isLineStateUnknown = false;
            typedCharacterCount = 0;
            if (activeExecutionId is { } executionId)
            {
                activeExecutionId = null;
                isReady = true;
                signal = TerminalSessionCommandSignal.Finished(executionId, exitCode);
            }
            else if (isInputWritePending == true && pendingSubmission == true)
            {
                queuedFinishExitCode = exitCode;
                hasQueuedFinish = true;
            }
            else
            {
                isReady = true;
                signal = TerminalSessionCommandSignal.Ready();
            }
        }

        Raise(signal);
    }

    protected void ObserveIntegrationLost()
    {
        NotifySessionExited();
    }

    private bool ContainsCommandSubmission(string data)
    {
        for (var index = 0; index < data.Length; index++)
        {
            if (MatchesAt(data, index, BracketedPasteStart) == true)
            {
                isBracketedPaste = true;
                index += BracketedPasteStart.Length - 1;
                continue;
            }

            if (MatchesAt(data, index, BracketedPasteEnd) == true)
            {
                isBracketedPaste = false;
                index += BracketedPasteEnd.Length - 1;
                continue;
            }

            if (data[index] == '\u0003' || data[index] == '\u0015')
            {
                isLineStateUnknown = false;
                typedCharacterCount = 0;
                continue;
            }

            if (data[index] == '\r' && isBracketedPaste == false)
            {
                var hasCommand = typedCharacterCount > 0 || isLineStateUnknown == true;
                isLineStateUnknown = false;
                typedCharacterCount = 0;
                return hasCommand;
            }

            if (data[index] == '\b' || data[index] == '\u007f')
            {
                if (typedCharacterCount > 0)
                {
                    typedCharacterCount--;
                }

                continue;
            }

            if (data[index] == '\u001b')
            {
                isLineStateUnknown = true;
                continue;
            }

            if (data[index] >= ' ' && data[index] != '\u007f')
            {
                typedCharacterCount++;
            }
        }

        return false;
    }

    private static bool MatchesAt(string value, int index, string candidate)
    {
        return index + candidate.Length <= value.Length &&
               value.AsSpan(index, candidate.Length).SequenceEqual(candidate);
    }

    private void MarkLostLocked()
    {
        activeExecutionId = null;
        queuedFinishExitCode = null;
        hasQueuedFinish = false;
        pendingSubmission = false;
        isReady = false;
        isBracketedPaste = false;
        isLineStateUnknown = false;
        typedCharacterCount = 0;
        isAvailable = false;
    }

    private void Raise(TerminalSessionCommandSignal? signal)
    {
        if (signal.HasValue == true)
        {
            SignalReceived?.Invoke(signal.Value);
        }
    }
}

internal sealed class PowerShellCommandLifecycleIntegration : ShellCommandLifecycleIntegration
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly IDiagnosticLog diagnosticLog;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly NamedPipeServerStream pipeServer;
    private Task? listenerTask;
    private int disposeStarted;

    internal PowerShellCommandLifecycleIntegration(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog)
        : this(shell, diagnosticLog, $"starboard-command-{Guid.NewGuid():N}")
    {
    }

    internal PowerShellCommandLifecycleIntegration(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog,
                                                   string pipeName)
        : base(shell)
    {
        this.diagnosticLog = diagnosticLog;
        BootstrapInput = CreateBootstrapInput(pipeName);
        pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                                               PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public override string BootstrapInput { get; }

    public override void Start()
    {
        listenerTask ??= ListenAsync(lifetimeCancellation.Token);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        pipeServer.Dispose();

        if (listenerTask is not null)
        {
            try
            {
                await listenerTask.WaitAsync(DisposeTimeout).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or ObjectDisposedException or TimeoutException or IOException)
            {
                // The pipe handle is already closed and session shutdown remains bounded.
            }
        }

        lifetimeCancellation.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    internal void ProcessControlMessage(string message)
    {
        var fields = message.Split('|');
        if (fields.Length != 3 || fields[0] != "P" ||
            int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var nestedPromptLevel) == false ||
            int.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exitCode) == false)
        {
            ObserveIntegrationLost();
            return;
        }

        // A nested PowerShell prompt suspends the outer command. Only its return to level zero proves completion.
        if (nestedPromptLevel == 0)
        {
            ObservePrompt(exitCode);
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(pipeServer, new UTF8Encoding(false, false), false, 1024, true);
            while (cancellationToken.IsCancellationRequested == false)
            {
                var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    ObserveIntegrationLost();
                    return;
                }

                ProcessControlMessage(message);
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
            if (cancellationToken.IsCancellationRequested == false)
            {
                diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "CommandLifecyclePipe",
                                    "The PowerShell command lifecycle channel ended unexpectedly.", exception);
                ObserveIntegrationLost();
            }
        }
    }

    private static string CreateBootstrapInput(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) == true)
        {
            throw new ArgumentException("A command lifecycle pipe name is required.", nameof(pipeName));
        }

        var script = CreateBootstrapScript(pipeName);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return "$starboardBootstrap=[System.Text.Encoding]::Unicode.GetString(" +
               $"[System.Convert]::FromBase64String('{encodedScript}')); " +
               "Invoke-Expression $starboardBootstrap; Remove-Variable starboardBootstrap -ErrorAction Ignore; " +
               "[Console]::Write(([char]27).ToString()+'[3J'+([char]27)+'[2J'+([char]27)+'[H')\r\n";
    }

    private static string CreateBootstrapScript(string pipeName)
    {
        return $$"""
            $global:__StarboardOriginalPrompt = ${function:prompt}
            try {
                $global:__StarboardCommandPipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{pipeName}}', [System.IO.Pipes.PipeDirection]::Out, [System.IO.Pipes.PipeOptions]::Asynchronous)
                $global:__StarboardCommandPipe.Connect(5000)
                $global:__StarboardCommandWriter = [System.IO.StreamWriter]::new($global:__StarboardCommandPipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
                $global:__StarboardCommandWriter.AutoFlush = $true
            } catch {
                $global:__StarboardCommandWriter = $null
                if ($null -ne $global:__StarboardCommandPipe) { $global:__StarboardCommandPipe.Dispose() }
                $global:__StarboardCommandPipe = $null
            }
            function global:prompt {
                $starboardSucceeded = $?
                $starboardDepthVariable = Get-Variable -Name NestedPromptLevel -ErrorAction Ignore
                $starboardDepth = if ($null -eq $starboardDepthVariable) { 0 } else { [int]$starboardDepthVariable.Value }
                $starboardExitCode = if ($starboardSucceeded) { 0 } else { 1 }
                if ($null -ne $global:__StarboardCommandWriter) {
                    try { $global:__StarboardCommandWriter.WriteLine(('P|{0}|{1}' -f $starboardDepth, $starboardExitCode)) }
                    catch {
                        $global:__StarboardCommandWriter.Dispose()
                        $global:__StarboardCommandWriter = $null
                    }
                }
                if ($null -ne $global:__StarboardOriginalPrompt) { & $global:__StarboardOriginalPrompt }
                else { "PS $($executionContext.SessionState.Path.CurrentLocation)> " }
            }
            """;
    }
}

internal sealed class CmdCommandLifecycleIntegration : ShellCommandLifecycleIntegration
{
    private readonly string promptMarker;
    private string pendingOutput = string.Empty;

    internal CmdCommandLifecycleIntegration(ShellLaunchSpec shell)
        : this(shell, Guid.NewGuid().ToString("N"))
    {
    }

    internal CmdCommandLifecycleIntegration(ShellLaunchSpec shell, string nonce)
        : base(shell)
    {
        ValidateNonce(nonce);
        promptMarker = $"\u001b]633;Starboard;{nonce};Prompt\u0007";
        BootstrapInput = $"prompt {promptMarker}$P$G & cls\r\n";
    }

    public override string BootstrapInput { get; }

    public override string FilterOutput(string data)
    {
        if (data.Length == 0)
        {
            return data;
        }

        pendingOutput += data;
        var visible = new StringBuilder(pendingOutput.Length);
        while (true)
        {
            var markerIndex = pendingOutput.IndexOf(promptMarker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                visible.Append(pendingOutput, 0, markerIndex);
                pendingOutput = pendingOutput[(markerIndex + promptMarker.Length)..];
                ObservePrompt(null);
                continue;
            }

            var retainedLength = GetRetainedPrefixLength(pendingOutput, promptMarker);
            var visibleLength = pendingOutput.Length - retainedLength;
            visible.Append(pendingOutput, 0, visibleLength);
            pendingOutput = pendingOutput[visibleLength..];
            return visible.ToString();
        }
    }

    private static void ValidateNonce(string nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce) == true || nonce.Any(character => char.IsAsciiLetterOrDigit(character) == false))
        {
            throw new ArgumentException("A command prompt integration nonce must be alphanumeric.", nameof(nonce));
        }
    }

    private static int GetRetainedPrefixLength(string value, string marker)
    {
        var maximumLength = Math.Min(value.Length, marker.Length - 1);
        for (var length = maximumLength; length > 0; length--)
        {
            if (value.AsSpan(value.Length - length).SequenceEqual(marker.AsSpan(0, length)) == true)
            {
                return length;
            }
        }

        return 0;
    }
}

internal sealed class NullShellCommandLifecycleIntegration : ShellCommandLifecycleIntegration
{
    internal NullShellCommandLifecycleIntegration(ShellLaunchSpec shell)
        : base(shell)
    {
    }
}
