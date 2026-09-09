using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Tests.TestDoubles;

internal sealed class TerminalSafetyContractTestDouble
{
    internal TerminalSafetyContractTestDouble(TerminalConfirmationToken pendingToken,
                                              TerminalSessionReference currentSession)
    {
        PendingToken = pendingToken;
        CurrentSession = currentSession;
    }

    internal TerminalConfirmationToken PendingToken { get; set; }

    internal TerminalSessionReference CurrentSession { get; set; }

    internal List<TerminalConfirmationResult> AppliedResults { get; } = [];

    internal bool TryApply(TerminalConfirmationResponse response)
    {
        if (response.CanApplyTo(PendingToken, CurrentSession) == false)
        {
            return false;
        }

        AppliedResults.Add(response.Result);
        return true;
    }
}
