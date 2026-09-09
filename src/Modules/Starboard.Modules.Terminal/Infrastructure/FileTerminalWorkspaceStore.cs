using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Infrastructure;

internal sealed class FileTerminalWorkspaceStore : ITerminalWorkspaceStore
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

    private readonly string workspacePath;
    private readonly string backupPath;
    private readonly string temporaryPath;

    internal FileTerminalWorkspaceStore(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        this.workspacePath = Path.GetFullPath(workspacePath);
        backupPath = this.workspacePath + ".bak";
        temporaryPath = this.workspacePath + ".tmp";
    }

    internal static string GetDefaultPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "Starboard", "workspace.json");
    }

    public async Task<TerminalWorkspaceLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var primary = await ReadAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        if (primary.Status == StoredFileStatus.Valid)
        {
            return new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.Loaded, primary.Configuration, null);
        }

        if (primary.Status == StoredFileStatus.FutureSchema)
        {
            return CreateFutureSchemaResult();
        }

        var backup = await ReadAsync(backupPath, cancellationToken).ConfigureAwait(false);
        if (backup.Status == StoredFileStatus.FutureSchema)
        {
            return CreateFutureSchemaResult();
        }

        if (backup.Status == StoredFileStatus.Valid && backup.Configuration is not null)
        {
            var message = "손상되거나 누락된 작업공간 파일을 마지막 정상 백업에서 복구했습니다.";
            try
            {
                await RestorePrimaryFromBackupAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsFileException(exception) == true)
            {
                message = "마지막 정상 백업에서 작업공간을 복원했지만 기본 파일을 복구하지 못했습니다.";
            }

            return new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.RecoveredFromBackup,
                                                   backup.Configuration, message);
        }

        if (primary.Status == StoredFileStatus.NotFound && backup.Status == StoredFileStatus.NotFound)
        {
            return new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.NotFound, null, null);
        }

        return new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.Invalid, null,
                                               "저장된 작업공간과 백업이 유효하지 않아 기본 탭으로 시작합니다.");
    }

    public async Task SaveAsync(TerminalWorkspaceConfiguration configuration,
                                CancellationToken cancellationToken)
    {
        TerminalWorkspaceConfigurationValidator.Validate(configuration);
        var storedConfiguration = StoredWorkspaceConfiguration.From(configuration);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(storedConfiguration, _serializerOptions);
        if (bytes.Length > MaximumFileSizeBytes)
        {
            throw new InvalidDataException("The terminal workspace exceeds the 64 KiB storage limit.");
        }

        var directory = Path.GetDirectoryName(workspacePath)
            ?? throw new InvalidOperationException("The terminal workspace path has no parent directory.");
        Directory.CreateDirectory(directory);

        try
        {
            var currentPrimary = await ReadAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            if (currentPrimary.Status == StoredFileStatus.FutureSchema)
            {
                throw new TerminalWorkspaceFutureSchemaException();
            }

            await WriteTemporaryFileAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (File.Exists(workspacePath) == true)
            {
                var backupDestination = currentPrimary.Status == StoredFileStatus.Valid
                    ? backupPath
                    : null;
                File.Replace(temporaryPath, workspacePath, backupDestination, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, workspacePath);
            }
        }
        finally
        {
            TryDeleteTemporaryFile();
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(workspacePath);
        File.Delete(backupPath);
        File.Delete(temporaryPath);

        return Task.CompletedTask;
    }

    private static async Task<StoredFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path) == false)
        {
            return new StoredFileReadResult(StoredFileStatus.NotFound, null);
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                    bufferSize: 4096,
                                                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumFileSizeBytes)
            {
                return new StoredFileReadResult(StoredFileStatus.Invalid, null);
            }

            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
                                                    {
                                                        AllowTrailingCommas = false,
                                                        CommentHandling = JsonCommentHandling.Disallow,
                                                        MaxDepth = 16,
                                                    });

            if (TryGetSchemaVersion(document.RootElement, out var schemaVersion) == false)
            {
                return new StoredFileReadResult(StoredFileStatus.Invalid, null);
            }

            if (schemaVersion > TerminalWorkspaceConfigurationValidator.CurrentSchemaVersion)
            {
                return new StoredFileReadResult(StoredFileStatus.FutureSchema, null);
            }

            var storedConfiguration = JsonSerializer.Deserialize<StoredWorkspaceConfiguration>(bytes,
                                                                                               _serializerOptions);
            var configuration = storedConfiguration?.ToConfiguration();
            if (configuration is null)
            {
                return new StoredFileReadResult(StoredFileStatus.Invalid, null);
            }

            TerminalWorkspaceConfigurationValidator.Validate(configuration);
            return new StoredFileReadResult(StoredFileStatus.Valid, configuration);
        }
        catch (Exception exception) when (IsInvalidFileException(exception) == true)
        {
            return new StoredFileReadResult(StoredFileStatus.Invalid, null);
        }
    }

    private async Task WriteTemporaryFileAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private async Task RestorePrimaryFromBackupAsync(CancellationToken cancellationToken)
    {
        await using (var source = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                 bufferSize: 4096,
                                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                                      bufferSize: 4096,
                                                      FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }

        if (File.Exists(workspacePath) == true)
        {
            File.Replace(temporaryPath, workspacePath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, workspacePath);
        }
    }

    private void TryDeleteTemporaryFile()
    {
        File.Delete(temporaryPath);
    }

    private static bool TryGetSchemaVersion(JsonElement root, out int schemaVersion)
    {
        schemaVersion = 0;
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("schemaVersion", out var value) == true &&
               value.TryGetInt32(out schemaVersion) == true;
    }

    private static TerminalWorkspaceLoadResult CreateFutureSchemaResult()
    {
        return new TerminalWorkspaceLoadResult(TerminalWorkspaceLoadStatus.FutureSchema, null,
                                               "더 새로운 버전의 작업공간 파일을 보존하기 위해 이번 실행에서는 복원과 자동 저장을 중단합니다.");
    }

    private static bool IsInvalidFileException(Exception exception)
    {
        return exception is ArgumentException or
               IOException or
               InvalidDataException or
               JsonException or
               NotSupportedException or
               UnauthorizedAccessException;
    }

    private static bool IsFileException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private enum StoredFileStatus
    {
        NotFound,
        Valid,
        Invalid,
        FutureSchema,
    }

    private sealed record StoredFileReadResult(StoredFileStatus Status,
                                               TerminalWorkspaceConfiguration? Configuration);

    private sealed record StoredWorkspaceConfiguration(int SchemaVersion, IReadOnlyList<StoredTabConfiguration?>? Tabs,
                                                       string? ActiveTabConfigurationId)
    {
        internal static StoredWorkspaceConfiguration From(TerminalWorkspaceConfiguration configuration)
        {
            return new StoredWorkspaceConfiguration(configuration.SchemaVersion,
                                                    configuration.Tabs.Select(StoredTabConfiguration.From).ToArray(),
                                                    configuration.ActiveTabConfigurationId?.ToString());
        }

        internal TerminalWorkspaceConfiguration ToConfiguration()
        {
            if (Tabs is null)
            {
                throw new InvalidDataException("The terminal workspace tabs are missing.");
            }

            var tabs = Tabs.Select(tab => tab?.ToConfiguration()
                                          ?? throw new InvalidDataException("A terminal workspace tab is null."))
                           .ToArray();
            TerminalTabConfigurationId? activeIdentifier = string.IsNullOrEmpty(ActiveTabConfigurationId)
                ? null
                : ParseIdentifier(ActiveTabConfigurationId);
            return new TerminalWorkspaceConfiguration(SchemaVersion, tabs, activeIdentifier);
        }
    }

    private sealed record StoredTabConfiguration(string? ConfigurationId, string? Name, int Order,
                                                 string? StartingDirectory, TerminalShellKind ShellKind)
    {
        internal static StoredTabConfiguration From(TerminalWorkspaceTabConfiguration tab)
        {
            return new StoredTabConfiguration(tab.ConfigurationId.ToString(), tab.Name, tab.Order,
                                              tab.StartingDirectory, tab.ShellKind);
        }

        internal TerminalWorkspaceTabConfiguration ToConfiguration()
        {
            if (Name is null || StartingDirectory is null)
            {
                throw new InvalidDataException("A terminal workspace tab contains a null field.");
            }

            return new TerminalWorkspaceTabConfiguration(ParseIdentifier(ConfigurationId), Name, Order,
                                                         StartingDirectory, ShellKind);
        }
    }

    private static TerminalTabConfigurationId ParseIdentifier(string? value)
    {
        if (value is null || Guid.TryParseExact(value, "N", out var identifier) == false || identifier == Guid.Empty)
        {
            throw new InvalidDataException("A terminal tab configuration identifier is invalid.");
        }

        return new TerminalTabConfigurationId(identifier);
    }
}
