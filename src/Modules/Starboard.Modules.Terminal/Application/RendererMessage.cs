namespace Starboard.Modules.Terminal.Application;

internal enum RendererMessageType
{
    Ready,
    Input,
    Resize,
    Copy,
    PasteRequest,
    RendererError,
}

internal sealed record RendererMessage(
    RendererMessageType Type,
    string? Data = null,
    int Columns = 0,
    int Rows = 0);
