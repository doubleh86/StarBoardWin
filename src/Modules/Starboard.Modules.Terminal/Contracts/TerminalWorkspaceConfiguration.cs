namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// The persisted, non-runtime description of one terminal tab. StartingDirectory is Windows
/// metadata for the next built-in shell start; WSL launch keeps its separate Linux home policy.
/// Order is zero-based. A profile identifier never carries executable paths or arguments.
/// </summary>
public sealed record TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId ConfigurationId, string Name,
                                                       int Order, string StartingDirectory, TerminalShellKind ShellKind)
{
    public TerminalLaunchProfileId LaunchProfileId { get; init; } = TerminalLaunchProfileId.FromShell(ShellKind);

    public TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId configurationId, string name,
                                             int order, string startingDirectory, TerminalLaunchProfileId? launchProfileId)
        : this(configurationId, name, order, startingDirectory, TerminalShellKind.Automatic)
    {
        if (launchProfileId is null || TerminalLaunchProfileId.TryCreate(launchProfileId.Value.Value, out var validated) == false)
        {
            throw new ArgumentException("A workspace tab requires a valid launch profile identifier.", nameof(launchProfileId));
        }

        LaunchProfileId = validated;
        if (validated.IsBuiltInShell == true)
        {
            ShellKind = validated.ToShellKind();
        }
    }
}

/// <summary>
/// The persisted terminal-only workspace. It never contains renderer session IDs,
/// process IDs, terminal input, output, or shell runtime state. User-created saved tabs use
/// <see cref="TerminalSavedTabsSnapshot"/> and have an independent persistence lifetime.
/// </summary>
public sealed record TerminalWorkspaceConfiguration(int SchemaVersion,
                                                    IReadOnlyList<TerminalWorkspaceTabConfiguration> Tabs,
                                                    TerminalTabConfigurationId? ActiveTabConfigurationId);
