using Starboard.Modules.DesktopIntegration.Contracts;
using Starboard.Modules.DesktopIntegration.Infrastructure.Interop;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal interface IVirtualDesktopManagerApi : IDisposable
{
    bool IsWindowOnCurrentVirtualDesktop(nint windowHandle);

    Guid GetWindowDesktopId(nint windowHandle);

    void MoveWindowToDesktop(nint windowHandle, Guid desktopId);
}

internal interface IVirtualDesktopService : IDisposable
{
    VirtualDesktopCapabilities Capabilities { get; }

    VirtualDesktopWindowState CaptureWindowState(nint windowHandle);

    VirtualDesktopMoveStatus MoveWindowToDesktop(nint windowHandle, Guid desktopId);
}

internal static class VirtualDesktopServiceFactory
{
    internal static IVirtualDesktopService Create(IDiagnosticLog diagnosticLog)
    {
        return Create(diagnosticLog, ComVirtualDesktopManagerApi.Create);
    }

    internal static IVirtualDesktopService Create(IDiagnosticLog diagnosticLog,
                                                  Func<IVirtualDesktopManagerApi> managerFactory)
    {
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        ArgumentNullException.ThrowIfNull(managerFactory);

        try
        {
            var service = new SupportedVirtualDesktopService(managerFactory());
            return new FailoverVirtualDesktopService(service, diagnosticLog);
        }
        catch (Exception exception) when (VirtualDesktopFailurePolicy.IsNonFatal(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", "InitializeVirtualDesktop",
                                "The documented virtual desktop API is unavailable; virtual desktop operations are disabled.",
                                exception);
            return new FallbackVirtualDesktopService();
        }
    }
}

internal sealed class SupportedVirtualDesktopService : IVirtualDesktopService
{
    private static readonly VirtualDesktopCapabilities SupportedCapabilities = new(true, true, false);

    private readonly IVirtualDesktopManagerApi manager;
    private bool isDisposed;

    internal SupportedVirtualDesktopService(IVirtualDesktopManagerApi manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        this.manager = manager;
    }

    public VirtualDesktopCapabilities Capabilities => SupportedCapabilities;

    public VirtualDesktopWindowState CaptureWindowState(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var isOnCurrentDesktop = manager.IsWindowOnCurrentVirtualDesktop(windowHandle);
        var desktopId = manager.GetWindowDesktopId(windowHandle);

        return new VirtualDesktopWindowState(VirtualDesktopWindowStateStatus.Available,
                                             isOnCurrentDesktop, desktopId);
    }

    public VirtualDesktopMoveStatus MoveWindowToDesktop(nint windowHandle, Guid desktopId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        manager.MoveWindowToDesktop(windowHandle, desktopId);

        return VirtualDesktopMoveStatus.Moved;
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        manager.Dispose();
    }
}

internal sealed class FallbackVirtualDesktopService : IVirtualDesktopService
{
    private static readonly VirtualDesktopCapabilities UnsupportedCapabilities = new(false, false, false);
    private static readonly VirtualDesktopWindowState UnavailableState =
        new(VirtualDesktopWindowStateStatus.Unavailable, null, null);

    public VirtualDesktopCapabilities Capabilities => UnsupportedCapabilities;

    public VirtualDesktopWindowState CaptureWindowState(nint windowHandle)
    {
        _ = windowHandle;
        return UnavailableState;
    }

    public VirtualDesktopMoveStatus MoveWindowToDesktop(nint windowHandle, Guid desktopId)
    {
        _ = windowHandle;
        _ = desktopId;
        return VirtualDesktopMoveStatus.Unsupported;
    }

    public void Dispose()
    {
    }
}

internal sealed class FailoverVirtualDesktopService : IVirtualDesktopService
{
    private readonly IDiagnosticLog diagnosticLog;
    private readonly Lock stateLock = new();

    private IVirtualDesktopService service;
    private bool isDisposed;

    internal FailoverVirtualDesktopService(IVirtualDesktopService service, IDiagnosticLog diagnosticLog)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        this.service = service;
        this.diagnosticLog = diagnosticLog;
    }

    public VirtualDesktopCapabilities Capabilities
    {
        get
        {
            lock (stateLock)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                return service.Capabilities;
            }
        }
    }

    public VirtualDesktopWindowState CaptureWindowState(nint windowHandle)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            try
            {
                return service.CaptureWindowState(windowHandle);
            }
            catch (Exception exception) when (VirtualDesktopFailurePolicy.IsNonFatal(exception) == true)
            {
                TransitionToFallback("CaptureVirtualDesktopState",
                                     "The panel virtual desktop state could not be queried; virtual desktop operations are now disabled.",
                                     exception);
                return service.CaptureWindowState(windowHandle);
            }
        }
    }

    public VirtualDesktopMoveStatus MoveWindowToDesktop(nint windowHandle, Guid desktopId)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            try
            {
                return service.MoveWindowToDesktop(windowHandle, desktopId);
            }
            catch (Exception exception) when (VirtualDesktopFailurePolicy.IsNonFatal(exception) == true)
            {
                TransitionToFallback("MovePanelToVirtualDesktop",
                                     "The panel could not be moved to the requested virtual desktop; virtual desktop operations are now disabled.",
                                     exception);
                return VirtualDesktopMoveStatus.Failed;
            }
        }
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (isDisposed == true)
            {
                return;
            }

            isDisposed = true;
            DisposeService(service, "DisposeVirtualDesktopAdapter");
            service = new FallbackVirtualDesktopService();
        }
    }

    private void TransitionToFallback(string operation, string message, Exception exception)
    {
        diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", operation, message, exception);
        DisposeService(service, "DisposeFailedVirtualDesktopAdapter");
        service = new FallbackVirtualDesktopService();
    }

    private void DisposeService(IVirtualDesktopService serviceToDispose, string operation)
    {
        try
        {
            serviceToDispose.Dispose();
        }
        catch (Exception exception) when (VirtualDesktopFailurePolicy.IsNonFatal(exception) == true)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "DesktopIntegration", operation,
                                "The virtual desktop COM adapter could not be released cleanly; application shutdown continues.",
                                exception);
        }
    }
}

internal static class VirtualDesktopFailurePolicy
{
    internal static bool IsNonFatal(Exception exception)
    {
        return exception is not OutOfMemoryException and
            not StackOverflowException and
            not AccessViolationException;
    }
}
