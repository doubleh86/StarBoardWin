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

    ValueTask WaitForInputReadyAsync(CancellationToken cancellationToken);

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

    public virtual ValueTask WaitForInputReadyAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return ValueTask.CompletedTask;
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

    public virtual void NotifySessionExited()
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
    private static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(5);

    private readonly IDiagnosticLog diagnosticLog;
    private readonly TimeSpan readinessTimeout;
    private readonly string controlNonce;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly NamedPipeClientStream pipeClient;
    private readonly TaskCompletionSource inputReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? listenerTask;
    private int disposeStarted;
    private int pipeConnected;
    private int readinessFailureReported;

    internal PowerShellCommandLifecycleIntegration(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog)
        : this(shell, diagnosticLog, $"LOCAL\\starboard-command-{Guid.NewGuid():N}",
               Guid.NewGuid().ToString("N"), DefaultReadinessTimeout)
    {
    }

    internal PowerShellCommandLifecycleIntegration(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog,
                                                   string pipeName)
        : this(shell, diagnosticLog, pipeName, Guid.NewGuid().ToString("N"), DefaultReadinessTimeout)
    {
    }

    internal PowerShellCommandLifecycleIntegration(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog,
                                                   string pipeName, TimeSpan readinessTimeout)
        : this(shell, diagnosticLog, pipeName, Guid.NewGuid().ToString("N"), readinessTimeout)
    {
    }

    internal PowerShellCommandLifecycleIntegration(ShellLaunchSpec shell, IDiagnosticLog diagnosticLog,
                                                   string pipeName, string controlNonce,
                                                   TimeSpan readinessTimeout)
        : base(CreateLaunchSpec(shell, pipeName, controlNonce))
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(readinessTimeout, TimeSpan.Zero);
        this.diagnosticLog = diagnosticLog;
        this.readinessTimeout = readinessTimeout;
        this.controlNonce = controlNonce;
        pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
    }

    public override void Start()
    {
        listenerTask ??= ListenAsync(lifetimeCancellation.Token);
    }

    public override async ValueTask WaitForInputReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await inputReady.Task.WaitAsync(readinessTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            var operation = Volatile.Read(ref pipeConnected) == 0
                ? "PowerShellBootstrapConnection"
                : "PowerShellPromptReadiness";
            DisableIntegration(operation,
                               "The PowerShell command lifecycle bootstrap did not reach an input-ready prompt.",
                               exception);
        }
    }

    public override void NotifySessionExited()
    {
        inputReady.TrySetResult();
        base.NotifySessionExited();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        inputReady.TrySetResult();
        pipeClient.Dispose();

        if (listenerTask is not null)
        {
            try
            {
                await listenerTask.WaitAsync(DisposeTimeout).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or ObjectDisposedException or TimeoutException or IOException or
                    UnauthorizedAccessException)
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
        if (fields.Length != 4 || fields[0] != "P" ||
            string.Equals(fields[1], controlNonce, StringComparison.Ordinal) == false ||
            int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var nestedPromptLevel) == false ||
            int.TryParse(fields[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exitCode) == false)
        {
            DisableIntegration("CommandLifecyclePipe",
                               "The PowerShell command lifecycle channel received an invalid control message.");
            return;
        }

        // A nested PowerShell prompt suspends the outer command. Only its return to level zero proves completion.
        if (nestedPromptLevel == 0)
        {
            ObservePrompt(exitCode);
            inputReady.TrySetResult();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var timeoutMilliseconds = checked((int)readinessTimeout.TotalMilliseconds);
            await pipeClient.ConnectAsync(timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref pipeConnected, 1);
            using var reader = new StreamReader(pipeClient, new UTF8Encoding(false, false), false, 1024, true);
            while (cancellationToken.IsCancellationRequested == false)
            {
                var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    DisableIntegration("CommandLifecyclePipe",
                                       "The PowerShell command lifecycle channel ended before session exit.");
                    return;
                }

                ProcessControlMessage(message);
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or ObjectDisposedException or IOException or TimeoutException or
                UnauthorizedAccessException)
        {
            if (cancellationToken.IsCancellationRequested == false)
            {
                var operation = Volatile.Read(ref pipeConnected) == 0
                    ? "PowerShellBootstrapConnection"
                    : "CommandLifecyclePipe";
                var message = Volatile.Read(ref pipeConnected) == 0
                    ? "The PowerShell command lifecycle bootstrap could not connect."
                    : "The PowerShell command lifecycle channel ended unexpectedly.";
                DisableIntegration(operation, message, exception);
            }
        }
    }

    private void DisableIntegration(string operation, string message, Exception? exception = null)
    {
        inputReady.TrySetResult();
        ObserveIntegrationLost();
        if (Interlocked.Exchange(ref readinessFailureReported, 1) == 0)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", operation, message, exception);
        }
    }

    private static ShellLaunchSpec CreateLaunchSpec(ShellLaunchSpec shell, string pipeName, string controlNonce)
    {
        if (string.IsNullOrWhiteSpace(pipeName) == true)
        {
            throw new ArgumentException("A command lifecycle pipe name is required.", nameof(pipeName));
        }

        if (string.IsNullOrWhiteSpace(controlNonce) == true ||
            controlNonce.Any(character => char.IsAsciiLetterOrDigit(character) == false))
        {
            throw new ArgumentException("A command lifecycle nonce must be alphanumeric.", nameof(controlNonce));
        }

        var script = CreateBootstrapScript(pipeName, controlNonce);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var arguments = shell.Arguments.Concat(["-NoExit", "-EncodedCommand", encodedScript]);
        return new ShellLaunchSpec(shell.ExecutablePath, arguments, shell.WorkingDirectory);
    }

    private static string CreateBootstrapScript(string pipeName, string controlNonce)
    {
        return $$"""
            $starboardOriginalPromptCommand = Get-Command -Name prompt -CommandType Function -ErrorAction Ignore
            $global:__StarboardOriginalPrompt = if ($null -eq $starboardOriginalPromptCommand) { $null } else { $starboardOriginalPromptCommand.ScriptBlock }
            Remove-Variable starboardOriginalPromptCommand -ErrorAction Ignore
            try {
                $global:__StarboardCommandPipe = [System.IO.Pipes.NamedPipeServerStream]::new('{{pipeName}}', [System.IO.Pipes.PipeDirection]::Out, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte, [System.IO.Pipes.PipeOptions]::Asynchronous)
                $starboardConnectTask = $global:__StarboardCommandPipe.WaitForConnectionAsync()
                if (-not $starboardConnectTask.Wait(5000)) { throw [System.TimeoutException]::new() }
                $global:__StarboardCommandWriter = [System.IO.StreamWriter]::new($global:__StarboardCommandPipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
                $global:__StarboardCommandWriter.AutoFlush = $true
            } catch {
                $global:__StarboardCommandWriter = $null
                if ($null -ne $global:__StarboardCommandPipe) { $global:__StarboardCommandPipe.Dispose() }
                $global:__StarboardCommandPipe = $null
            } finally {
                Remove-Variable starboardConnectTask -ErrorAction Ignore
            }
            function global:prompt {
                $starboardSucceeded = $?
                if ($null -ne $global:__StarboardOriginalPrompt) {
                    $starboardPrompt = & $global:__StarboardOriginalPrompt
                } else {
                    $starboardPrompt = "PS $($executionContext.SessionState.Path.CurrentLocation)> "
                }
                $starboardDepthVariable = Get-Variable -Name NestedPromptLevel -ErrorAction Ignore
                $starboardDepth = if ($null -eq $starboardDepthVariable) { 0 } else { [int]$starboardDepthVariable.Value }
                $starboardExitCode = if ($starboardSucceeded) { 0 } else { 1 }
                if ($null -ne $global:__StarboardCommandWriter) {
                    try { $global:__StarboardCommandWriter.WriteLine(('P|{{controlNonce}}|{0}|{1}' -f $starboardDepth, $starboardExitCode)) }
                    catch {
                        $global:__StarboardCommandWriter.Dispose()
                        $global:__StarboardCommandWriter = $null
                    }
                }
                $starboardPrompt
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
