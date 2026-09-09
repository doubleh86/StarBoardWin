using System.IO;

namespace Starboard.Windows.Composition;

internal sealed record PortableReleaseSmokeResult(int ExitCode, string Message);

internal static class PortableReleaseSmokeCheck
{
    private const string _SmokeSwitch = "--portable-smoke-test";
    private const string _VersionOption = "--expected-version";
    private const string _CommitOption = "--expected-commit";

    private static readonly string[] _RequiredRelativePaths =
    [
        "Starboard.exe",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "release-metadata.json",
        Path.Combine("Renderer", "index.html"),
        Path.Combine("Renderer", "app.js"),
        Path.Combine("Renderer", "app.css"),
    ];

    private static readonly string[] _DefaultShellNames =
    [
        "pwsh.exe",
        "powershell.exe",
        "cmd.exe",
    ];

    internal static PortableReleaseSmokeResult? TryRun(IReadOnlyList<string> arguments, ProductBuildInfo buildInfo,
                                                       string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(buildInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        if (arguments.Contains(_SmokeSwitch, StringComparer.Ordinal) == false)
        {
            return null;
        }

        return Validate(arguments, buildInfo, baseDirectory, File.Exists, FindDefaultShell);
    }

    internal static PortableReleaseSmokeResult Validate(IReadOnlyList<string> arguments, ProductBuildInfo buildInfo,
                                                        string baseDirectory, Func<string, bool> fileExists,
                                                        Func<string?> findDefaultShell)
    {
        var expectedVersion = GetOption(arguments, _VersionOption);
        var expectedCommit = GetOption(arguments, _CommitOption);
        if (expectedVersion is null || expectedCommit is null)
        {
            return new PortableReleaseSmokeResult(2, "Expected version and commit arguments are required.");
        }

        if (buildInfo.Matches(expectedVersion, expectedCommit) == false)
        {
            return new PortableReleaseSmokeResult(3, "Assembly build metadata does not match the package metadata.");
        }

        foreach (var relativePath in _RequiredRelativePaths)
        {
            var path = Path.Combine(baseDirectory, relativePath);
            if (fileExists(path) == false)
            {
                return new PortableReleaseSmokeResult(4, $"Required portable file is missing: {relativePath}");
            }
        }

        var shellPath = findDefaultShell();
        if (string.IsNullOrWhiteSpace(shellPath) == true)
        {
            return new PortableReleaseSmokeResult(5, "No supported default shell executable is available.");
        }

        return new PortableReleaseSmokeResult(0, $"Portable release is ready with shell {Path.GetFileName(shellPath)}.");
    }

    private static string? GetOption(IReadOnlyList<string> arguments, string optionName)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], optionName, StringComparison.Ordinal) == true)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static string? FindDefaultShell()
    {
        var searchDirectories = GetShellSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var shellName in _DefaultShellNames)
        {
            foreach (var directory in searchDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory) == true)
                {
                    continue;
                }

                var candidate = Path.Combine(directory.Trim().Trim('"'), shellName);
                if (File.Exists(candidate) == true)
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetShellSearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator,
                                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return directory;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        yield return Path.Combine(programFiles, "PowerShell", "7");
        yield return Path.Combine(localApplicationData, "Microsoft", "WindowsApps");
        yield return Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0");
        yield return Path.Combine(windows, "System32");
    }
}
