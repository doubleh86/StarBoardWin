using System.Globalization;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Domain;

internal static class TerminalWorkspaceConfigurationValidator
{
    internal const int MinimumSchemaVersion = 1;
    // Keep automatic persistence on schema 1 until mixed-profile restoration is connected.
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumSchemaVersion = 2;
    internal const int MaximumTabs = TerminalTabRegistry.DefaultMaximumTabs;
    internal const int MaximumTabNameTextElements = 32;
    internal const int MaximumStartingDirectoryCharacters = 32_767;

    internal static void Validate(TerminalWorkspaceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion < MinimumSchemaVersion || configuration.SchemaVersion > MaximumSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), configuration.SchemaVersion,
                                                  "The terminal workspace schema version is not supported.");
        }

        if (configuration.Tabs is null || configuration.Tabs.Count == 0 || configuration.Tabs.Count > MaximumTabs)
        {
            throw new ArgumentException($"A terminal workspace must contain between 1 and {MaximumTabs} tabs.",
                                        nameof(configuration));
        }

        var configurationIds = new HashSet<TerminalTabConfigurationId>();
        var orders = new HashSet<int>();
        foreach (var tab in configuration.Tabs)
        {
            ValidateTab(tab, configuration.SchemaVersion);
            if (configurationIds.Add(tab.ConfigurationId) == false)
            {
                throw new ArgumentException("Terminal workspace configuration identifiers must be unique.",
                                            nameof(configuration));
            }

            if (orders.Add(tab.Order) == false)
            {
                throw new ArgumentException("Terminal workspace tab order values must be unique.", nameof(configuration));
            }
        }

        for (var order = 0; order < configuration.Tabs.Count; order++)
        {
            if (orders.Contains(order) == false)
            {
                throw new ArgumentException("Terminal workspace tab order must be contiguous and zero-based.",
                                            nameof(configuration));
            }
        }

        if (configuration.ActiveTabConfigurationId is { } activeConfigurationId &&
            configurationIds.Contains(activeConfigurationId) == false)
        {
            throw new ArgumentException("The active terminal workspace tab must exist in the workspace.", nameof(configuration));
        }
    }

    private static void ValidateTab(TerminalWorkspaceTabConfiguration tab, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.ConfigurationId.Value == Guid.Empty)
        {
            throw new ArgumentException("The terminal tab configuration identifier cannot be empty.", nameof(tab));
        }

        if (tab.Order < 0 || tab.Order >= MaximumTabs)
        {
            throw new ArgumentOutOfRangeException(nameof(tab), tab.Order,
                                                  "The terminal workspace tab order is outside the supported range.");
        }

        if (string.IsNullOrWhiteSpace(tab.Name) == true || tab.Name != tab.Name.Trim() ||
            StringInfo.ParseCombiningCharacters(tab.Name).Length > MaximumTabNameTextElements ||
            tab.Name.Any(char.IsControl) == true)
        {
            throw new ArgumentException("A terminal tab name must contain 1 to 32 non-control text elements without surrounding whitespace.",
                                        nameof(tab));
        }

        if (tab.StartingDirectory is null || tab.StartingDirectory.Length > MaximumStartingDirectoryCharacters ||
            TerminalStartingDirectory.IsSupportedLocalAbsolutePath(tab.StartingDirectory) == false)
        {
            throw new ArgumentException("The terminal starting directory must be a local absolute Windows path.", nameof(tab));
        }

        if (TerminalLaunchProfileId.TryCreate(tab.LaunchProfileId.Value, out var profileId) == false)
        {
            throw new ArgumentException("The workspace launch profile identifier is invalid.", nameof(tab));
        }

        if (Enum.IsDefined(tab.ShellKind) == false ||
            (profileId.IsBuiltInShell == true && profileId.ToShellKind() != tab.ShellKind) ||
            (profileId.IsBuiltInShell == false && tab.ShellKind != TerminalShellKind.Automatic))
        {
            throw new ArgumentException("The workspace shell kind conflicts with its launch profile.", nameof(tab));
        }

        if (schemaVersion == 1 && profileId.IsBuiltInShell == false)
        {
            throw new ArgumentException("Schema 1 supports only built-in shell launch profiles.", nameof(tab));
        }
    }
}
