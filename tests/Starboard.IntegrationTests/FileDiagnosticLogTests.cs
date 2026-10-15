using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Starboard.Modules.Terminal;
using Starboard.SharedKernel.Diagnostics;
using Starboard.Windows.Infrastructure;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class FileDiagnosticLogTests
{
    private delegate bool ParseInputDiagnostic(string json, out string operation);

    [TestMethod]
    public void WriteParsedInputStatesPreservesEveryCategoryWithoutPrivateContent()
    {
        // Exercise the production parser without widening the module's public surface for diagnostics.
        var parserType = typeof(TerminalModule).Assembly.GetType("Starboard.Modules.Terminal.Presentation.RendererInputDiagnostics", true)!;
        var parse = parserType.GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<ParseInputDiagnostic>();
        using var fixture = new LogFixture();
        var log = new FileDiagnosticLog(fixture.DirectoryPath);
        string[] names = ["route", "selection", "focus", "modal", "suppressed", "capture"];
        int[] radices = [6, 2, 3, 2, 2, 2];
        var expected = new HashSet<string>(StringComparer.Ordinal);
        const string privateContent = "clipboard-original terminal-input terminal-output tab-name C:\\private\\path";
        var exception = new InvalidOperationException(privateContent);
        try
        {
            throw exception;
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        for (var combination = 0; combination < 288; combination++)
        {
            var remaining = combination;
            var payload = new Dictionary<string, object>();
            foreach (var index in Enumerable.Range(0, names.Length))
            {
                payload.Add(names[index], remaining % radices[index]);
                remaining /= radices[index];
            }

            var json = JsonSerializer.Serialize(new { version = 2, type = "input-diagnostic", payload });
            Assert.IsTrue(parse(json, out var operation));
            var expectedOperation = $"Input.R{payload["route"]}.S{payload["selection"]}.F{payload["focus"]}.M{payload["modal"]}.U{payload["suppressed"]}.C{payload["capture"]}";
            Assert.AreEqual(expectedOperation, operation);
            Assert.IsTrue(operation.Length <= 64);
            Assert.IsTrue(expected.Add(expectedOperation));
            log.Write(DiagnosticLevel.Information, "Terminal", operation, privateContent, exception);

            // Arbitrary text in any state and extra private fields must never reach a log operation.
            foreach (var name in names)
            {
                var saved = payload[name];
                payload[name] = privateContent;
                Assert.IsFalse(parse(JsonSerializer.Serialize(new { version = 2, type = "input-diagnostic", payload }), out _));
                payload[name] = saved;
            }
            payload.Add("text", privateContent);
            Assert.IsFalse(parse(JsonSerializer.Serialize(new { version = 2, type = "input-diagnostic", payload }), out _));
        }

        log.Write(DiagnosticLevel.Warning, "Terminal", new string('a', 65), privateContent, exception);
        log.Write(DiagnosticLevel.Warning, "Terminal", "unsafe/path", privateContent, exception);
        var lines = File.ReadAllLines(fixture.LogPath);
        Assert.HasCount(290, lines);
        CollectionAssert.AreEquivalent(expected.ToArray(), lines.Take(288).Select(line => line.Split('\t')[3]).ToArray());
        Assert.AreEqual("redacted", lines[288].Split('\t')[3]);
        Assert.AreEqual("redacted", lines[289].Split('\t')[3]);
        var content = File.ReadAllText(fixture.LogPath);
        Assert.IsNotNull(exception.StackTrace);
        foreach (var secret in privateContent.Split(' ').Append(exception.StackTrace)
                                             .Append(nameof(WriteParsedInputStatesPreservesEveryCategoryWithoutPrivateContent)))
        {
            Assert.IsFalse(content.Contains(secret, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Inject the exact WPF clipboard exception for privacy verification.")]
    public void WriteClipboardAndCallbackFailuresPersistsCodesWithoutExceptionContent()
    {
        using var fixture = new LogFixture();
        var log = new FileDiagnosticLog(fixture.DirectoryPath);
        var clipboard = new System.Runtime.InteropServices.ExternalException("private clipboard content", unchecked((int)0x800401D0));
        clipboard.Data["private-data"] = "secret";
        log.Write(DiagnosticLevel.Warning, "Terminal", "Copy.Retry", "private message", clipboard);
        log.Write(DiagnosticLevel.Error, "Host", "DispatcherUnhandled", "ignored", new ObjectDisposedException("private-object"));

        var content = File.ReadAllText(fixture.LogPath);
        StringAssert.Contains(content, "ExternalException\t-2147221040");
        StringAssert.Contains(content, "ObjectDisposedException\t");
        Assert.IsFalse(content.Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("secret", StringComparison.Ordinal));
    }

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
