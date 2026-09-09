using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal enum RendererMessageType
{
    Ready,
    NewTab,
    SelectSession,
    SelectNext,
    SelectPrevious,
    Input,
    Resize,
    Copy,
    PasteRequest,
    CloseSession,
    RestartSession,
    SessionError,
    RendererError,
}

internal sealed record RendererMessage(RendererMessageType Type, TerminalSessionId? SessionId = null,
                                       string? Data = null, int Columns = 0, int Rows = 0);
