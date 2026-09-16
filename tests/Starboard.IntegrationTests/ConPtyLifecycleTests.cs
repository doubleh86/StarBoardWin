using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Starboard.ConPtyTestHost;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class ConPtyLifecycleTests
{
    [TestMethod]
    [Timeout(20_000)]
    public async Task GuiHostCanRoundTripCommandThroughConPty()
    {
        await RunHostAsync("lifecycle", TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    [DataRow("powershell-bootstrap-pwsh")]
    [DataRow("powershell-bootstrap-windows")]
    [Timeout(25_000)]
    public async Task GuiHostBootstrapsInteractivePowerShellWithoutExposingInternalInput(string mode)
    {
        await RunHostAsync(mode, TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    [Timeout(45_000)]
    public async Task GuiHostKeepsConPtyTabsIndependentThroughExitRestartAndClose()
    {
        await RunHostAsync("tabs", TimeSpan.FromSeconds(40));
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task GuiHostRejectedModeExitsWithinDeadlineAndLeavesNoTemporaryResult()
    {
        if (OperatingSystem.IsWindows() == false)
        {
            Assert.Inconclusive("The GUI ConPTY host is only available on Windows.");
        }

        var helperAssemblyPath = typeof(ConPtyTestHostMarker).Assembly.Location;
        var helperPath = Path.ChangeExtension(helperAssemblyPath, ".exe");
        var resultPath = Path.Combine(Path.GetTempPath(), $"starboard-conpty-rejected-{Guid.NewGuid():N}.json");
        Process? process = null;
        string? temporaryResultPath = null;
        try
        {
            var startInfo = new ProcessStartInfo(helperPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("unsupported-mode");
            startInfo.ArgumentList.Add(resultPath);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The GUI ConPTY test host did not start.");
            temporaryResultPath = $"{resultPath}.{process.Id}.tmp";
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(2, process.ExitCode);
            Assert.IsTrue(File.Exists(resultPath));
            Assert.IsFalse(File.Exists(temporaryResultPath));
            var json = await File.ReadAllTextAsync(resultPath);
            var result = JsonSerializer.Deserialize<TestHostResult>(json, JsonSerializerOptions.Web);
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("InProgress", result.ErrorType);
        }
        finally
        {
            if (process is not null)
            {
                if (process.HasExited == false)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }

                process.Dispose();
            }

            if (File.Exists(resultPath) == true)
            {
                File.Delete(resultPath);
            }

            if (temporaryResultPath is not null && File.Exists(temporaryResultPath) == true)
            {
                File.Delete(temporaryResultPath);
            }
        }
    }

    private static async Task RunHostAsync(string mode, TimeSpan timeout)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            Assert.Inconclusive("ConPTY is only available on Windows.");
        }

        var helperAssemblyPath = typeof(ConPtyTestHostMarker).Assembly.Location;
        var helperPath = Path.ChangeExtension(helperAssemblyPath, ".exe");
        var resultPath = Path.Combine(Path.GetTempPath(), $"starboard-conpty-{mode}-{Guid.NewGuid():N}.json");

        Process? process = null;
        string? temporaryResultPath = null;
        Exception? cleanupFailure = null;
        try
        {
            var startInfo = new ProcessStartInfo(helperPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add(mode);
            startInfo.ArgumentList.Add(resultPath);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The ConPTY test host did not start.");
            var launchedProcessId = process.Id;
            temporaryResultPath = $"{resultPath}.{launchedProcessId}.tmp";
            try
            {
                await process.WaitForExitAsync().WaitAsync(timeout);
            }
            catch (TimeoutException exception)
            {
                var progress = await TryReadResultAsync(resultPath);
                Assert.Fail($"The GUI ConPTY host timed out after {timeout}. " +
                            $"{DescribeHost(helperPath, launchedProcessId, progress)} {exception.Message}");
            }

            Assert.IsTrue(File.Exists(resultPath),
                          $"The GUI ConPTY host {launchedProcessId} exited without writing '{resultPath}'.");

            var json = await File.ReadAllTextAsync(resultPath);
            var result = JsonSerializer.Deserialize<TestHostResult>(json, JsonSerializerOptions.Web);

            Assert.IsNotNull(result);
            ValidateHostObservation(mode, helperPath, launchedProcessId, result);
            Assert.IsTrue(result.Succeeded,
                          $"The GUI ConPTY host failed with {result.ErrorType}: {result.ErrorMessage}; " +
                          $"exit code {process.ExitCode}. {DescribeHost(helperPath, launchedProcessId, result)}");
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            if (process is not null)
            {
                if (process.HasExited == false)
                {
                    try
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    catch (Exception exception) when (
                        exception is System.ComponentModel.Win32Exception or InvalidOperationException or
                            NotSupportedException or TimeoutException)
                    {
                        cleanupFailure = exception;
                    }
                }

                process.Dispose();
            }

            if (File.Exists(resultPath) == true)
            {
                File.Delete(resultPath);
            }

            if (temporaryResultPath is not null && File.Exists(temporaryResultPath) == true)
            {
                File.Delete(temporaryResultPath);
            }

            if (cleanupFailure is not null)
            {
                Assert.Fail($"The GUI ConPTY host cleanup failed: {cleanupFailure.Message}");
            }
        }
    }

    private static void ValidateHostObservation(string mode, string expectedPath, int launchedProcessId,
                                                TestHostResult result)
    {
        Assert.IsNotNull(result.Observation,
                         $"The GUI ConPTY host did not report its identity. Expected '{expectedPath}', " +
                         $"launched PID {launchedProcessId}.");
        var observation = result.Observation;
        Assert.AreEqual(launchedProcessId, observation.HostProcessId);
        Assert.IsTrue(string.Equals(Path.GetFullPath(expectedPath), Path.GetFullPath(observation.HostExecutablePath),
                                    StringComparison.OrdinalIgnoreCase),
                      $"The observed GUI ConPTY host path was '{observation.HostExecutablePath}' instead of " +
                      $"'{expectedPath}'.");

        if (mode != "tabs" || result.Succeeded == false)
        {
            return;
        }

        Assert.AreEqual("complete", observation.Stage);
        Assert.IsNotNull(observation.SessionId);
        Assert.IsTrue(observation.SessionGeneration >= 2);
        Assert.IsNotNull(observation.ShellProcessId);
        Assert.IsTrue(observation.TabRemoved);
        Assert.IsTrue(observation.ProcessExited);
    }

    private static async Task<TestHostResult?> TryReadResultAsync(string resultPath)
    {
        if (File.Exists(resultPath) == false)
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(resultPath);
            return JsonSerializer.Deserialize<TestHostResult>(json, JsonSerializerOptions.Web);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribeHost(string expectedPath, int launchedProcessId, TestHostResult? result)
    {
        if (result?.Observation is not { } observation)
        {
            return $"Expected host '{expectedPath}', launched host PID {launchedProcessId}; no lifecycle observation was written.";
        }

        return $"Expected host '{expectedPath}', launched host PID {launchedProcessId}; " +
               $"observed host '{observation.HostExecutablePath}', host PID {observation.HostProcessId}, " +
               $"stage '{observation.Stage}', session {observation.SessionId?.ToString() ?? "none"}, " +
               $"generation {observation.SessionGeneration?.ToString(CultureInfo.InvariantCulture) ?? "none"}, " +
               $"shell PID {observation.ShellProcessId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, " +
               $"tab removed {observation.TabRemoved}, process exited {observation.ProcessExited}.";
    }

    private sealed record TestHostResult(bool Succeeded, string? ErrorType, string? ErrorMessage,
                                         TestHostObservation? Observation);

    private sealed record TestHostObservation(int HostProcessId, string HostExecutablePath, string Stage,
                                              Guid? SessionId, long? SessionGeneration, int? ShellProcessId,
                                              bool TabRemoved, bool ProcessExited);
}
