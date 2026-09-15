using System.Text;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalLaunchProfileCatalogTests
{
    private static readonly string[] ExpectedProfiles =
        ["shell:pwsh", "shell:cmd", "wsl:Ubuntu-24.04", "wsl:개발 환경"];
    private static readonly string[] ExpectedListArguments = ["--list", "--quiet"];
    private static readonly string[] ExpectedPowerShellProfile = ["shell:powershell"];
    private static readonly string[] ExpectedCmdProfile = ["shell:cmd"];
    private static readonly string[] ExpectedWslLaunchArguments =
        ["--distribution", "개발 Ubuntu 24.04", "--cd", "~"];

    [TestMethod]
    public async Task QueryAsyncInstalledShellsAndUnicodeWslOutputReturnsOnlyAvailableProfiles()
    {
        var runner = new FakeWslProcessRunner
        {
            Result = new WslProcessResult(0, Encoding.Unicode.GetBytes("Ubuntu-24.04\r\n개발 환경\r\n")),
        };
        var executables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pwsh.exe"] = "C:\\PowerShell\\pwsh.exe",
            ["cmd.exe"] = "C:\\Windows\\cmd.exe",
            ["wsl.exe"] = "C:\\Windows\\wsl.exe",
        };
        var catalog = CreateCatalog(runner, executable => executables.GetValueOrDefault(executable));

        var result = await catalog.QueryAsync(CancellationToken.None);

        Assert.AreEqual(TerminalLaunchProfileQueryStatus.Succeeded, result.Status);
        CollectionAssert.AreEqual(ExpectedProfiles,
                                  result.Profiles.Select(profile => profile.ProfileId.ToString()).ToArray());
        CollectionAssert.AreEqual(ExpectedListArguments, runner.Arguments.ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), runner.Timeout);
    }

    [TestMethod]
    public async Task QueryAsyncMissingWslOrNoDistributionsKeepsInstalledBuiltInShells()
    {
        var missingRunner = new FakeWslProcessRunner();
        var missingCatalog = CreateCatalog(missingRunner, executable => executable == "powershell.exe"
            ? "C:\\Windows\\powershell.exe"
            : null);

        var missing = await missingCatalog.QueryAsync(CancellationToken.None);

        Assert.AreEqual(TerminalLaunchProfileQueryStatus.WslUnavailable, missing.Status);
        CollectionAssert.AreEqual(ExpectedPowerShellProfile,
                                  missing.Profiles.Select(profile => profile.ProfileId.ToString()).ToArray());
        Assert.AreEqual(0, missingRunner.CallCount);

        var emptyRunner = new FakeWslProcessRunner
        {
            Result = new WslProcessResult(0, Encoding.UTF8.GetBytes("\r\n")),
        };
        var emptyCatalog = CreateCatalog(emptyRunner, executable => executable is "cmd.exe" or "wsl.exe"
            ? $"C:\\Windows\\{executable}"
            : null);

        var empty = await emptyCatalog.QueryAsync(CancellationToken.None);

        Assert.AreEqual(TerminalLaunchProfileQueryStatus.Succeeded, empty.Status);
        CollectionAssert.AreEqual(ExpectedCmdProfile,
                                  empty.Profiles.Select(profile => profile.ProfileId.ToString()).ToArray());
    }

    [TestMethod]
    public async Task QueryAsyncTimeoutReturnsRetryableFailureWithoutHidingBuiltInShells()
    {
        var runner = new FakeWslProcessRunner
        {
            Exception = new TimeoutException("simulated timeout"),
        };
        var catalog = CreateCatalog(runner, executable => $"C:\\Available\\{executable}");

        var result = await catalog.QueryAsync(CancellationToken.None);

        Assert.AreEqual(TerminalLaunchProfileQueryStatus.WslDiscoveryFailed, result.Status);
        Assert.IsTrue(result.CanRetry);
        Assert.HasCount(3, result.Profiles);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FailureMessage));
    }

    [TestMethod]
    public async Task QueryAsyncCallerCancellationPropagatesWithoutStartingOrChangingAnySession()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new FakeWslProcessRunner
        {
            Exception = new OperationCanceledException(cancellation.Token),
        };
        var catalog = CreateCatalog(runner, executable => $"C:\\Available\\{executable}");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => catalog.QueryAsync(cancellation.Token));
    }

    [TestMethod]
    public void ResolveWslUsesSeparateArgumentsAndLinuxHomeWithoutQuotingDistributionName()
    {
        var runner = new FakeWslProcessRunner();
        var catalog = CreateCatalog(runner, executable => executable == "wsl.exe"
            ? "C:\\Windows\\System32\\wsl.exe"
            : null);
        var profile = TerminalLaunchProfile.CreateWsl("개발 Ubuntu 24.04");

        var spec = catalog.Resolve(profile);

        Assert.AreEqual("C:\\Windows\\System32\\wsl.exe", spec.ExecutablePath);
        CollectionAssert.AreEqual(ExpectedWslLaunchArguments, spec.Arguments.ToArray());
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), spec.WorkingDirectory);
    }

    [TestMethod]
    public void WindowsCommandLineBuilderQuotesEachArgumentWithoutChangingBoundaries()
    {
        var commandLine = WindowsCommandLineBuilder.Build(
            "C:\\Program Files\\wsl.exe", ["--distribution", "개발 Ubuntu", "--cd", "~"]);

        Assert.AreEqual("\"C:\\Program Files\\wsl.exe\" --distribution \"개발 Ubuntu\" --cd ~", commandLine);
    }

    [TestMethod]
    public async Task WslProcessRunnerTimeoutTerminatesBoundedChildProcess()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                      "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.IsTrue(File.Exists(powershell));
        var runner = new WslProcessRunner();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => runner.RunAsync(
            powershell, ["-NoProfile", "-Command", "Start-Sleep -Seconds 5"],
            TimeSpan.FromMilliseconds(100), CancellationToken.None));

        stopwatch.Stop();
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task WslProcessRunnerPreCancelledRequestDoesNotStartProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new WslProcessRunner();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => runner.RunAsync(
            "missing-wsl-test.exe", [], TimeSpan.FromSeconds(1), cancellation.Token));
    }

    private static TerminalLaunchProfileCatalog CreateCatalog(FakeWslProcessRunner runner,
                                                              Func<string, string?> executableResolver)
    {
        return new TerminalLaunchProfileCatalog(runner, new NullDiagnosticLog(), executableResolver,
                                                TimeSpan.FromMilliseconds(250));
    }

    private sealed class FakeWslProcessRunner : IWslProcessRunner
    {
        internal WslProcessResult Result { get; init; } = new(0, []);

        internal Exception? Exception { get; init; }

        internal int CallCount { get; private set; }

        internal IReadOnlyList<string> Arguments { get; private set; } = [];

        internal TimeSpan Timeout { get; private set; }

        public Task<WslProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments,
                                               TimeSpan timeout, CancellationToken cancellationToken)
        {
            _ = executablePath;
            _ = cancellationToken;
            CallCount++;
            Arguments = arguments.ToArray();
            Timeout = timeout;
            if (Exception is not null)
            {
                return Task.FromException<WslProcessResult>(Exception);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
            _ = level;
            _ = subsystem;
            _ = operation;
            _ = message;
            _ = exception;
        }
    }
}
