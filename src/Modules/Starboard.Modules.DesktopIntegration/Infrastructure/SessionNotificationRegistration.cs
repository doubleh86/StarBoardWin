using System.ComponentModel;
using System.Runtime.InteropServices;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal interface ISessionNotificationNativeApi
{
    uint GetCurrentSessionId();
    void Register(nint windowHandle);
    void Unregister(nint windowHandle);
}

internal sealed class SessionNotificationNativeApi : ISessionNotificationNativeApi
{
    public uint GetCurrentSessionId()
    {
        if (NativeMethods.ProcessIdToSessionId((uint)Environment.ProcessId, out var sessionId) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return sessionId;
    }

    public void Register(nint windowHandle)
    {
        if (NativeMethods.WTSRegisterSessionNotification(windowHandle, NativeMethods.NotifyForThisSession) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Unregister(nint windowHandle)
    {
        if (NativeMethods.WTSUnRegisterSessionNotification(windowHandle) == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}

internal sealed class SessionNotificationRegistration : IDisposable
{
    private readonly ISessionNotificationNativeApi _nativeApi;
    private readonly IDiagnosticLog _diagnosticLog;
    private nint _windowHandle;
    private readonly uint _sessionId;

    internal SessionNotificationRegistration(nint windowHandle, IDiagnosticLog diagnosticLog,
                                             ISessionNotificationNativeApi? nativeApi = null)
    {
        _nativeApi = nativeApi ?? new SessionNotificationNativeApi();
        _diagnosticLog = diagnosticLog;
        try
        {
            _sessionId = _nativeApi.GetCurrentSessionId();
            _nativeApi.Register(windowHandle);
            _windowHandle = windowHandle;
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            // Preserve only an error code, never native exception text containing user data.
            LogFailure("RegisterSessionNotification", exception is Win32Exception native ? native.NativeErrorCode : exception.HResult);
        }
    }

    internal bool Matches(nint windowHandle, nint sessionId)
    {
        return _windowHandle != 0 && _windowHandle == windowHandle && sessionId.ToInt64() == _sessionId;
    }

    public void Dispose()
    {
        var windowHandle = _windowHandle;
        _windowHandle = 0;
        if (windowHandle == 0)
        {
            return;
        }

        try
        {
            _nativeApi.Unregister(windowHandle);
        }
        catch (Exception exception) when (DesktopIntegrationModule.IsRecoverablePlatformFailure(exception) == true)
        {
            LogFailure("UnregisterSessionNotification", exception is Win32Exception native ? native.NativeErrorCode : exception.HResult);
        }
    }

    private void LogFailure(string operation, int errorCode)
    {
        _diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", operation,
                             $"Session notification unavailable; display and periodic fallback remain active. Error={errorCode}.");
    }
}
