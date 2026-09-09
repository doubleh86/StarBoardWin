using System.IO;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Application;

internal static class ShellResolver
{
    private static readonly string[] DefaultShellNames =
    [
        "pwsh.exe",
        "powershell.exe",
        "cmd.exe",
    ];

    internal static ShellLaunchSpec Resolve(string? configuredExecutable)
    {
        var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(configuredExecutable) == false)
        {
            var configuredPath = FindExecutable(configuredExecutable.Trim());
            if (configuredPath is null)
            {
                throw new FileNotFoundException("설정한 shell 실행 파일을 찾을 수 없습니다.", configuredExecutable);
            }

            return CreateSpec(configuredPath, workingDirectory);
        }

        foreach (var shellName in DefaultShellNames)
        {
            var executablePath = FindExecutable(shellName);
            if (executablePath is not null)
            {
                return CreateSpec(executablePath, workingDirectory);
            }
        }

        throw new FileNotFoundException("pwsh.exe, powershell.exe 또는 cmd.exe를 찾을 수 없습니다.");
    }

    internal static ShellLaunchSpec Resolve(TerminalShellKind shellKind)
    {
        return shellKind switch
        {
            TerminalShellKind.Automatic => Resolve(null),
            TerminalShellKind.Pwsh => Resolve("pwsh.exe"),
            TerminalShellKind.PowerShell => Resolve("powershell.exe"),
            TerminalShellKind.Cmd => Resolve("cmd.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind,
                                                       "The terminal shell kind is not supported."),
        };
    }

    internal static TerminalShellKind? GetConfiguredKind(string? configuredExecutable)
    {
        if (string.IsNullOrWhiteSpace(configuredExecutable) == true)
        {
            return TerminalShellKind.Automatic;
        }

        var fileName = Path.GetFileName(configuredExecutable.Trim());
        if (fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return TerminalShellKind.Pwsh;
        }

        if (fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return TerminalShellKind.PowerShell;
        }

        if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return TerminalShellKind.Cmd;
        }

        return null;
    }

    internal static string? FindExecutable(string executable, IEnumerable<string>? pathDirectories = null)
    {
        if (Path.IsPathFullyQualified(executable) == true)
        {
            return File.Exists(executable) == true
                ? Path.GetFullPath(executable)
                : null;
        }

        var directories = pathDirectories ?? GetSearchDirectories();
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) == true)
            {
                continue;
            }

            var candidate = Path.Combine(directory.Trim().Trim('"'), executable);
            if (File.Exists(candidate) == true)
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchDirectories()
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

    private static ShellLaunchSpec CreateSpec(string executablePath, string workingDirectory)
    {
        var fileName = Path.GetFileName(executablePath);
        var arguments = fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            ? "/Q"
            : "-NoLogo";

        return new ShellLaunchSpec(executablePath, arguments, workingDirectory);
    }
}
