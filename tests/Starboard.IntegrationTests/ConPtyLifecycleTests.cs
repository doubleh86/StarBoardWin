using System.Diagnostics;
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
    [Timeout(45_000)]
    public async Task GuiHostKeepsConPtyTabsIndependentThroughExitRestartAndClose()
    {
        await RunHostAsync("tabs", TimeSpan.FromSeconds(40));
    }

    private static async Task RunHostAsync(string mode, TimeSpan timeout)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            Assert.Inconclusive("ConPTY is only available on Windows.");
        }

        var helperAssemblyPath = typeof(ConPtyTestHostMarker).Assembly.Location;
        var helperPath = Path.ChangeExtension(helperAssemblyPath, ".exe");
        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"starboard-conpty-{mode}-{Guid.NewGuid():N}.json");

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo(helperPath)
            {
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add(mode);
            startInfo.ArgumentList.Add(resultPath);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The ConPTY test host did not start.");
            await process.WaitForExitAsync().WaitAsync(timeout);

            Assert.IsTrue(File.Exists(resultPath));

            var json = await File.ReadAllTextAsync(resultPath);
            var result = JsonSerializer.Deserialize<TestHostResult>(
                json,
                JsonSerializerOptions.Web);

            Assert.IsNotNull(result);
            Assert.IsTrue(
                result.Succeeded,
                $"The GUI ConPTY host failed with {result.ErrorType}: {result.ErrorMessage}; " +
                $"exit code {process.ExitCode}.");
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            if (process is not null)
            {
                if (process.HasExited == false)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }

                process.Dispose();
            }

            if (File.Exists(resultPath) == true)
            {
                File.Delete(resultPath);
            }
        }
    }

    private sealed record TestHostResult(
        bool Succeeded,
        string? ErrorType,
        string? ErrorMessage);
}
