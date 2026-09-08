using System.IO;
using System.Text.Json;
using Starboard.Modules.Preferences.Application;
using Starboard.Modules.Preferences.Contracts;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Modules.Preferences.Infrastructure;

internal sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IDiagnosticLog diagnosticLog;
    private readonly string settingsPath;

    internal JsonSettingsStore(IDiagnosticLog diagnosticLog, string settingsPath)
    {
        this.diagnosticLog = diagnosticLog;
        this.settingsPath = settingsPath;
    }

    internal async Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(settingsPath) == false)
        {
            if (File.Exists(settingsPath + ".bak") == true)
            {
                return await TryRecoverBackupAsync(
                    new FileNotFoundException("The primary settings file is missing.", settingsPath),
                    cancellationToken);
            }

            return new PreferencesLoadResult(new AppSettings(), true, null);
        }

        try
        {
            var settings = await ReadAsync(settingsPath, cancellationToken);

            return new PreferencesLoadResult(
                settings,
                false,
                null);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return await TryRecoverBackupAsync(exception, cancellationToken);
        }
    }

    internal async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = SettingsValidator.Normalize(settings);
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        var temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = settingsPath + ".bak";

        Directory.CreateDirectory(directory);
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(settingsPath) == true)
            {
                File.Replace(temporaryPath, settingsPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, settingsPath);
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

    private async Task<PreferencesLoadResult> TryRecoverBackupAsync(
        Exception primaryException,
        CancellationToken cancellationToken)
    {
        var backupPath = settingsPath + ".bak";
        if (File.Exists(backupPath) == false)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Preferences",
                "Load",
                "Settings could not be read; defaults will be used.",
                primaryException);

            return new PreferencesLoadResult(
                new AppSettings(),
                true,
                "설정 파일을 읽지 못해 기본값으로 시작했습니다.");
        }

        try
        {
            var settings = await ReadAsync(backupPath, cancellationToken);
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Preferences",
                "LoadBackup",
                "The primary settings file could not be read; the previous settings were restored.",
                primaryException);

            return new PreferencesLoadResult(
                settings,
                false,
                "설정 파일을 읽지 못해 이전 설정으로 복구했습니다.");
        }
        catch (Exception backupException) when (backupException is IOException or JsonException or UnauthorizedAccessException)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Preferences",
                "LoadBackup",
                "The backup settings file could not be read; defaults will be used.",
                backupException);

            return new PreferencesLoadResult(
                new AppSettings(),
                true,
                "설정 파일과 이전 설정을 읽지 못해 기본값으로 시작했습니다.");
        }
    }

    private static async Task<AppSettings> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var candidate = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            SerializerOptions,
            cancellationToken);

        return SettingsValidator.Normalize(candidate);
    }
}
