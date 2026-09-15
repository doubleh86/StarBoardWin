namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// Describes only the capabilities exposed by the documented IVirtualDesktopManager contract.
/// </summary>
public sealed record VirtualDesktopCapabilities(bool CanQueryWindowState, bool CanMoveWindow,
                                                bool CanPinWindowToAllDesktops);

public enum VirtualDesktopWindowStateStatus
{
    Available,
    Unavailable,
}

/// <summary>
/// Captures the virtual desktop associated with the panel at the time of the query.
/// Values are absent when the documented Windows API is unavailable.
/// </summary>
public sealed record VirtualDesktopWindowState(VirtualDesktopWindowStateStatus Status,
                                               bool? IsOnCurrentDesktop, Guid? DesktopId);

public enum VirtualDesktopMoveStatus
{
    Moved,
    Unsupported,
    Failed,
}
