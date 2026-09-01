using Microsoft.Win32.SafeHandles;

namespace Starboard.Modules.Terminal.Infrastructure.Interop;

internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeKernelHandle()
        : base(true)
    {
    }

    internal SafeKernelHandle(nint existingHandle)
        : base(true)
    {
        SetHandle(existingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}
