namespace Starboard.Modules.Terminal.Domain;

internal readonly record struct TerminalSessionId(Guid Value)
{
    internal static TerminalSessionId CreateNew()
    {
        return new TerminalSessionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}
