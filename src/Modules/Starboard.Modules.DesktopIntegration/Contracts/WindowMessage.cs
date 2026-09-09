namespace Starboard.Modules.DesktopIntegration.Contracts;

/// <summary>
/// Preserves every native argument supplied to a top-level window procedure.
/// </summary>
/// <remarks>
/// The host must forward this value synchronously. A pointer carried by
/// <see cref="LongParameter"/> is valid only for the lifetime guaranteed by the
/// originating window procedure. The DesktopIntegration native adapter owns copying
/// pointed-to data before the procedure returns.
/// </remarks>
public readonly record struct WindowMessage(nint WindowHandle, int MessageId, nuint WordParameter, nint LongParameter);
