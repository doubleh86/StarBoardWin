using System.Globalization;
using System.IO;
using System.Text;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Windows.Infrastructure;

internal sealed class FileDiagnosticLog : IDiagnosticLog
{
    internal const long MaximumLogFileBytes = 512 * 1024;
    internal const int MaximumLogFileCount = 5;

    private readonly Lock writeLock = new();
    private readonly string? logPath;

    internal FileDiagnosticLog()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Starboard", "Logs"))
    {
    }

    internal FileDiagnosticLog(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(logDirectory, "starboard.log");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logPath = null;
        }
    }

    public void Write(DiagnosticLevel level, string subsystem, string operation, string message,
                      Exception? exception = null)
    {
        if (logPath is null)
        {
            return;
        }

        lock (writeLock)
        {
            try
            {
                var line = CreateMetadataLine(level, subsystem, operation, exception);
                RotateIfRequired(Encoding.UTF8.GetByteCount(line + Environment.NewLine));
                using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(line);
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException or
                                                   ObjectDisposedException or NotSupportedException)
            {
                // Diagnostics are best-effort: an unavailable log must not change product behavior.
            }
        }
    }

    private void RotateIfRequired(int incomingByteCount)
    {
        if (logPath is null || File.Exists(logPath) == false)
        {
            return;
        }

        var currentLength = new FileInfo(logPath).Length;
        if (currentLength + incomingByteCount <= MaximumLogFileBytes)
        {
            return;
        }

        var oldestPath = GetArchivePath(MaximumLogFileCount - 1);
        File.Delete(oldestPath);
        for (var archiveIndex = MaximumLogFileCount - 2; archiveIndex >= 1; archiveIndex--)
        {
            var sourcePath = GetArchivePath(archiveIndex);
            if (File.Exists(sourcePath) == true)
            {
                File.Move(sourcePath, GetArchivePath(archiveIndex + 1));
            }
        }

        File.Move(logPath, GetArchivePath(1));
    }

    private string GetArchivePath(int archiveIndex)
    {
        return logPath + "." + archiveIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static string CreateMetadataLine(DiagnosticLevel level, string subsystem, string operation,
                                             Exception? exception)
    {
        var nativeCode = exception is System.ComponentModel.Win32Exception win32Exception
            ? win32Exception.NativeErrorCode.ToString(CultureInfo.InvariantCulture)
            : "-";
        return string.Join('\t', DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), level,
                           SanitizeMetadata(subsystem), SanitizeMetadata(operation), nativeCode);
    }

    private static string SanitizeMetadata(string value)
    {
        if (string.IsNullOrWhiteSpace(value) == true || value.Length > 64)
        {
            return "redacted";
        }

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) == false && character is not '.' and not '_' and not '-')
            {
                return "redacted";
            }
        }

        return value;
    }
}
