using Starboard.Modules.Terminal.Contracts;
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
    DropPaths,
    CloseSession,
    RestartSession,
    RenameSession,
    MoveSession,
    SetStartingDirectory,
    SessionError,
    RendererError,
    ConfirmationResponse,
    CreateSavedTab,
    UpdateSavedTab,
    DeleteSavedTab,
    LaunchSavedTab,
    CancelSavedTabLaunch,
}

internal sealed record RendererMessage(RendererMessageType Type, TerminalSessionId? SessionId = null,
                                       string? Data = null, int Columns = 0, int Rows = 0,
                                       TerminalConfirmationResponse? ConfirmationResponse = null,
                                       TerminalSavedTabCreateRequest? SavedTabCreateRequest = null,
                                       TerminalSavedTabUpdateRequest? SavedTabUpdateRequest = null,
                                       TerminalSavedTabDeleteRequest? SavedTabDeleteRequest = null,
                                       TerminalSavedTabLaunchRequest? SavedTabLaunchRequest = null,
                                       TerminalSavedTabLaunchCancellation? SavedTabLaunchCancellation = null,
                                       Guid? RendererInstanceId = null, long SessionGeneration = 0);
