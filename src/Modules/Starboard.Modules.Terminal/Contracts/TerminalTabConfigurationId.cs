namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Stable identity for a saved terminal-tab configuration.
/// This identity is intentionally separate from the runtime session identity that
/// addresses a live renderer and shell process.
/// </summary>
public readonly record struct TerminalTabConfigurationId
{
    public TerminalTabConfigurationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The terminal tab configuration identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalTabConfigurationId CreateNew()
    {
        return new TerminalTabConfigurationId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}
