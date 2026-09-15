using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Starboard.SharedKernel.Diagnostics;
using Starboard.Windows.Infrastructure;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class FileDiagnosticLogTests
{
    [TestMethod]
    public void WriteDoesNotPersistMessagesOrUnsafeMetadata()
    {
        using var fixture = new LogFixture();
        var log = new FileDiagnosticLog(fixture.DirectoryPath);

        log.Write(DiagnosticLevel.Warning, "Terminal", "Restart", "secret-command C:\\Users\\A\\prompt> output");
        log.Write(DiagnosticLevel.Error, "user supplied name", "line\nbreak", "another secret");

        var content = File.ReadAllText(fixture.LogPath);
        Assert.IsFalse(content.Contains("secret-command", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("C:\\Users", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("prompt>", StringComparison.Ordinal));
        StringAssert.Contains(content, "Terminal\tRestart");
        StringAssert.Contains(content, "redacted\tredacted");
    }

    [TestMethod]
    public void WriteRotatesAtBoundedSizeAndKeepsOnlyConfiguredFiles()
    {
        using var fixture = new LogFixture();
        File.WriteAllBytes(fixture.LogPath, new byte[FileDiagnosticLog.MaximumLogFileBytes]);
        var log = new FileDiagnosticLog(fixture.DirectoryPath);

        for (var index = 0; index < FileDiagnosticLog.MaximumLogFileCount + 2; index++)
        {
            log.Write(DiagnosticLevel.Information, "Host", "Startup", "ignored");
            File.WriteAllBytes(fixture.LogPath, new byte[FileDiagnosticLog.MaximumLogFileBytes]);
        }

        var files = Directory.GetFiles(fixture.DirectoryPath, "starboard.log*");
        Assert.AreEqual(FileDiagnosticLog.MaximumLogFileCount, files.Length);
        Assert.IsFalse(File.Exists(fixture.LogPath + ".5"));
    }

    [TestMethod]
    public async Task ConcurrentAndLockedWritesAreIsolated()
    {
        using var fixture = new LogFixture();
        var log = new FileDiagnosticLog(fixture.DirectoryPath);
        var writes = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => log.Write(DiagnosticLevel.Information, "Host", "Concurrent", "ignored")));

        await Task.WhenAll(writes);

        using (new FileStream(fixture.LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            log.Write(DiagnosticLevel.Warning, "Host", "Locked", "must not throw");
        }

        Assert.IsTrue(File.Exists(fixture.LogPath));
    }

    [TestMethod]
    public void CorruptExistingLogAndUnavailableDirectoryDoNotThrow()
    {
        using var fixture = new LogFixture();
        File.WriteAllBytes(fixture.LogPath, [0x00, 0xFF, 0x10]);
        var log = new FileDiagnosticLog(fixture.DirectoryPath);

        log.Write(DiagnosticLevel.Error, "Host", "Shutdown", "ignored");

        var unavailableLog = new FileDiagnosticLog(Path.Combine(fixture.DirectoryPath, "blocked"));
        Directory.Delete(Path.Combine(fixture.DirectoryPath, "blocked"), true);
        unavailableLog.Write(DiagnosticLevel.Error, "Host", "Shutdown", "ignored");
    }

    private sealed class LogFixture : IDisposable
    {
        internal LogFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "StarboardTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }

        internal string LogPath => Path.Combine(DirectoryPath, "starboard.log");

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath) == true)
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
