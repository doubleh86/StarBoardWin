using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Infrastructure;

internal sealed class FileTerminalSavedTabStore : ITerminalSavedTabStore, IDisposable
{
    internal const int MaximumFileSizeBytes = 64 * 1024;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    private static readonly string[] _rootPropertyNames = ["schemaVersion", "tabs"];
    private static readonly string[] _tabPropertyNames = ["savedTabId", "name", "startingDirectory", "shellKind"];

    private readonly string savedTabsPath;
    private readonly string temporaryPath;
    private readonly SemaphoreSlim writerLock = new(1, 1);

    private bool isDisposed;

    internal FileTerminalSavedTabStore(string savedTabsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedTabsPath);
        this.savedTabsPath = Path.GetFullPath(savedTabsPath);
        temporaryPath = this.savedTabsPath + ".tmp";
    }

    internal static string GetDefaultPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "Starboard", "saved-tabs.json");
    }

    public async Task<TerminalSavedTabsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (File.Exists(savedTabsPath) == false)
        {
            return new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.NotFound,
                                                   CreateEmptySnapshot(), null);
        }

        try
        {
            await using var stream = new FileStream(savedTabsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                    bufferSize: 4096,
                                                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumFileSizeBytes)
            {
                return CreateInvalidResult();
            }

            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
                                                    {
                                                        AllowTrailingCommas = false,
                                                        CommentHandling = JsonCommentHandling.Disallow,
                                                        MaxDepth = 8,
                                                    });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                TryGetSchemaVersion(document.RootElement, out var schemaVersion, out var isFutureSchema) == false)
            {
                return CreateInvalidResult();
            }

            if (isFutureSchema == true)
            {
                return new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.FutureSchema, null,
                                                       "더 새로운 버전의 저장한 탭 파일을 보존하기 위해 편집을 중단했습니다.");
            }

            if (schemaVersion != TerminalSavedTabsContract.CurrentSchemaVersion ||
                TryValidateShape(document.RootElement, out _) == false)
            {
                return CreateInvalidResult();
            }

            var storedSnapshot = JsonSerializer.Deserialize<StoredSavedTabsSnapshot>(bytes, _serializerOptions);
            var snapshot = storedSnapshot?.ToSnapshot();
            if (snapshot is null)
            {
                return CreateInvalidResult();
            }

            return new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.Loaded, snapshot, null);
        }
        catch (Exception exception) when (IsInvalidFileException(exception) == true)
        {
            return CreateInvalidResult();
        }
    }

    public async Task SaveAsync(TerminalSavedTabsSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var storedSnapshot = StoredSavedTabsSnapshot.From(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(storedSnapshot, _serializerOptions);
        if (bytes.Length > MaximumFileSizeBytes)
        {
            throw new InvalidDataException("The saved terminal tabs exceed the 64 KiB storage limit.");
        }

        await writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            var directory = Path.GetDirectoryName(savedTabsPath)
                ?? throw new InvalidOperationException("The saved terminal tabs path has no parent directory.");
            Directory.CreateDirectory(directory);

            var current = await InspectSchemaAsync(cancellationToken).ConfigureAwait(false);
            if (current == StoredFileStatus.FutureSchema)
            {
                throw new TerminalSavedTabsFutureSchemaException();
            }

            try
            {
                await WriteTemporaryFileAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (File.Exists(savedTabsPath) == true)
                {
                    File.Replace(temporaryPath, savedTabsPath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, savedTabsPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath) == true)
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            writerLock.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed == true)
        {
            return;
        }

        isDisposed = true;
        writerLock.Dispose();
    }

    private async Task<StoredFileStatus> InspectSchemaAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(savedTabsPath) == false)
        {
            return StoredFileStatus.NotFound;
        }

        await using var stream = new FileStream(savedTabsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                bufferSize: 4096,
                                                FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumFileSizeBytes)
        {
            return StoredFileStatus.Invalid;
        }

        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
                                                    {
                                                        AllowTrailingCommas = false,
                                                        CommentHandling = JsonCommentHandling.Disallow,
                                                        MaxDepth = 8,
                                                    });
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetSchemaVersion(document.RootElement, out _, out var isFutureSchema) == true &&
                isFutureSchema == true)
            {
                return StoredFileStatus.FutureSchema;
            }
        }
        catch (JsonException)
        {
            return StoredFileStatus.Invalid;
        }

        return StoredFileStatus.Valid;
    }

    private async Task WriteTemporaryFileAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryValidateShape(JsonElement root, out int schemaVersion)
    {
        schemaVersion = 0;
        if (HasExactlyProperties(root, _rootPropertyNames) == false ||
            TryGetUniqueInt32(root, "schemaVersion", out schemaVersion) == false ||
            root.TryGetProperty("tabs", out var tabs) == false || tabs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var tab in tabs.EnumerateArray())
        {
            if (HasExactlyProperties(tab, _tabPropertyNames) == false)
            {
                return false;
            }

            if (HasSingleString(tab, "savedTabId") == false || HasSingleString(tab, "name") == false ||
                HasSingleString(tab, "startingDirectory") == false || HasSingleString(tab, "shellKind") == false)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExactlyProperties(JsonElement element, string[] expectedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = element.EnumerateObject().Select(property => property.Name).ToArray();
        return properties.Length == expectedNames.Length && properties.Distinct(StringComparer.Ordinal).Count() == properties.Length &&
               properties.All(expectedNames.Contains);
    }

    private static bool HasSingleString(JsonElement element, string propertyName)
    {
        var matches = element.EnumerateObject().Where(property => property.Name == propertyName).ToArray();
        return matches.Length == 1 && matches[0].Value.ValueKind == JsonValueKind.String;
    }

    private static bool TryGetUniqueInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        var matches = element.EnumerateObject().Where(property => property.Name == propertyName).ToArray();
        return matches.Length == 1 && matches[0].Value.TryGetInt32(out value);
    }

    private static bool TryGetSchemaVersion(JsonElement element, out int schemaVersion, out bool isFutureSchema)
    {
        schemaVersion = 0;
        isFutureSchema = false;
        var matches = element.EnumerateObject().Where(property => property.Name == "schemaVersion").ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (matches[0].Value.TryGetInt32(out schemaVersion) == true)
        {
            isFutureSchema = schemaVersion > TerminalSavedTabsContract.CurrentSchemaVersion;
            return true;
        }

        // An unrepresentable positive numeric version cannot belong to this implementation. Preserve it as future
        // schema instead of treating it as corrupt and allowing a later save to overwrite it.
        var rawVersion = matches[0].Value.GetRawText();
        isFutureSchema = rawVersion.Length > 0 && rawVersion[0] != '-';
        return isFutureSchema;
    }

    private static TerminalSavedTabsLoadResult CreateInvalidResult()
    {
        return new TerminalSavedTabsLoadResult(TerminalSavedTabsLoadStatus.Invalid, CreateEmptySnapshot(),
                                               "저장한 탭 파일이 손상되어 빈 목록으로 시작합니다.");
    }

    private static TerminalSavedTabsSnapshot CreateEmptySnapshot()
    {
        return new TerminalSavedTabsSnapshot(TerminalSavedTabsContract.CurrentSchemaVersion, []);
    }

    private static bool IsInvalidFileException(Exception exception)
    {
        return exception is ArgumentException or
               InvalidDataException or
               JsonException or
               NotSupportedException;
    }

    private enum StoredFileStatus
    {
        NotFound,
        Valid,
        Invalid,
        FutureSchema,
    }

    private sealed record StoredSavedTabsSnapshot(int SchemaVersion, IReadOnlyList<StoredSavedTab?>? Tabs)
    {
        internal static StoredSavedTabsSnapshot From(TerminalSavedTabsSnapshot snapshot)
        {
            return new StoredSavedTabsSnapshot(snapshot.SchemaVersion,
                                               snapshot.Tabs.Select(StoredSavedTab.From).ToArray());
        }

        internal TerminalSavedTabsSnapshot ToSnapshot()
        {
            if (Tabs is null)
            {
                throw new InvalidDataException("The saved terminal tabs list is missing.");
            }

            var tabs = Tabs.Select(tab => tab?.ToSavedTab()
                                          ?? throw new InvalidDataException("A saved terminal tab is null."))
                           .ToArray();
            return new TerminalSavedTabsSnapshot(SchemaVersion, tabs);
        }
    }

    private sealed record StoredSavedTab(string? SavedTabId, string? Name, string? StartingDirectory,
                                         TerminalShellKind ShellKind)
    {
        internal static StoredSavedTab From(TerminalSavedTab savedTab)
        {
            return new StoredSavedTab(savedTab.SavedTabId.ToString(), savedTab.Name,
                                      savedTab.StartingDirectory, savedTab.ShellKind);
        }

        internal TerminalSavedTab ToSavedTab()
        {
            if (Name is null || StartingDirectory is null)
            {
                throw new InvalidDataException("A saved terminal tab contains a null field.");
            }

            if (SavedTabId is null || Guid.TryParseExact(SavedTabId, "N", out var identifier) == false ||
                identifier == Guid.Empty)
            {
                throw new InvalidDataException("A saved terminal tab identifier is invalid.");
            }

            return new TerminalSavedTab(new TerminalSavedTabId(identifier), Name, StartingDirectory, ShellKind);
        }
    }
}
