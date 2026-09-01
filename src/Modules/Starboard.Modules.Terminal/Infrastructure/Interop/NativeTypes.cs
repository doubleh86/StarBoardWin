using System.Runtime.InteropServices;

namespace Starboard.Modules.Terminal.Infrastructure.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct ConsoleSize
{
    internal ConsoleSize(short columns, short rows)
    {
        Columns = columns;
        Rows = rows;
    }

    internal short Columns;

    internal short Rows;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct StartupInfo
{
    internal int Size;
    internal string? Reserved;
    internal string? Desktop;
    internal string? Title;
    internal int X;
    internal int Y;
    internal int XSize;
    internal int YSize;
    internal int XCountChars;
    internal int YCountChars;
    internal int FillAttribute;
    internal int Flags;
    internal short ShowWindow;
    internal short Reserved2Size;
    internal nint Reserved2;
    internal nint StandardInput;
    internal nint StandardOutput;
    internal nint StandardError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoExtended
{
    internal StartupInfo StartupInfo;
    internal nint AttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    internal nint Process;
    internal nint Thread;
    internal int ProcessIdentifier;
    internal int ThreadIdentifier;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SecurityAttributes
{
    internal int Length;
    internal nint SecurityDescriptor;
    internal int InheritHandle;
}
