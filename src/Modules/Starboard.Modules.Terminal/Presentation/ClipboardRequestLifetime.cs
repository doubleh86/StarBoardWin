using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Presentation;

internal sealed record ClipboardRequestLifetime(long RendererGeneration, Guid RendererInstanceId,
                                                long WorkspaceEpoch, TerminalSessionReference Session)
{
    internal static ClipboardRequestLifetime? Capture(long rendererGeneration, Guid rendererInstanceId,
                                                      long workspaceEpoch, TerminalSessionId sessionId,
                                                      long sessionGeneration, bool rendererAvailable)
    {
        if (rendererAvailable == false || rendererInstanceId == Guid.Empty || sessionGeneration < 1)
        {
            return null;
        }

        return new ClipboardRequestLifetime(rendererGeneration, rendererInstanceId, workspaceEpoch,
                                            new TerminalSessionReference(sessionId.Value, sessionGeneration));
    }

    internal bool CanApply(long rendererGeneration, Guid rendererInstanceId, long workspaceEpoch,
                           TerminalSessionId? activeSessionId, bool sessionIsCurrent, bool rendererAvailable,
                           bool hasConfirmation)
    {
        return rendererAvailable == true && hasConfirmation == false && sessionIsCurrent == true &&
               RendererInstanceId != Guid.Empty && rendererInstanceId == RendererInstanceId &&
               rendererGeneration == RendererGeneration && workspaceEpoch == WorkspaceEpoch &&
               activeSessionId?.Value == Session.SessionId;
    }
}
