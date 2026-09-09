namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// The persisted, non-runtime description of one terminal tab.
/// </summary>
/// <remarks>
/// StartingDirectory is a local absolute directory chosen for the next shell start.
/// It does not track the current directory of a running shell. Order is zero-based.
/// </remarks>
public sealed record TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId ConfigurationId, string Name,
                                                       int Order, string StartingDirectory, TerminalShellKind ShellKind);

/// <summary>
/// The persisted terminal-only workspace. It never contains renderer session IDs,
/// process IDs, terminal input, output, or shell runtime state.
/// </summary>
public sealed record TerminalWorkspaceConfiguration(int SchemaVersion,
                                                    IReadOnlyList<TerminalWorkspaceTabConfiguration> Tabs,
                                                    TerminalTabConfigurationId? ActiveTabConfigurationId);
