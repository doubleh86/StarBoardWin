using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal sealed class TerminalAppearanceState
{
    private TerminalAppearanceSettings current;
    private bool isRendererReady;

    internal TerminalAppearanceState(TerminalAppearanceSettings initialAppearance)
    {
        ArgumentNullException.ThrowIfNull(initialAppearance);
        current = initialAppearance;
    }

    internal TerminalAppearanceSettings Current => current;

    internal TerminalAppearanceSettings? Update(TerminalAppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        current = appearance;

        return isRendererReady == true ? current : null;
    }

    internal TerminalAppearanceSettings MarkRendererReady()
    {
        isRendererReady = true;
        return current;
    }

    internal void MarkRendererUnavailable()
    {
        isRendererReady = false;
    }
}
