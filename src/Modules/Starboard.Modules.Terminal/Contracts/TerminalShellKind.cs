namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Shell kinds that can be stored in a workspace configuration. Custom executables
/// and arguments are intentionally excluded from the persisted workspace contract.
/// </summary>
public enum TerminalShellKind
{
    Automatic,
    Pwsh,
    PowerShell,
    Cmd,
}
