using Microsoft.Win32.SafeHandles;

namespace Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

internal sealed class SafeWinEventHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly IWinEventHookNativeApi _nativeApi;

    internal SafeWinEventHookHandle(nint handle, IWinEventHookNativeApi nativeApi)
        : base(false)
    {
        _nativeApi = nativeApi;
        SetHandle(handle);
    }

    internal int ReleaseErrorCode { get; private set; }

    internal bool TryRelease()
    {
        if (IsInvalid == true || IsClosed == true)
        {
            ReleaseErrorCode = 0;
            return true;
        }

        var released = _nativeApi.Unregister(handle, out var errorCode);
        ReleaseErrorCode = errorCode;
        if (released == true)
        {
            SetHandleAsInvalid();
        }

        return released;
    }

    protected override bool ReleaseHandle()
    {
        // UnhookWinEvent is thread-affine, so a finalizer thread cannot safely
        // release this handle. ForegroundWindowObserver calls TryRelease from the
        // registration thread before disposing this non-owning SafeHandle.
        return true;
    }
}
