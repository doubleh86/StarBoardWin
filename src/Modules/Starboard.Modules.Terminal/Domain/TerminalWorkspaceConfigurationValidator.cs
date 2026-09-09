using System.Globalization;
using System.Text.RegularExpressions;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Domain;

internal static class TerminalWorkspaceConfigurationValidator
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumTabs = TerminalTabRegistry.DefaultMaximumTabs;
    internal const int MaximumTabNameTextElements = 32;

    private static readonly Regex _localAbsolutePathPattern = new("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant,
                                                                  TimeSpan.FromMilliseconds(100));

    internal static void Validate(TerminalWorkspaceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion != CurrentSchemaVersion)
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
            ValidateTab(tab);
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

    private static void ValidateTab(TerminalWorkspaceTabConfiguration tab)
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

        if (string.IsNullOrWhiteSpace(tab.StartingDirectory) == true ||
            _localAbsolutePathPattern.IsMatch(tab.StartingDirectory) == false ||
            tab.StartingDirectory.StartsWith("\\\\", StringComparison.Ordinal) == true)
        {
            throw new ArgumentException("The terminal starting directory must be a local absolute Windows path.", nameof(tab));
        }

        if (Enum.IsDefined(tab.ShellKind) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(tab), tab.ShellKind,
                                                  "The terminal shell kind is not supported for workspace restore.");
        }
    }
}
