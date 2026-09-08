using System.ComponentModel;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal sealed class ForegroundWindowObserver : IDisposable
{
    private readonly WinEventCallback _callback;
    private GCHandle _callbackRoot;
    private readonly SafeWinEventHookHandle _hook;
    private readonly IWinEventHookNativeApi _nativeApi;
    private readonly uint _registrationThreadIdentifier;
    private int _disposeState;

    internal ForegroundWindowObserver()
        : this(new WinEventHookNativeApi())
    {
    }

    internal ForegroundWindowObserver(IWinEventHookNativeApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);

        _nativeApi = nativeApi;
        _registrationThreadIdentifier = nativeApi.GetCurrentThreadIdentifier();
        _callback = HandleWinEvent;
        _callbackRoot = GCHandle.Alloc(_callback);
        nint hook;

        try
        {
            hook = nativeApi.RegisterForegroundChanged(_callback);
        }
        catch
        {
            _callbackRoot.Free();
            throw;
        }

        _hook = new SafeWinEventHookHandle(hook, nativeApi);
    }

    internal event EventHandler? ForegroundChanged;

    internal int ReleaseErrorCode => _hook.ReleaseErrorCode;

    public void Dispose()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        // UnhookWinEvent must run on the same message-loop thread that installed
        // the hook. Keep the observer retryable when ownership is violated.
        if (_nativeApi.GetCurrentThreadIdentifier() != _registrationThreadIdentifier)
        {
            throw new InvalidOperationException(
                "The foreground observer must be disposed on its registration thread.");
        }

        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        if (_hook.TryRelease() == false)
        {
            Volatile.Write(ref _disposeState, 0);
            throw new Win32Exception(
                _hook.ReleaseErrorCode,
                "The foreground window event hook could not be released.");
        }

        _hook.Dispose();
        _callbackRoot.Free();
    }

    private void HandleWinEvent(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectIdentifier,
        int childIdentifier,
        uint eventThreadIdentifier,
        uint eventTime)
    {
        _ = hook;
        _ = eventType;
        _ = windowHandle;
        _ = objectIdentifier;
        _ = childIdentifier;
        _ = eventThreadIdentifier;
        _ = eventTime;

        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        ForegroundChanged?.Invoke(this, EventArgs.Empty);
    }
}
