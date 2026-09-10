using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Stable limits shared by saved-tab persistence, launch policy, and renderer presentation.
/// </summary>
public static class TerminalSavedTabsContract
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumSavedTabs = 20;
    public const int MaximumRunningTabs = 8;
    public const int MaximumNameTextElements = 32;
    public const int MaximumStartingDirectoryCharacters = 32_767;
}

/// <summary>
/// Stable identity of a user-created saved tab. It is unrelated to workspace configuration and
/// runtime session identities.
/// </summary>
public readonly record struct TerminalSavedTabId
{
    public TerminalSavedTabId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The saved terminal tab identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TerminalSavedTabId CreateNew()
    {
        return new TerminalSavedTabId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("N");
    }
}

/// <summary>
/// A reusable shell launch definition. It contains no runtime session, command, output, process,
/// environment, history, clipboard, or scrollback state.
/// </summary>
public sealed record TerminalSavedTab
{
    public TerminalSavedTab(TerminalSavedTabId savedTabId, string name, string startingDirectory,
                            TerminalShellKind shellKind)
    {
        if (savedTabId.Value == Guid.Empty)
        {
            throw new ArgumentException("A saved terminal tab requires a valid identifier.", nameof(savedTabId));
        }

        TerminalSavedTabContractValidator.ValidateDefinition(name, startingDirectory, shellKind);
        SavedTabId = savedTabId;
        Name = name;
        StartingDirectory = startingDirectory;
        ShellKind = shellKind;
    }

    public TerminalSavedTabId SavedTabId { get; }

    public string Name { get; }

    /// <summary>
    /// A syntactically valid local absolute Windows path. Existence is checked again at save and launch time.
    /// </summary>
    public string StartingDirectory { get; }

    public TerminalShellKind ShellKind { get; }
}

/// <summary>
/// Immutable persisted list of saved tabs. An empty list is valid and is independent of the
/// opt-in terminal workspace restore document.
/// </summary>
public sealed class TerminalSavedTabsSnapshot
{
    private readonly ReadOnlyCollection<TerminalSavedTab> _tabs;

    public TerminalSavedTabsSnapshot(int schemaVersion, IReadOnlyList<TerminalSavedTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (schemaVersion != TerminalSavedTabsContract.CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion,
                                                  "The saved terminal tabs schema version is not supported.");
        }

        if (tabs.Count > TerminalSavedTabsContract.MaximumSavedTabs)
        {
            throw new ArgumentException(
                $"A saved terminal tabs snapshot cannot contain more than " +
                $"{TerminalSavedTabsContract.MaximumSavedTabs} items.",
                nameof(tabs));
        }

        var capturedTabs = tabs.ToArray();
        if (capturedTabs.Any(tab => tab is null) == true)
        {
            throw new ArgumentException("A saved terminal tabs snapshot cannot contain null items.", nameof(tabs));
        }

        if (capturedTabs.Select(tab => tab.SavedTabId).Distinct().Count() != capturedTabs.Length)
        {
            throw new ArgumentException("Saved terminal tab identifiers must be unique.", nameof(tabs));
        }

        SchemaVersion = schemaVersion;
        _tabs = Array.AsReadOnly(capturedTabs);
    }

    public int SchemaVersion { get; }

    public IReadOnlyList<TerminalSavedTab> Tabs => _tabs;
}

internal static partial class TerminalSavedTabContractValidator
{
    private static readonly Regex _localAbsolutePathPattern = CreateLocalAbsolutePathPattern();
    private static readonly Regex _expansionPattern = CreateExpansionPattern();

    internal static void ValidateDefinition(string name, string startingDirectory, TerminalShellKind shellKind)
    {
        if (string.IsNullOrWhiteSpace(name) == true || name != name.Trim() || name.Any(char.IsControl) == true ||
            StringInfo.ParseCombiningCharacters(name).Length > TerminalSavedTabsContract.MaximumNameTextElements)
        {
            throw new ArgumentException(
                $"A saved terminal tab name must contain 1 to {TerminalSavedTabsContract.MaximumNameTextElements} " +
                "non-control text elements without surrounding whitespace.", nameof(name));
        }

        if (IsSupportedLocalAbsolutePath(startingDirectory) == false)
        {
            throw new ArgumentException(
                "A saved terminal tab starting directory must be a local absolute Windows path.",
                                        nameof(startingDirectory));
        }

        if (Enum.IsDefined(shellKind) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind,
                                                  "The saved terminal tab shell kind is not supported.");
        }
    }

    private static bool IsSupportedLocalAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) == true || path != path.Trim() ||
            path.Length > TerminalSavedTabsContract.MaximumStartingDirectoryCharacters ||
            path.Any(char.IsControl) == true || _localAbsolutePathPattern.IsMatch(path) == false ||
            path.StartsWith("\\\\", StringComparison.Ordinal) == true ||
            path.StartsWith("//", StringComparison.Ordinal) == true ||
            _expansionPattern.IsMatch(path) == true || path.Contains('`', StringComparison.Ordinal) == true)
        {
            return false;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path);
            return _localAbsolutePathPattern.IsMatch(normalizedPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CreateLocalAbsolutePathPattern();

    [GeneratedRegex("%[^%\\r\\n]+%|![^!\\r\\n]+!|\\$(?:[A-Za-z_][A-Za-z0-9_]*|[0-9]+|\\{|\\()",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 100)]
    private static partial Regex CreateExpansionPattern();
}
