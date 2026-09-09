using Starboard.Modules.DesktopIntegration.Contracts;

namespace Starboard.Modules.DesktopIntegration.Tests.TestDoubles;

internal sealed class ShortcutGuideContractTestDouble
{
    internal int RequestCount { get; private set; }

    internal GlobalShortcutRegistrationSnapshot? LastSnapshot { get; private set; }

    internal void HandleRequest(object? sender, ShortcutGuideRequestEventArgs eventArguments)
    {
        _ = sender;
        ArgumentNullException.ThrowIfNull(eventArguments);

        RequestCount++;
        LastSnapshot = eventArguments.RegistrationSnapshot;
    }
}
