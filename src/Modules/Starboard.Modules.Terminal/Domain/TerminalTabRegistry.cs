using Starboard.Modules.Terminal.Contracts;
using System.Globalization;

namespace Starboard.Modules.Terminal.Domain;

internal sealed record TerminalTabCloseResult(TerminalTab ClosedTab, TerminalTab? ReplacementTab);

internal sealed class TerminalTabRegistry
{
    internal const int DefaultMaximumTabs = 8;

    private readonly Func<TerminalSessionId> sessionIdFactory;
    private readonly Func<TerminalTabConfigurationId> configurationIdFactory;
    private readonly int maximumTabs;
    private readonly List<TerminalTab> tabs = [];

    private int nextTabNumber = 1;

    internal TerminalTabRegistry(Func<TerminalSessionId>? sessionIdFactory = null, int maximumTabs = DefaultMaximumTabs,
                                 Func<TerminalTabConfigurationId>? configurationIdFactory = null)
    {
        if (maximumTabs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTabs), maximumTabs,
                                                  "The maximum tab count must be positive.");
        }

        this.sessionIdFactory = sessionIdFactory ?? TerminalSessionId.CreateNew;
        this.configurationIdFactory = configurationIdFactory ?? TerminalTabConfigurationId.CreateNew;
        this.maximumTabs = maximumTabs;
    }

    internal TerminalSessionId? ActiveSessionId { get; private set; }

    internal bool IsAtCapacity => tabs.Count >= maximumTabs;

    internal TerminalTab Add(string startingDirectory, TerminalShellKind? shellKind = TerminalShellKind.Automatic)
    {
        return Add(startingDirectory, shellKind, null);
    }

    internal TerminalTab Add(string startingDirectory, TerminalShellKind? shellKind, string? name)
    {
        if (IsAtCapacity == true)
        {
            throw new InvalidOperationException($"Terminal tab limit ({maximumTabs}) has been reached.");
        }

        var sessionId = sessionIdFactory();
        if (sessionId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("The session identifier cannot be empty.");
        }

        if (tabs.Any(tab => tab.SessionId == sessionId) == true)
        {
            throw new InvalidOperationException("The session identifier must be unique.");
        }

        var configurationId = configurationIdFactory();
        if (configurationId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("The terminal tab configuration identifier cannot be empty.");
        }

        if (tabs.Any(tab => tab.ConfigurationId == configurationId) == true)
        {
            throw new InvalidOperationException("The terminal tab configuration identifier must be unique.");
        }

        var tabName = name ?? $"PowerShell {nextTabNumber}";
        if (TryNormalizeName(tabName, out var normalizedName) == false)
        {
            throw new ArgumentException("The terminal tab name is invalid.", nameof(name));
        }

        var tab = new TerminalTab(sessionId, configurationId, normalizedName, startingDirectory,
                                  shellKind, TerminalSessionState.Starting, null);
        nextTabNumber++;
        tabs.Add(tab);
        ActiveSessionId = sessionId;

        return tab;
    }

    internal TerminalTab AddRestored(TerminalWorkspaceTabConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (tabs.Count >= maximumTabs)
        {
            throw new InvalidOperationException($"Terminal tab limit ({maximumTabs}) has been reached.");
        }

        var sessionId = sessionIdFactory();
        if (sessionId.Value == Guid.Empty || tabs.Any(tab => tab.SessionId == sessionId) == true)
        {
            throw new InvalidOperationException("The restored runtime session identifier must be non-empty and unique.");
        }

        if (tabs.Any(tab => tab.ConfigurationId == configuration.ConfigurationId) == true)
        {
            throw new InvalidOperationException("The restored terminal configuration identifier must be unique.");
        }

        var tab = new TerminalTab(sessionId, configuration.ConfigurationId, configuration.Name,
                                  configuration.StartingDirectory, configuration.ShellKind,
                                  TerminalSessionState.Starting, null);
        tabs.Add(tab);
        ActiveSessionId = sessionId;

        return tab;
    }

    internal bool SelectConfiguration(TerminalTabConfigurationId configurationId)
    {
        var tab = tabs.FirstOrDefault(candidate => candidate.ConfigurationId == configurationId);
        return tab is not null && Select(tab.SessionId);
    }

    internal bool Select(TerminalSessionId sessionId)
    {
        if (tabs.Any(tab => tab.SessionId == sessionId) == false)
        {
            return false;
        }

        ActiveSessionId = sessionId;

        return true;
    }

    internal bool SelectNext()
    {
        return SelectOffset(1);
    }

    internal bool SelectPrevious()
    {
        return SelectOffset(-1);
    }

    internal bool Rename(TerminalSessionId sessionId, string? name)
    {
        if (TryNormalizeName(name, out var normalizedName) == false)
        {
            return false;
        }

        var index = tabs.FindIndex(tab => tab.SessionId == sessionId);
        if (index < 0)
        {
            return false;
        }

        tabs[index] = tabs[index] with { Name = normalizedName };

        return true;
    }

    internal bool MoveLeft(TerminalSessionId sessionId)
    {
        return Move(sessionId, -1);
    }

    internal bool MoveRight(TerminalSessionId sessionId)
    {
        return Move(sessionId, 1);
    }

    internal void SetStartingDirectory(TerminalSessionId sessionId, string startingDirectory)
    {
        var index = tabs.FindIndex(tab => tab.SessionId == sessionId);
        if (index < 0)
        {
            throw new InvalidOperationException("The terminal tab does not exist.");
        }

        tabs[index] = tabs[index] with { StartingDirectory = startingDirectory };
    }

    internal void SetShellKind(TerminalSessionId sessionId, TerminalShellKind? shellKind)
    {
        var index = tabs.FindIndex(tab => tab.SessionId == sessionId);
        if (index < 0)
        {
            throw new InvalidOperationException("The terminal tab does not exist.");
        }

        tabs[index] = tabs[index] with { ShellKind = shellKind };
    }

    internal TerminalTabCloseResult? Close(TerminalSessionId sessionId)
    {
        var closedIndex = tabs.FindIndex(tab => tab.SessionId == sessionId);
        if (closedIndex < 0)
        {
            return null;
        }

        var closedTab = tabs[closedIndex];
        var wasActive = ActiveSessionId == sessionId;
        tabs.RemoveAt(closedIndex);

        TerminalTab? replacementTab = null;
        if (tabs.Count == 0)
        {
            replacementTab = Add(closedTab.StartingDirectory);
        }
        else if (wasActive == true)
        {
            var adjacentIndex = Math.Min(closedIndex, tabs.Count - 1);
            ActiveSessionId = tabs[adjacentIndex].SessionId;
        }

        return new TerminalTabCloseResult(closedTab, replacementTab);
    }

    internal TerminalTab GetRequired(TerminalSessionId sessionId)
    {
        return tabs.FirstOrDefault(tab => tab.SessionId == sessionId)
            ?? throw new InvalidOperationException("The terminal tab does not exist.");
    }

    internal bool Contains(TerminalSessionId sessionId)
    {
        return tabs.Any(tab => tab.SessionId == sessionId);
    }

    internal void SetState(TerminalSessionId sessionId, TerminalSessionState state, uint? exitCode = null)
    {
        var index = tabs.FindIndex(tab => tab.SessionId == sessionId);
        if (index < 0)
        {
            throw new InvalidOperationException("The terminal tab does not exist.");
        }

        tabs[index] = tabs[index] with
        {
            State = state,
            ExitCode = exitCode,
        };
    }

    internal TerminalWorkspaceSnapshot CreateSnapshot()
    {
        return new TerminalWorkspaceSnapshot(tabs.ToArray(), ActiveSessionId);
    }

    private bool SelectOffset(int offset)
    {
        if (tabs.Count == 0 || ActiveSessionId is null)
        {
            return false;
        }

        var activeIndex = tabs.FindIndex(tab => tab.SessionId == ActiveSessionId.Value);
        if (activeIndex < 0)
        {
            throw new InvalidOperationException("The active terminal tab is not registered.");
        }

        var selectedIndex = (activeIndex + offset + tabs.Count) % tabs.Count;
        ActiveSessionId = tabs[selectedIndex].SessionId;

        return true;
    }

    private bool Move(TerminalSessionId sessionId, int offset)
    {
        var currentIndex = tabs.FindIndex(tab => tab.SessionId == sessionId);
        var destinationIndex = currentIndex + offset;
        if (currentIndex < 0 || destinationIndex < 0 || destinationIndex >= tabs.Count)
        {
            return false;
        }

        (tabs[currentIndex], tabs[destinationIndex]) = (tabs[destinationIndex], tabs[currentIndex]);

        return true;
    }

    private static bool TryNormalizeName(string? name, out string normalizedName)
    {
        normalizedName = string.Empty;
        if (name is null)
        {
            return false;
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length == 0 || trimmedName.Any(char.IsControl) == true)
        {
            return false;
        }

        var textElementCount = StringInfo.ParseCombiningCharacters(trimmedName).Length;
        if (textElementCount < 1 || textElementCount > 32)
        {
            return false;
        }

        normalizedName = trimmedName;
        return true;
    }
}
