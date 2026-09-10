using System.Text.Json;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Infrastructure;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class FileTerminalSavedTabStoreTests
{
    private static readonly string[] _allowedRootProperties = ["schemaVersion", "tabs"];
    private static readonly string[] _allowedTabProperties =
        ["savedTabId", "name", "startingDirectory", "shellKind"];
    private static readonly string[] _forbiddenProperties =
        ["command", "output", "history", "clipboard", "environment", "pid", "scrollback", "sessionId"];

    [TestMethod]
    public async Task SaveAndLoadAsyncRoundTripPersistsOnlyReusableDefinition()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "saved-tabs.json");
            using var store = new FileTerminalSavedTabStore(path);
            var expected = CreateSnapshot(directory);

            await store.SaveAsync(expected, CancellationToken.None);
            var result = await store.LoadAsync(CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);

            Assert.AreEqual(TerminalSavedTabsLoadStatus.Loaded, result.Status);
            Assert.IsNotNull(result.Snapshot);
            Assert.AreEqual(expected.Tabs[0], result.Snapshot.Tabs[0]);
            CollectionAssert.AreEquivalent(_allowedRootProperties,
                                           document.RootElement.EnumerateObject()
                                               .Select(property => property.Name).ToArray());
            CollectionAssert.AreEquivalent(_allowedTabProperties,
                                           document.RootElement.GetProperty("tabs")[0].EnumerateObject()
                                               .Select(property => property.Name).ToArray());
            foreach (var forbiddenProperty in _forbiddenProperties)
            {
                Assert.IsFalse(json.Contains(forbiddenProperty, StringComparison.OrdinalIgnoreCase));
            }

            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsyncMissingMalformedUnknownAndOversizedFilesReturnsBoundedEmptyResults()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "saved-tabs.json");
            using var store = new FileTerminalSavedTabStore(path);

            var missing = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(TerminalSavedTabsLoadStatus.NotFound, missing.Status);
            Assert.IsEmpty(missing.Snapshot!.Tabs);

            await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"tabs\":[],\"unexpected\":true}");
            var unknown = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(TerminalSavedTabsLoadStatus.Invalid, unknown.Status);
            Assert.IsEmpty(unknown.Snapshot!.Tabs);

            await File.WriteAllTextAsync(path,
                                         "{\"schemaVersion\":1,\"schemaVersion\":1,\"tabs\":[]}");
            var duplicate = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(TerminalSavedTabsLoadStatus.Invalid, duplicate.Status);

            await File.WriteAllBytesAsync(path, new byte[FileTerminalSavedTabStore.MaximumFileSizeBytes + 1]);
            var oversized = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(TerminalSavedTabsLoadStatus.Invalid, oversized.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAsyncFutureSchemaPreservesOriginalFile()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "saved-tabs.json");
            const string futureJson =
                "{\"schemaVersion\":999999999999999999999999999999,\"tabs\":[],\"futureData\":true}";
            await File.WriteAllTextAsync(path, futureJson);
            using var store = new FileTerminalSavedTabStore(path);

            var load = await store.LoadAsync(CancellationToken.None);
            await Assert.ThrowsExactlyAsync<TerminalSavedTabsFutureSchemaException>(() =>
                store.SaveAsync(CreateSnapshot(directory), CancellationToken.None));

            Assert.AreEqual(TerminalSavedTabsLoadStatus.FutureSchema, load.Status);
            Assert.AreEqual(futureJson, await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAsyncConcurrentWritersLeavesOneCompleteValidDocument()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "saved-tabs.json");
            using var store = new FileTerminalSavedTabStore(path);
            var first = CreateSnapshot(directory, "첫째", 1);
            var second = CreateSnapshot(directory, "둘째", 2);

            await Task.WhenAll(store.SaveAsync(first, CancellationToken.None),
                               store.SaveAsync(second, CancellationToken.None));
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(TerminalSavedTabsLoadStatus.Loaded, loaded.Status);
            Assert.IsTrue(loaded.Snapshot!.Tabs[0].Name is "첫째" or "둘째");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TerminalSavedTabsSnapshot CreateSnapshot(string directory, string name = "서버", int id = 1)
    {
        var savedTab = new TerminalSavedTab(new TerminalSavedTabId(CreateGuid(id)), name, directory,
                                            TerminalShellKind.Pwsh);
        return new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, [savedTab]);
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starboard-saved-tab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static Guid CreateGuid(int suffix)
    {
        return Guid.Parse($"50000000-0000-0000-0000-{suffix:D12}");
    }
}
