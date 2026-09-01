using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Starboard.Modules.Terminal.Infrastructure.Interop;

internal sealed class ProcessAttributeList : IDisposable
{
    private nint attributeList;

    internal ProcessAttributeList(nint pseudoConsole)
    {
        nint requiredSize = 0;
        _ = NativeMethods.InitializeProcThreadAttributeList(
            0,
            1,
            0,
            ref requiredSize);

        if (requiredSize == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        attributeList = Marshal.AllocHGlobal(requiredSize);
        if (NativeMethods.InitializeProcThreadAttributeList(
            attributeList,
            1,
            0,
            ref requiredSize) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (NativeMethods.UpdateProcThreadAttribute(
            attributeList,
            0,
            NativeMethods.ProcThreadAttributePseudoConsole,
            pseudoConsole,
            nint.Size,
            0,
            0) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal nint DangerousGetHandle()
    {
        return attributeList;
    }

    public void Dispose()
    {
        if (attributeList != 0)
        {
            NativeMethods.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = 0;
        }
    }
}
