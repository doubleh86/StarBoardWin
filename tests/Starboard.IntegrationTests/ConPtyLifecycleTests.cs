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
        if (OperatingSystem.IsWindows() == false)
        {
            Assert.Inconclusive("ConPTY is only available on Windows.");
        }

        var helperAssemblyPath = typeof(ConPtyTestHostMarker).Assembly.Location;
        var helperPath = Path.ChangeExtension(helperAssemblyPath, ".exe");
        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"starboard-conpty-{Guid.NewGuid():N}.json");

        try
        {
            var startInfo = new ProcessStartInfo(helperPath)
            {
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add(resultPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The ConPTY test host did not start.");
            await process.WaitForExitAsync();

            Assert.IsTrue(File.Exists(resultPath));

            var json = await File.ReadAllTextAsync(resultPath);
            var result = JsonSerializer.Deserialize<TestHostResult>(
                json,
                JsonSerializerOptions.Web);

            Assert.IsNotNull(result);
            Assert.IsTrue(
                result.Succeeded,
                $"The GUI ConPTY host failed with {result.ErrorType}; exit code {process.ExitCode}.");
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            if (File.Exists(resultPath) == true)
            {
                File.Delete(resultPath);
            }
        }
    }

    private sealed record TestHostResult(bool Succeeded, string? ErrorType);
}
