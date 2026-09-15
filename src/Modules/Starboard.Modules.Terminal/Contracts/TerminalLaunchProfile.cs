using System.Collections.ObjectModel;

namespace Starboard.Modules.Terminal.Contracts;

/// <summary>
/// Stable, protocol-safe identity for one launch profile. Built-in shell identifiers are independent
/// from display labels, while a WSL identifier follows the distribution registration name.
/// </summary>
public readonly record struct TerminalLaunchProfileId
{
    private const int _MaximumLength = 512;

    public TerminalLaunchProfileId(string value)
    {
        if (IsValid(value) == false)
        {
            throw new ArgumentException("The terminal launch profile identifier is invalid.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static TerminalLaunchProfileId FromShell(TerminalShellKind shellKind)
    {
        var suffix = shellKind switch
        {
            TerminalShellKind.Automatic => "automatic",
            TerminalShellKind.Pwsh => "pwsh",
            TerminalShellKind.PowerShell => "powershell",
            TerminalShellKind.Cmd => "cmd",
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind,
                                                       "The launch profile shell kind is not supported."),
        };

        return new TerminalLaunchProfileId($"shell:{suffix}");
    }

    public static TerminalLaunchProfileId FromWslDistribution(string distributionName)
    {
        ValidateDistributionName(distributionName);
        return new TerminalLaunchProfileId($"wsl:{distributionName}");
    }

    public static bool TryCreate(string? value, out TerminalLaunchProfileId profileId)
    {
        profileId = default;
        if (IsValid(value) == false)
        {
            return false;
        }

        profileId = new TerminalLaunchProfileId(value!);
        return true;
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    internal static void ValidateDistributionName(string distributionName)
    {
        if (IsValidDistributionName(distributionName) == false)
        {
            throw new ArgumentException("The WSL distribution name is invalid.", nameof(distributionName));
        }
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) == true || value.Length > _MaximumLength ||
            value.Any(char.IsControl) == true)
        {
            return false;
        }

        if (value is "shell:automatic" or "shell:pwsh" or "shell:powershell" or "shell:cmd")
        {
            return true;
        }

        return value.StartsWith("wsl:", StringComparison.Ordinal) &&
               IsValidDistributionName(value[4..]);
    }

    private static bool IsValidDistributionName(string? distributionName)
    {
        return string.IsNullOrWhiteSpace(distributionName) == false && distributionName.Length <= 256 &&
               string.Equals(distributionName, distributionName.Trim(), StringComparison.Ordinal) &&
               distributionName.Any(char.IsControl) == false;
    }
}

public enum TerminalLaunchProfileKind
{
    BuiltInShell,
    WslDistribution,
}

/// <summary>
/// Immutable description of one locally available terminal launch target. A WSL distribution is
/// represented by data instead of extending <see cref="TerminalShellKind"/> dynamically.
/// </summary>
public sealed record TerminalLaunchProfile
{
    private TerminalLaunchProfile(TerminalLaunchProfileId profileId, TerminalLaunchProfileKind kind,
                                  string displayName, TerminalShellKind? shellKind, string? wslDistributionName)
    {
        if (profileId.Value is null)
        {
            throw new ArgumentException("A launch profile requires a valid profile identifier.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(displayName) == true || displayName.Length > 256 ||
            displayName.Any(char.IsControl) == true)
        {
            throw new ArgumentException("A launch profile requires a valid display name.", nameof(displayName));
        }

        if (Enum.IsDefined(kind) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The launch profile kind is not supported.");
        }

        ProfileId = profileId;
        Kind = kind;
        DisplayName = displayName;
        ShellKind = shellKind;
        WslDistributionName = wslDistributionName;
    }

    public TerminalLaunchProfileId ProfileId { get; }

    public TerminalLaunchProfileKind Kind { get; }

    public string DisplayName { get; }

    public TerminalShellKind? ShellKind { get; }

    public string? WslDistributionName { get; }

    public static TerminalLaunchProfile CreateBuiltIn(TerminalShellKind shellKind, string displayName)
    {
        if (Enum.IsDefined(shellKind) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind,
                                                  "The launch profile shell kind is not supported.");
        }

        return new TerminalLaunchProfile(TerminalLaunchProfileId.FromShell(shellKind),
                                         TerminalLaunchProfileKind.BuiltInShell, displayName, shellKind, null);
    }

    public static TerminalLaunchProfile CreateWsl(string distributionName, string? displayName = null)
    {
        TerminalLaunchProfileId.ValidateDistributionName(distributionName);
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) == true
            ? distributionName
            : displayName;

        return new TerminalLaunchProfile(TerminalLaunchProfileId.FromWslDistribution(distributionName),
                                         TerminalLaunchProfileKind.WslDistribution, resolvedDisplayName,
                                         null, distributionName);
    }
}

public enum TerminalLaunchProfileQueryStatus
{
    Succeeded,
    WslUnavailable,
    WslDiscoveryFailed,
}

/// <summary>
/// One profile discovery snapshot. WSL discovery failure remains distinct from an authoritative
/// empty WSL list, allowing the renderer to offer retry without hiding available built-in shells.
/// </summary>
public sealed record TerminalLaunchProfileQueryResult
{
    public TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus status,
                                            IEnumerable<TerminalLaunchProfile> profiles,
                                            string? failureMessage = null)
    {
        if (Enum.IsDefined(status) == false)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status,
                                                  "The launch profile query status is not supported.");
        }

        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = profiles.ToArray();
        if (snapshot.Any(profile => profile is null) == true)
        {
            throw new ArgumentException("A launch profile query cannot contain a null profile.", nameof(profiles));
        }

        if (snapshot.Select(profile => profile.ProfileId).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("A launch profile query cannot contain duplicate profile identifiers.",
                                        nameof(profiles));
        }

        if (status == TerminalLaunchProfileQueryStatus.WslDiscoveryFailed &&
            string.IsNullOrWhiteSpace(failureMessage) == true)
        {
            throw new ArgumentException("A failed WSL discovery requires a user-safe failure message.",
                                        nameof(failureMessage));
        }

        if (status != TerminalLaunchProfileQueryStatus.WslDiscoveryFailed && failureMessage is not null)
        {
            throw new ArgumentException("A successful or unavailable WSL query cannot include a failure message.",
                                        nameof(failureMessage));
        }

        Status = status;
        Profiles = new ReadOnlyCollection<TerminalLaunchProfile>(snapshot);
        FailureMessage = failureMessage;
    }

    public TerminalLaunchProfileQueryStatus Status { get; }

    public IReadOnlyList<TerminalLaunchProfile> Profiles { get; }

    public string? FailureMessage { get; }

    public bool CanRetry => Status == TerminalLaunchProfileQueryStatus.WslDiscoveryFailed;
}
