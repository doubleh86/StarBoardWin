using System.Globalization;
using System.IO;
using Starboard.SharedKernel.Diagnostics;

namespace Starboard.Windows.Infrastructure;

internal sealed class FileDiagnosticLog : IDiagnosticLog
{
    private readonly Lock writeLock = new();
    private readonly string? logPath;

    internal FileDiagnosticLog()
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "Starboard", "Logs");
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

        var nativeCode = exception is System.ComponentModel.Win32Exception win32Exception
            ? win32Exception.NativeErrorCode.ToString(CultureInfo.InvariantCulture)
            : "-";
        var line = string.Join('\t', DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture), level, subsystem,
                               operation, nativeCode, message.ReplaceLineEndings(" "));

        lock (writeLock)
        {
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }
}
