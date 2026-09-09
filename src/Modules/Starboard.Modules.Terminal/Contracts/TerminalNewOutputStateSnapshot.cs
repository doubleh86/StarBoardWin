using System.Collections.ObjectModel;

namespace Starboard.Modules.Terminal.Contracts;

public readonly record struct TerminalSessionNewOutputState(TerminalSessionReference Session, bool HasNewOutput);

/// <summary>
/// Immutable, process-local unread-output state used to rebuild a renderer. State is scoped to an
/// exact session generation and must not be restored onto a restarted session.
/// </summary>
public sealed class TerminalNewOutputStateSnapshot
{
    private readonly ReadOnlyCollection<TerminalSessionNewOutputState> _states;

    public TerminalNewOutputStateSnapshot(IEnumerable<TerminalSessionNewOutputState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var capturedStates = states.ToArray();
        if (capturedStates.Any(state => state.Session.SessionId == Guid.Empty || state.Session.Generation < 1) == true)
        {
            throw new ArgumentException("A new-output snapshot requires valid runtime session references.",
                                        nameof(states));
        }

        if (capturedStates.Select(state => state.Session.SessionId).Distinct().Count() != capturedStates.Length)
        {
            throw new ArgumentException("A new-output snapshot cannot contain duplicate runtime session identifiers.",
                                        nameof(states));
        }

        _states = Array.AsReadOnly(capturedStates);
    }

    public IReadOnlyList<TerminalSessionNewOutputState> States => _states;

    public bool TryGetState(TerminalSessionReference session, out TerminalSessionNewOutputState state)
    {
        foreach (var candidate in _states)
        {
            if (candidate.Session == session)
            {
                state = candidate;
                return true;
            }
        }

        state = default;
        return false;
    }
}
