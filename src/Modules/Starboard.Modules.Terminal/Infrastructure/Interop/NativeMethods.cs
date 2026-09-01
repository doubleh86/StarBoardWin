using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Starboard.Modules.Terminal.Infrastructure.Interop;

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
    Justification = "ConPTY process startup uses mutable buffers and pointer-sized attribute lists that are clearer with the documented DllImport signatures.")]
[SuppressMessage(
    "Performance",
    "CA1838:Avoid StringBuilder parameters for P/Invokes",
    Justification = "CreateProcessW requires a mutable command-line buffer and can modify it in place.")]
internal static class NativeMethods
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const nint ProcThreadAttributePseudoConsole = 0x00020016;
    internal const uint Infinite = 0xFFFFFFFF;
    internal const uint StillActive = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        nint pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    internal static extern int CreatePseudoConsole(
        ConsoleSize size,
        SafeFileHandle inputReadSide,
        SafeFileHandle outputWriteSide,
        uint flags,
        out nint pseudoConsole);

    [DllImport("kernel32.dll")]
    internal static extern int ResizePseudoConsole(
        SafePseudoConsoleHandle pseudoConsole,
        ConsoleSize size);

    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(nint pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(
        nint attributeList,
        int attributeCount,
        int flags,
        ref nint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(
        nint attributeList,
        uint flags,
        nint attribute,
        nint value,
        nint size,
        nint previousValue,
        nint returnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(nint attributeList);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        ref SecurityAttributes processAttributes,
        ref SecurityAttributes threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        [In] ref StartupInfoExtended startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(
        SafeKernelHandle handle,
        uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(
        SafeKernelHandle process,
        out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(
        SafeKernelHandle process,
        uint exitCode);
}
