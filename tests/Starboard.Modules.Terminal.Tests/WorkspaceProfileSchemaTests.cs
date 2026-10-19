using System.Text.Json;
using System.Text.Json.Nodes;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;
using Starboard.Modules.Terminal.Infrastructure;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class WorkspaceProfileSchemaTests
{
    private static readonly string[] _storedTabPropertyNames =
        ["configurationId", "name", "order", "startingDirectory", "launchProfileId"];

    private string directory = null!;
    private string path = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), $"Starboard profile schema {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "workspace.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    [DataRow(TerminalShellKind.Automatic, "automatic", "shell:automatic")]
    [DataRow(TerminalShellKind.Pwsh, "pwsh", "shell:pwsh")]
    [DataRow(TerminalShellKind.PowerShell, "powerShell", "shell:powershell")]
    [DataRow(TerminalShellKind.Cmd, "cmd", "shell:cmd")]
    public async Task LoadLegacyShellAndSaveSchemaTwoPreservesIdentity(TerminalShellKind shellKind,
                                                                       string storedKind, string profileId)
    {
        var json = $$"""
            {"schemaVersion":1,"tabs":[{"configurationId":"20000000000000000000000000000001",
            "name":"기존","order":0,"startingDirectory":"C:\\Work","shellKind":"{{storedKind}}"}],
            "activeTabConfigurationId":null}
            """;
        await File.WriteAllTextAsync(path, json);
        var store = new FileTerminalWorkspaceStore(path);
        var legacy = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, legacy.Status);
        Assert.IsNotNull(legacy.Configuration);
        Assert.AreEqual(profileId, legacy.Configuration.Tabs[0].LaunchProfileId.Value);
        Assert.AreEqual(shellKind, legacy.Configuration.Tabs[0].LaunchProfileId.ToShellKind());
        Assert.IsNull(legacy.Configuration.Tabs[0].LaunchProfileId.WslDistributionName);
        await store.SaveAsync(legacy.Configuration with { SchemaVersion = 2 }, CancellationToken.None);
        var upgraded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, upgraded.Status);
        Assert.IsNotNull(upgraded.Configuration);
        Assert.AreEqual(2, upgraded.Configuration.SchemaVersion);
        CollectionAssert.AreEqual(legacy.Configuration.Tabs.ToArray(), upgraded.Configuration.Tabs.ToArray());
        Assert.AreEqual(json, await File.ReadAllTextAsync(path + ".bak"));
    }

    [TestMethod]
    [DataRow("configurationId")]
    [DataRow("name")]
    [DataRow("order")]
    [DataRow("startingDirectory")]
    [DataRow("launchProfileId")]
    public async Task LoadSchemaTwoMissingRequiredFieldRejectsWorkspace(string field)
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["tabs"]![0]!.AsObject().Remove(field);
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
        Assert.IsNull(result.Configuration);
    }

    [TestMethod]
    public async Task SaveMixedAfterCorruptPrimaryPreservesGoodBackup()
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var backup = await File.ReadAllBytesAsync(path + ".bak");
        await File.WriteAllTextAsync(path, "broken");
        var replacement = CreateMixedConfiguration();

        await store.SaveAsync(replacement, CancellationToken.None);
        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, result.Status);
        Assert.IsNotNull(result.Configuration);
        CollectionAssert.AreEqual(replacement.Tabs.ToArray(), result.Configuration.Tabs.ToArray());
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(path + ".bak"));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }

    [TestMethod]
    public async Task LoadSchemaTwoOversizedPrimaryRecoversBackup()
    {
        var store = new FileTerminalWorkspaceStore(path);
        var configuration = CreateMixedConfiguration();
        await store.SaveAsync(configuration, CancellationToken.None);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var backup = await File.ReadAllBytesAsync(path + ".bak");
        var oversized = "{\"schemaVersion\":2,\"padding\":\"" + new string('a', 65_536) + "\"}";
        await File.WriteAllTextAsync(path, oversized);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.RecoveredFromBackup, result.Status);
        Assert.IsNotNull(result.Configuration);
        CollectionAssert.AreEqual(configuration.Tabs.ToArray(), result.Configuration.Tabs.ToArray());
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(path + ".bak"));
    }

    [TestMethod]
    public async Task LoadLegacySchemaPreservesBuiltInIdentity()
    {
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":1,"tabs":[{"configurationId":"20000000000000000000000000000001",
            "name":"기존","order":0,"startingDirectory":"C:\\Work","shellKind":"pwsh"}],
            "activeTabConfigurationId":"20000000000000000000000000000001"}
            """);
        var result = await new FileTerminalWorkspaceStore(path).LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, result.Status);
        Assert.IsNotNull(result.Configuration);
        Assert.AreEqual("shell:pwsh", result.Configuration.Tabs[0].LaunchProfileId.Value);
        Assert.AreEqual(TerminalShellKind.Pwsh, result.Configuration.Tabs[0].ShellKind);
        Assert.AreEqual(result.Configuration.Tabs[0].ConfigurationId, result.Configuration.ActiveTabConfigurationId);
        await new FileTerminalWorkspaceStore(path).SaveAsync(result.Configuration, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.IsTrue(document.RootElement.GetProperty("tabs")[0].TryGetProperty("shellKind", out _));
        Assert.IsFalse(document.RootElement.GetProperty("tabs")[0].TryGetProperty("launchProfileId", out _));
    }

    [TestMethod]
    public async Task SaveMixedSchemaRoundTripsOnlyAllowedMetadata()
    {
        var configuration = CreateMixedConfiguration();
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(configuration, CancellationToken.None);
        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Loaded, result.Status);
        Assert.IsNotNull(result.Configuration);
        Assert.AreEqual(2, result.Configuration.SchemaVersion);
        CollectionAssert.AreEqual(configuration.Tabs.ToArray(), result.Configuration.Tabs.ToArray());
        Assert.AreEqual(configuration.ActiveTabConfigurationId, result.Configuration.ActiveTabConfigurationId);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        foreach (var tab in document.RootElement.GetProperty("tabs").EnumerateArray())
        {
            CollectionAssert.AreEquivalent(_storedTabPropertyNames,
                                           tab.EnumerateObject().Select(property => property.Name).ToArray());
        }

        Assert.AreEqual("Ubuntu", result.Configuration.Tabs[1].LaunchProfileId.WslDistributionName);
        Assert.IsFalse(result.Configuration.Tabs[1].LaunchProfileId.IsBuiltInShell);
    }

    [TestMethod]
    [DataRow("executable")]
    [DataRow("arguments")]
    [DataRow("command")]
    [DataRow("output")]
    [DataRow("environment")]
    [DataRow("sessionId")]
    public async Task LoadUnknownRuntimeFieldRejectsOtherwiseValidFile(string field)
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["tabs"]![0]![field] = "forbidden";
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("shell:unknown")]
    [DataRow("cmd.exe /c echo unsafe")]
    [DataRow("wsl:")]
    [DataRow("wsl: Ubuntu")]
    [DataRow("wsl:Ubuntu\n")]
    public async Task LoadInvalidProfileRejectsWholeFile(string? profile)
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["tabs"]![1]!["launchProfileId"] = profile;
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
    }

    [TestMethod]
    public void ValidateDefaultOrConflictingProfilesRejectsWorkspace()
    {
        var configuration = CreateMixedConfiguration();
        var tab = configuration.Tabs[0];
        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(
            configuration with { Tabs = [tab with { LaunchProfileId = default }] }));
        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(
            configuration with { Tabs = [tab with { ShellKind = TerminalShellKind.Cmd }] }));
        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(
            configuration with { SchemaVersion = 1 }));
        Assert.IsFalse(default(TerminalLaunchProfileId).IsBuiltInShell);
        Assert.IsNull(default(TerminalLaunchProfileId).WslDistributionName);
    }

    [TestMethod]
    public async Task LoadConflictingSchemaFieldsRejectsWorkspace()
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["tabs"]![1]!["shellKind"] = "cmd";
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
    }

    [TestMethod]
    public async Task SavePreservesFutureBackupAndPrimaryBytes()
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var original = await File.ReadAllTextAsync(path);
        const string future = "{\"schemaVersion\":3,\"payload\":\"preserve\"}";
        await File.WriteAllTextAsync(path + ".bak", future);

        await Assert.ThrowsExactlyAsync<TerminalWorkspaceFutureSchemaException>(() =>
            store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None));

        Assert.AreEqual(original, await File.ReadAllTextAsync(path));
        Assert.AreEqual(future, await File.ReadAllTextAsync(path + ".bak"));
        Assert.IsFalse(File.Exists(path + ".tmp"));
        await File.WriteAllTextAsync(path, "broken");
        var result = await store.LoadAsync(CancellationToken.None);
        Assert.AreEqual(TerminalWorkspaceLoadStatus.FutureSchema, result.Status);
        Assert.AreEqual("broken", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task LoadCorruptMixedPrimaryRecoversExactBackup()
    {
        var store = new FileTerminalWorkspaceStore(path);
        var configuration = CreateMixedConfiguration();
        await store.SaveAsync(configuration, CancellationToken.None);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var backup = await File.ReadAllTextAsync(path + ".bak");
        await File.WriteAllTextAsync(path, "broken");

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.RecoveredFromBackup, result.Status);
        Assert.IsNotNull(result.Configuration);
        CollectionAssert.AreEqual(configuration.Tabs.ToArray(), result.Configuration.Tabs.ToArray());
        Assert.AreEqual(backup, await File.ReadAllTextAsync(path));
        Assert.AreEqual(backup, await File.ReadAllTextAsync(path + ".bak"));
    }

    [TestMethod]
    public async Task LoadExcessMixedTabsRejectsWholeWorkspace()
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var tabs = json["tabs"]!.AsArray();
        for (var index = 2; index < 9; index++)
        {
            var tab = tabs[0]!.DeepClone();
            tab["configurationId"] = TerminalTabConfigurationId.CreateNew().ToString();
            tab["order"] = index;
            tabs.Add(tab);
        }

        await File.WriteAllTextAsync(path, json.ToJsonString());
        var result = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(TerminalWorkspaceLoadStatus.Invalid, result.Status);
    }

    [TestMethod]
    public async Task FailedAtomicReplacePreservesPrimaryAndBackup()
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var original = await File.ReadAllTextAsync(path);
        var backup = await File.ReadAllTextAsync(path + ".bak");
        await using (var lockedPrimary = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None));
        }

        Assert.AreEqual(original, await File.ReadAllTextAsync(path));
        Assert.AreEqual(backup, await File.ReadAllTextAsync(path + ".bak"));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }

    [TestMethod]
    public async Task CancelledSavePreservesPrimaryAndBackup()
    {
        var store = new FileTerminalWorkspaceStore(path);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        await store.SaveAsync(CreateMixedConfiguration(), CancellationToken.None);
        var original = await File.ReadAllTextAsync(path);
        var backup = await File.ReadAllTextAsync(path + ".bak");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.SaveAsync(CreateMixedConfiguration(), cancellation.Token));

        Assert.AreEqual(original, await File.ReadAllTextAsync(path));
        Assert.AreEqual(backup, await File.ReadAllTextAsync(path + ".bak"));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }

    [TestMethod]
    public async Task SaveExcessTabsOrBytesPreservesExistingFile()
    {
        var store = new FileTerminalWorkspaceStore(path);
        var configuration = CreateMixedConfiguration();
        await store.SaveAsync(configuration, CancellationToken.None);
        var original = await File.ReadAllTextAsync(path);
        var tabs = Enumerable.Range(0, 9).Select(index => configuration.Tabs[0] with
        {
            ConfigurationId = TerminalTabConfigurationId.CreateNew(),
            Order = index,
        }).ToArray();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.SaveAsync(configuration with { Tabs = tabs }, CancellationToken.None));
        tabs = tabs.Take(8).Select(tab => tab with { StartingDirectory = "C:\\" + new string('a', 9000) }).ToArray();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            store.SaveAsync(configuration with { Tabs = tabs, ActiveTabConfigurationId = null }, CancellationToken.None));

        Assert.AreEqual(original, await File.ReadAllTextAsync(path));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }

    private static TerminalWorkspaceConfiguration CreateMixedConfiguration()
    {
        var first = new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "Windows", 0,
                                                          "C:\\Work", TerminalLaunchProfileId.FromShell(TerminalShellKind.Pwsh));
        var second = new TerminalWorkspaceTabConfiguration(TerminalTabConfigurationId.CreateNew(), "Ubuntu", 1,
                                                           "C:\\Work", TerminalLaunchProfileId.FromWslDistribution("Ubuntu"));
        return new TerminalWorkspaceConfiguration(2, [first, second], second.ConfigurationId);
    }
}
