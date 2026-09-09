using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Infrastructure;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class FileTerminalWorkspaceStoreTests
{
    private static readonly string[] _storedWorkspacePropertyNames =
    [
        "schemaVersion",
        "tabs",
        "activeTabConfigurationId",
    ];
    private static readonly string[] _forbiddenWorkspacePropertyNames =
    [
        "sessionId",
        "process",
        "command",
        "output",
        "clipboard",
        "environment",
    ];

    [TestMethod]
    public async Task SaveAndLoadRoundTripContainsOnlyConfigurationFields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var configuration = CreateConfiguration("개발", "C:\\Work\\한글", TerminalShellKind.Pwsh);

            await store.SaveAsync(configuration, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            var result = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, result.Status);
            AssertConfigurationEqual(configuration, result.Configuration);
            StringAssert.Contains(json, "\"configurationId\"");
            StringAssert.Contains(json, "\"shellKind\": \"pwsh\"");
            foreach (var forbidden in _forbiddenWorkspacePropertyNames)
            {
                Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
            }

            using var document = JsonDocument.Parse(json);
            CollectionAssert.AreEquivalent(_storedWorkspacePropertyNames,
                                           document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CorruptPrimaryRecoversLastGoodBackupAndRepairsPrimary()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var first = CreateConfiguration("첫 구성", "C:\\Work\\One", TerminalShellKind.PowerShell);
            var second = CreateConfiguration("둘째 구성", "C:\\Work\\Two", TerminalShellKind.Cmd);
            await store.SaveAsync(first, CancellationToken.None);
            await store.SaveAsync(second, CancellationToken.None);
            await File.WriteAllTextAsync(path, "{ corrupt", CancellationToken.None);

            var recovered = await store.LoadAsync(CancellationToken.None);
            var repaired = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.RecoveredFromBackup, recovered.Status);
            AssertConfigurationEqual(first, recovered.Configuration);
            Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, repaired.Status);
            AssertConfigurationEqual(first, repaired.Configuration);
            Assert.IsTrue(File.Exists(path + ".bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAfterUnrepairableCorruptPrimaryPreservesLastGoodBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var first = CreateConfiguration("백업 보존", "C:\\Work\\First", TerminalShellKind.Pwsh);
            var second = CreateConfiguration("현재 구성", "C:\\Work\\Second", TerminalShellKind.PowerShell);
            var third = CreateConfiguration("새 구성", "C:\\Work\\Third", TerminalShellKind.Cmd);
            await store.SaveAsync(first, CancellationToken.None);
            await store.SaveAsync(second, CancellationToken.None);
            await File.WriteAllTextAsync(path, "{ corrupt", CancellationToken.None);

            await using (var primaryLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var recoveredWithoutRepair = await store.LoadAsync(CancellationToken.None);
                Assert.AreEqual(TerminalWorkspaceLoadStatus.RecoveredFromBackup, recoveredWithoutRepair.Status);
                AssertConfigurationEqual(first, recoveredWithoutRepair.Configuration);
            }

            await store.SaveAsync(third, CancellationToken.None);
            await File.WriteAllTextAsync(path, "{ corrupt again", CancellationToken.None);
            var recoveredAfterSave = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.RecoveredFromBackup, recoveredAfterSave.Status);
            AssertConfigurationEqual(first, recoveredAfterSave.Configuration);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvalidOrOversizedFilesFallBackToDefaultWorkspaceResult()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            await File.WriteAllBytesAsync(path, new byte[FileTerminalWorkspaceStore.MaximumFileSizeBytes + 1]);
            await File.WriteAllTextAsync(path + ".bak",
                                         "{\"schemaVersion\":1,\"tabs\":[],\"activeTabConfigurationId\":null}");

            var result = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
            Assert.IsNull(result.Configuration);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FutureSchemaIsPreservedWithoutUsingOlderBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var futureJson = "{\"schemaVersion\":99,\"futurePayload\":\"keep-me\"}";
            await store.SaveAsync(CreateConfiguration("백업", "C:\\Work\\Backup", TerminalShellKind.Cmd),
                                  CancellationToken.None);
            await store.SaveAsync(CreateConfiguration("현재", "C:\\Work\\Current", TerminalShellKind.Pwsh),
                                  CancellationToken.None);
            await File.WriteAllTextAsync(path, futureJson);

            var result = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.FutureSchema, result.Status);
            Assert.AreEqual(futureJson, await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveDoesNotOverwriteFutureSchemaThatAppearsAfterStartup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var futureJson = "{\"schemaVersion\":99,\"futurePayload\":\"keep-me\"}";
            await File.WriteAllTextAsync(path, futureJson);

            await Assert.ThrowsExactlyAsync<TerminalWorkspaceFutureSchemaException>(() =>
                store.SaveAsync(CreateConfiguration("새 구성", "C:\\Work\\New", TerminalShellKind.Cmd),
                                CancellationToken.None));

            Assert.AreEqual(futureJson, await File.ReadAllTextAsync(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UnknownFieldsAndInvalidIdentifierRejectWholeFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            var json = """
                {
                  "schemaVersion": 1,
                  "tabs": [{
                    "configurationId": "not-a-guid",
                    "name": "작업",
                    "order": 0,
                    "startingDirectory": "C:\\Work",
                    "shellKind": "pwsh",
                    "command": "do-not-accept"
                  }],
                  "activeTabConfigurationId": null
                }
                """;
            await File.WriteAllTextAsync(path, json);

            var result = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":1,\"tabs\":[null],\"activeTabConfigurationId\":null}")]
    [DataRow("{\"schemaVersion\":1,\"tabs\":[{\"configurationId\":\"20000000000000000000000000000001\",\"order\":0,\"startingDirectory\":\"C:\\\\Work\",\"shellKind\":\"pwsh\"}],\"activeTabConfigurationId\":null}")]
    [DataRow("{\"schemaVersion\":1,\"tabs\":[{\"configurationId\":\"20000000000000000000000000000001\",\"name\":\"Work\",\"order\":0,\"startingDirectory\":\"C:\\\\Work\"}],\"activeTabConfigurationId\":null}")]
    public async Task NullOrMissingTabFieldsRejectWholeFile(string json)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            var store = new FileTerminalWorkspaceStore(path);
            await File.WriteAllTextAsync(path, json);

            var result = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
            Assert.IsNull(result.Configuration);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TerminalWorkspaceConfiguration CreateConfiguration(string name, string directory,
                                                                      TerminalShellKind shellKind)
    {
        var identifier = new TerminalTabConfigurationId(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        return new TerminalWorkspaceConfiguration(1,
                                                  [new TerminalWorkspaceTabConfiguration(identifier, name, 0,
                                                                                         directory, shellKind)],
                                                  identifier);
    }

    private static void AssertConfigurationEqual(TerminalWorkspaceConfiguration expected,
                                                 TerminalWorkspaceConfiguration? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.SchemaVersion, actual.SchemaVersion);
        Assert.AreEqual(expected.ActiveTabConfigurationId, actual.ActiveTabConfigurationId);
        CollectionAssert.AreEqual(expected.Tabs.ToArray(), actual.Tabs.ToArray());
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Starboard workspace tests {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
