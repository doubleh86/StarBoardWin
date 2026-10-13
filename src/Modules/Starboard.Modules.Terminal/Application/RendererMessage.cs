using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal enum RendererMessageType
{
    Ready,
    RetryLaunchProfiles,
    NewTab,
    DuplicateTab,
    BeginPanelResize,
    ChangeCollapsedHeight,
    SelectSession,
    SelectNext,
    SelectPrevious,
    Input,
    Resize,
    Copy,
    PasteRequest,
    DropPaths,
    OpenUrlRequest,
    CloseSession,
    RestartSession,
    RenameSession,
    MoveSession,
    ReorderSession,
    SetStartingDirectory,
    SessionError,
    RendererError,
    ConfirmationResponse,
    CreateSavedTab,
    UpdateSavedTab,
    DeleteSavedTab,
    LaunchSavedTab,
    CancelSavedTabLaunch,
    SavedTabDirectoryRequest,
}

internal sealed record TerminalTabReorderRequest(Guid RendererInstanceId, TerminalSessionReference Session,
                                                 TerminalSessionId TargetSessionId,
                                                 TerminalTabInsertionPosition Position,
                                                 IReadOnlyList<TerminalSessionReference> StartOrder);

internal sealed record RendererReorderContext(bool IsAvailable, long Generation, Guid InstanceId)
{
    internal bool CanApply(TerminalTabReorderRequest request, long requestGeneration, bool isCurrentSender = true)
    {
        return IsAvailable == true && isCurrentSender == true && InstanceId != Guid.Empty &&
               Generation == requestGeneration &&
               InstanceId == request.RendererInstanceId;
    }
}

internal sealed record RendererMessage(RendererMessageType Type, TerminalSessionId? SessionId = null,
                                       string? Data = null, int Columns = 0, int Rows = 0,
                                       TerminalConfirmationResponse? ConfirmationResponse = null,
                                       TerminalSavedTabCreateRequest? SavedTabCreateRequest = null,
                                       TerminalSavedTabUpdateRequest? SavedTabUpdateRequest = null,
                                       TerminalSavedTabDeleteRequest? SavedTabDeleteRequest = null,
                                       TerminalSavedTabLaunchRequest? SavedTabLaunchRequest = null,
                                       TerminalSavedTabLaunchCancellation? SavedTabLaunchCancellation = null,
                                       Guid? RendererInstanceId = null, long SessionGeneration = 0,
                                       TerminalUrlOpenTarget? UrlOpenTarget = null,
                                       TerminalNewTabRequest? NewTabRequest = null,
                                       TerminalTabDuplicateRequest? TabDuplicateRequest = null,
                                       TerminalCollapsedHeightChangeRequest? CollapsedHeightChangeRequest = null,
                                       TerminalTabReorderRequest? ReorderRequest = null);
