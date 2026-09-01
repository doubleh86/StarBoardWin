namespace Starboard.SharedKernel.Diagnostics;

public interface IDiagnosticLog
{
    void Write(
        DiagnosticLevel level,
        string subsystem,
        string operation,
        string message,
        Exception? exception = null);
}
