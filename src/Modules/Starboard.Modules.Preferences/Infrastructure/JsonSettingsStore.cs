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
            return new PreferencesLoadResult(new AppSettings(), true, null);
        }

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            var candidate = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            return new PreferencesLoadResult(
                SettingsValidator.Normalize(candidate),
                false,
                null);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            diagnosticLog.Write(
                DiagnosticLevel.Warning,
                "Preferences",
                "Load",
                "Settings could not be read; defaults will be used.",
                exception);

            return new PreferencesLoadResult(
                new AppSettings(),
                true,
                "설정 파일을 읽지 못해 기본값으로 시작했습니다.");
        }
    }

    internal async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = SettingsValidator.Normalize(settings);
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        var temporaryPath = settingsPath + ".tmp";
        var backupPath = settingsPath + ".bak";

        Directory.CreateDirectory(directory);

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
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
}
