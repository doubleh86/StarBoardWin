using System.Runtime.InteropServices;

namespace Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManagerNative
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow,
                                        [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, [In] ref Guid desktopId);
}

internal sealed class ComVirtualDesktopManagerApi : IVirtualDesktopManagerApi
{
    private static readonly Guid VirtualDesktopManagerClassId =
        new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    private IVirtualDesktopManagerNative? manager;

    private ComVirtualDesktopManagerApi(IVirtualDesktopManagerNative manager)
    {
        this.manager = manager;
    }

    internal static ComVirtualDesktopManagerApi Create()
    {
        var managerType = Type.GetTypeFromCLSID(VirtualDesktopManagerClassId, throwOnError: true)
            ?? throw new InvalidOperationException("The Windows virtual desktop COM class is unavailable.");
        var instance = Activator.CreateInstance(managerType)
            ?? throw new InvalidOperationException("The Windows virtual desktop COM class could not be created.");

        if (instance is IVirtualDesktopManagerNative virtualDesktopManager)
        {
            return new ComVirtualDesktopManagerApi(virtualDesktopManager);
        }

        if (Marshal.IsComObject(instance) == true)
        {
            _ = Marshal.FinalReleaseComObject(instance);
        }

        throw new InvalidCastException("The Windows virtual desktop COM class does not expose IVirtualDesktopManager.");
    }

    public bool IsWindowOnCurrentVirtualDesktop(nint windowHandle)
    {
        var currentManager = GetManager();
        var result = currentManager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isOnCurrentDesktop);
        Marshal.ThrowExceptionForHR(result);

        return isOnCurrentDesktop;
    }

    public Guid GetWindowDesktopId(nint windowHandle)
    {
        var currentManager = GetManager();
        var result = currentManager.GetWindowDesktopId(windowHandle, out var desktopId);
        Marshal.ThrowExceptionForHR(result);

        return desktopId;
    }

    public void MoveWindowToDesktop(nint windowHandle, Guid desktopId)
    {
        var currentManager = GetManager();
        var result = currentManager.MoveWindowToDesktop(windowHandle, ref desktopId);
        Marshal.ThrowExceptionForHR(result);
    }

    public void Dispose()
    {
        var currentManager = Interlocked.Exchange(ref manager, null);
        if (currentManager is null)
        {
            return;
        }

        if (Marshal.IsComObject(currentManager) == true)
        {
            _ = Marshal.FinalReleaseComObject(currentManager);
        }
    }

    private IVirtualDesktopManagerNative GetManager()
    {
        return manager ?? throw new ObjectDisposedException(nameof(ComVirtualDesktopManagerApi));
    }
}
