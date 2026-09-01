using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Infrastructure;

internal sealed class ConPtySession : IAsyncDisposable
{
    private readonly IDiagnosticLog diagnosticLog;
    private readonly SafePseudoConsoleHandle pseudoConsole;
    private readonly SafeFileHandle pseudoConsoleInput;
    private readonly SafeFileHandle pseudoConsoleOutput;
    private readonly ProcessAttributeList processAttributeList;
    private readonly SafeKernelHandle process;
    private readonly SafeKernelHandle processThread;
    private readonly FileStream inputStream;
    private readonly FileStream outputStream;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim inputLock = new(1, 1);

    private Task? outputPump;
    private Task? processWait;
    private bool isDisposed;

    private ConPtySession(
        IDiagnosticLog diagnosticLog,
        SafePseudoConsoleHandle pseudoConsole,
        SafeFileHandle pseudoConsoleInput,
        SafeFileHandle pseudoConsoleOutput,
        ProcessAttributeList processAttributeList,
        SafeKernelHandle process,
        SafeKernelHandle processThread,
        FileStream inputStream,
        FileStream outputStream)
    {
        this.diagnosticLog = diagnosticLog;
        this.pseudoConsole = pseudoConsole;
        this.pseudoConsoleInput = pseudoConsoleInput;
        this.pseudoConsoleOutput = pseudoConsoleOutput;
        this.processAttributeList = processAttributeList;
        this.process = process;
        this.processThread = processThread;
        this.inputStream = inputStream;
        this.outputStream = outputStream;
    }

    internal event Action<string>? OutputReceived;

    internal event Action<uint>? Exited;

    internal static ConPtySession Start(
        ShellLaunchSpec shell,
        int columns,
        int rows,
        IDiagnosticLog diagnosticLog)
    {
        SafeFileHandle? pseudoConsoleInput = null;
        SafeFileHandle? hostInput = null;
        SafeFileHandle? hostOutput = null;
        SafeFileHandle? pseudoConsoleOutput = null;
        SafePseudoConsoleHandle? pseudoConsole = null;
        ProcessAttributeList? processAttributeList = null;
        SafeKernelHandle? process = null;
        SafeKernelHandle? processThread = null;

        try
        {
            CreatePipe(out pseudoConsoleInput, out hostInput);
            CreatePipe(out hostOutput, out pseudoConsoleOutput);

            var result = NativeMethods.CreatePseudoConsole(
                ToConsoleSize(columns, rows),
                pseudoConsoleInput,
                pseudoConsoleOutput,
                0,
                out var pseudoConsoleValue);
            ThrowIfFailed(result, "CreatePseudoConsole");
            pseudoConsole = new SafePseudoConsoleHandle(pseudoConsoleValue);

            processAttributeList = new ProcessAttributeList(
                pseudoConsole.DangerousGetHandle());
            var startupInfo = new StartupInfoExtended
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoExtended>(),
                },
                AttributeList = processAttributeList.DangerousGetHandle(),
            };
            var commandLine = new StringBuilder(
                $"\"{shell.ExecutablePath}\" {shell.Arguments}".TrimEnd());
            var securityAttributeSize = Marshal.SizeOf<SecurityAttributes>();
            var processAttributes = new SecurityAttributes
            {
                Length = securityAttributeSize,
            };
            var threadAttributes = new SecurityAttributes
            {
                Length = securityAttributeSize,
            };

            if (NativeMethods.CreateProcessW(
                shell.ExecutablePath,
                commandLine,
                ref processAttributes,
                ref threadAttributes,
                false,
                NativeMethods.ExtendedStartupInfoPresent,
                0,
                shell.WorkingDirectory,
                ref startupInfo,
                out var processInformation) == false)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            process = new SafeKernelHandle(processInformation.Process);
            processThread = new SafeKernelHandle(processInformation.Thread);

            var inputStream = new FileStream(hostInput, FileAccess.Write, 4096, false);
            hostInput = null;
            var outputStream = new FileStream(hostOutput, FileAccess.Read, 4096, false);
            hostOutput = null;

            return new ConPtySession(
                diagnosticLog,
                pseudoConsole,
                pseudoConsoleInput,
                pseudoConsoleOutput,
                processAttributeList,
                process,
                processThread,
                inputStream,
                outputStream);
        }
        catch
        {
            pseudoConsoleInput?.Dispose();
            hostInput?.Dispose();
            hostOutput?.Dispose();
            pseudoConsoleOutput?.Dispose();
            processThread?.Dispose();
            process?.Dispose();
            processAttributeList?.Dispose();
            pseudoConsole?.Dispose();
            throw;
        }
    }

    internal void BeginReading()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        outputPump ??= PumpOutputAsync(lifetimeCancellation.Token);
        processWait ??= WaitForExitAsync(lifetimeCancellation.Token);
    }

    internal async ValueTask WriteAsync(
        string data,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var bytes = Encoding.UTF8.GetBytes(data);

        await inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await inputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await inputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            inputLock.Release();
        }
    }

    internal void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var result = NativeMethods.ResizePseudoConsole(
            pseudoConsole,
            ToConsoleSize(columns, rows));
        ThrowIfFailed(result, "ResizePseudoConsole");
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        lifetimeCancellation.Cancel();
        inputStream.Dispose();

        if (NativeMethods.GetExitCodeProcess(process, out var exitCode) == true &&
            exitCode == NativeMethods.StillActive)
        {
            _ = NativeMethods.TerminateProcess(process, 0);
        }

        await ObserveBackgroundTaskAsync(processWait).ConfigureAwait(false);

        var closeTask = Task.Run(pseudoConsole.Dispose);
        _ = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(2)))
            .ConfigureAwait(false);

        outputStream.Dispose();
        await ObserveBackgroundTaskAsync(outputPump).ConfigureAwait(false);

        pseudoConsoleInput.Dispose();
        pseudoConsoleOutput.Dispose();
        processThread.Dispose();
        process.Dispose();
        processAttributeList.Dispose();

        inputLock.Dispose();
        lifetimeCancellation.Dispose();
    }

    private async Task PumpOutputAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(
                outputStream,
                new UTF8Encoding(false, false),
                false,
                4096,
                true);
            var buffer = new char[4096];

            while (cancellationToken.IsCancellationRequested == false)
            {
                var count = await reader
                    .ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                OutputReceived?.Invoke(new string(buffer, 0, count));
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            if (cancellationToken.IsCancellationRequested == false)
            {
                diagnosticLog.Write(
                    DiagnosticLevel.Warning,
                    "Terminal",
                    "ReadOutput",
                    "The ConPTY output stream ended unexpectedly.",
                    exception);
            }
        }
    }

    private async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await Task.Run(
            () => NativeMethods.WaitForSingleObject(process, NativeMethods.Infinite),
            cancellationToken).ConfigureAwait(false);

        if (NativeMethods.GetExitCodeProcess(process, out var exitCode) == false)
        {
            exitCode = uint.MaxValue;
        }

        Exited?.Invoke(exitCode);
    }

    private static void CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe)
    {
        if (NativeMethods.CreatePipe(out readPipe, out writePipe, 0, 0) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static ConsoleSize ToConsoleSize(int columns, int rows)
    {
        return new ConsoleSize(
            checked((short)Math.Clamp(columns, 2, 500)),
            checked((short)Math.Clamp(rows, 1, 300)));
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation} failed with HRESULT 0x{result:X8}.",
            Marshal.GetExceptionForHR(result));
    }

    private static async Task ObserveBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException)
        {
            // Shutdown is bounded; the owned handles have already been closed.
        }
    }
}
