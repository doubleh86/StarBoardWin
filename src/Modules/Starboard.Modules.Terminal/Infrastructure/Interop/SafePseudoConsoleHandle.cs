using Microsoft.Win32.SafeHandles;

namespace Starboard.Modules.Terminal.Infrastructure.Interop;

internal sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafePseudoConsoleHandle(nint existingHandle)
        : base(true)
    {
        SetHandle(existingHandle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ClosePseudoConsole(handle);
        return true;
    }
}
