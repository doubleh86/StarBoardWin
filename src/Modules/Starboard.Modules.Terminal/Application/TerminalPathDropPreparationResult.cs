using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal sealed record TerminalPathDropPreparationResult(TerminalPathDropConfirmationRequest? ConfirmationRequest,
                                                         string? ErrorMessage)
{
    internal bool Succeeded => ConfirmationRequest is not null;
}
