using System.ComponentModel;
using System.IO;
using System.Text;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Application;

internal sealed class TerminalLaunchProfileCatalog
{
    private static readonly TimeSpan DefaultDiscoveryTimeout = TimeSpan.FromSeconds(3);
    private static readonly string[] WslListArguments = ["--list", "--quiet"];

    private readonly IWslProcessRunner wslProcessRunner;
    private readonly IDiagnosticLog diagnosticLog;
    private readonly Func<string, string?> executableResolver;
    private readonly TimeSpan discoveryTimeout;

    internal TerminalLaunchProfileCatalog(IWslProcessRunner wslProcessRunner, IDiagnosticLog diagnosticLog,
                                          Func<string, string?>? executableResolver = null,
                                          TimeSpan? discoveryTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(wslProcessRunner);
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        this.wslProcessRunner = wslProcessRunner;
        this.diagnosticLog = diagnosticLog;
        this.executableResolver = executableResolver ?? (executable => ShellResolver.FindExecutable(executable));
        this.discoveryTimeout = discoveryTimeout ?? DefaultDiscoveryTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.discoveryTimeout, TimeSpan.Zero,
                                                           nameof(discoveryTimeout));
    }

    internal async Task<TerminalLaunchProfileQueryResult> QueryAsync(CancellationToken cancellationToken)
    {
        var profiles = GetInstalledBuiltInProfiles().ToList();
        var wslExecutable = executableResolver("wsl.exe");
        if (wslExecutable is null)
        {
            return new TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus.WslUnavailable, profiles);
        }

        try
        {
            var result = await wslProcessRunner.RunAsync(wslExecutable, WslListArguments,
                                                         discoveryTimeout, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return new TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus.WslUnavailable,
                                                            profiles);
            }

            foreach (var distributionName in ParseDistributionNames(result.StandardOutput))
            {
                profiles.Add(TerminalLaunchProfile.CreateWsl(distributionName));
            }

            return new TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus.Succeeded, profiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is TimeoutException or Win32Exception or IOException or InvalidOperationException)
        {
            diagnosticLog.Write(DiagnosticLevel.Warning, "Terminal", "DiscoverWslProfiles",
                                "WSL distribution discovery failed without affecting built-in shells.", exception);
            return new TerminalLaunchProfileQueryResult(TerminalLaunchProfileQueryStatus.WslDiscoveryFailed,
                                                        profiles, "WSL 배포판 목록을 불러오지 못했습니다.");
        }
    }

    internal ShellLaunchSpec Resolve(TerminalLaunchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Kind == TerminalLaunchProfileKind.BuiltInShell && profile.ShellKind is { } shellKind)
        {
            return ResolveBuiltInShell(shellKind);
        }

        if (profile.Kind != TerminalLaunchProfileKind.WslDistribution || profile.WslDistributionName is null)
        {
            throw new ArgumentException("The terminal launch profile is incomplete.", nameof(profile));
        }

        var wslExecutable = executableResolver("wsl.exe");
        if (wslExecutable is null)
        {
            throw new FileNotFoundException("wsl.exe를 찾을 수 없습니다.");
        }

        var workingDirectory = GetDefaultWorkingDirectory();
        string[] arguments = ["--distribution", profile.WslDistributionName, "--cd", "~"];
        return new ShellLaunchSpec(wslExecutable, arguments, workingDirectory);
    }

    private ShellLaunchSpec ResolveBuiltInShell(TerminalShellKind shellKind)
    {
        if (shellKind == TerminalShellKind.Automatic)
        {
            return ShellResolver.Resolve(null);
        }

        var executableName = shellKind switch
        {
            TerminalShellKind.Pwsh => "pwsh.exe",
            TerminalShellKind.PowerShell => "powershell.exe",
            TerminalShellKind.Cmd => "cmd.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind,
                                                       "The terminal shell kind is not supported."),
        };
        var executablePath = executableResolver(executableName);
        if (executablePath is null)
        {
            throw new FileNotFoundException("선택한 shell 실행 파일을 찾을 수 없습니다.", executableName);
        }

        string[] arguments = shellKind == TerminalShellKind.Cmd ? ["/Q"] : ["-NoLogo"];
        var workingDirectory = GetDefaultWorkingDirectory();
        return new ShellLaunchSpec(executablePath, arguments, workingDirectory);
    }

    private static string GetDefaultWorkingDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile) == false ? userProfile : Environment.CurrentDirectory;
    }

    private IEnumerable<TerminalLaunchProfile> GetInstalledBuiltInProfiles()
    {
        if (executableResolver("pwsh.exe") is not null)
        {
            yield return TerminalLaunchProfile.CreateBuiltIn(TerminalShellKind.Pwsh, "PowerShell 7");
        }

        if (executableResolver("powershell.exe") is not null)
        {
            yield return TerminalLaunchProfile.CreateBuiltIn(TerminalShellKind.PowerShell, "Windows PowerShell");
        }

        if (executableResolver("cmd.exe") is not null)
        {
            yield return TerminalLaunchProfile.CreateBuiltIn(TerminalShellKind.Cmd, "Command Prompt");
        }
    }

    private static List<string> ParseDistributionNames(byte[] output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var text = DecodeOutput(output).Replace("\0", string.Empty, StringComparison.Ordinal);
        var names = new List<string>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = line.Trim().TrimStart('\uFEFF');
            if (name.Length == 0 || name.Length > 256 || name.Any(char.IsControl) == true ||
                uniqueNames.Add(name) == false)
            {
                continue;
            }

            names.Add(name);
        }

        return names;
    }

    private static string DecodeOutput(byte[] output)
    {
        if (output.Length >= 2 && output[0] == 0xFF && output[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(output, 2, output.Length - 2);
        }

        if (output.Length >= 2 && output[0] == 0xFE && output[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(output, 2, output.Length - 2);
        }

        var looksLikeUtf16LittleEndian = output.Length >= 2 &&
                                         Enumerable.Range(1, output.Length / 2)
                                             .Count(index => output[(index * 2) - 1] == 0) > output.Length / 8;
        return looksLikeUtf16LittleEndian
            ? Encoding.Unicode.GetString(output)
            : new UTF8Encoding(false, false).GetString(output);
    }
}
