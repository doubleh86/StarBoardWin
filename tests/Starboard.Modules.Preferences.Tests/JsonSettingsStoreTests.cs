using System.Text.Json;
using Starboard.Modules.Preferences.Contracts;
using Starboard.Modules.Preferences.Infrastructure;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Preferences.Tests;

[TestClass]
public sealed class JsonSettingsStoreTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task LoadAsyncCorruptedPrimaryWithValidBackupRestoresPreviousSettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, "not valid json");
            const string backupSettingsJson = """
                {
                  "schemaVersion": 4,
                  "theme": "Dark",
                  "fontSize": 15
                }
                """;
            await File.WriteAllTextAsync(settingsPath + ".bak", backupSettingsJson);
            var store = new JsonSettingsStore(new TestDiagnosticLog(), settingsPath);

            var result = await store.LoadAsync(CancellationToken.None);

            Assert.IsFalse(result.UsedDefaults);
            Assert.AreEqual("Dark", result.Settings.Theme);
            Assert.AreEqual(15, result.Settings.FontSize);
            Assert.AreEqual("Ctrl+Alt+S", result.Settings.ActivationShortcut);
            Assert.AreEqual("설정 파일을 읽지 못해 이전 설정으로 복구했습니다.", result.RecoveryMessage);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task SaveAsyncReplacesPrimaryOnlyAfterCompleteTemporaryDocumentAndKeepsBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var store = new JsonSettingsStore(new TestDiagnosticLog(), settingsPath);
            await store.SaveAsync(new AppSettings { Theme = "Dark" }, CancellationToken.None);
            await store.SaveAsync(new AppSettings { Theme = "Light" }, CancellationToken.None);

            var current = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(settingsPath),
                                                                  SerializerOptions);
            var backup = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(settingsPath + ".bak"),
                                                                 SerializerOptions);

            Assert.IsNotNull(current);
            Assert.IsNotNull(backup);
            Assert.AreEqual("Light", current.Theme);
            Assert.AreEqual("Dark", backup.Theme);
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task SaveAsyncFailureDoesNotOverwriteTheExistingSettingsFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var blockedPath = Path.Combine(directory, "blocked");
            await File.WriteAllTextAsync(blockedPath, "existing file");
            var settingsPath = Path.Combine(blockedPath, "settings.json");
            var store = new JsonSettingsStore(new TestDiagnosticLog(), settingsPath);

            await Assert.ThrowsExactlyAsync<IOException>(async () =>
                await store.SaveAsync(new AppSettings { Theme = "Dark" }, CancellationToken.None));

            var existingContent = await File.ReadAllTextAsync(blockedPath);
            Assert.AreEqual("existing file", existingContent);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Starboard.Preferences.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory) == true)
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class TestDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                          Exception? exception = null)
        {
        }
    }
}
