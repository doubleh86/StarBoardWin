using Microsoft.Win32;

namespace Starboard.Modules.DesktopIntegration.Infrastructure;

internal interface IStartupRegistration
{
    void SetEnabled(bool isEnabled);
}

internal interface IExecutablePathProvider
{
    string GetExecutablePath();
}

internal sealed class ProcessExecutablePathProvider : IExecutablePathProvider
{
    public string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
    }
}

internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string _RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string _ValueName = "Starboard";

    private readonly IExecutablePathProvider executablePathProvider;

    internal RegistryStartupRegistration(IExecutablePathProvider executablePathProvider)
    {
        ArgumentNullException.ThrowIfNull(executablePathProvider);
        this.executablePathProvider = executablePathProvider;
    }

    public void SetEnabled(bool isEnabled)
    {
        if (isEnabled == false)
        {
            using var existingRunKey = Registry.CurrentUser.OpenSubKey(_RunKeyPath, true);
            existingRunKey?.DeleteValue(_ValueName, false);

            return;
        }

        using var runKey = Registry.CurrentUser.CreateSubKey(_RunKeyPath, true);
        if (runKey is null)
        {
            throw new InvalidOperationException("The current-user startup registry key could not be opened.");
        }

        var executablePath = executablePathProvider.GetExecutablePath();
        runKey.SetValue(_ValueName, QuoteExecutablePath(executablePath), RegistryValueKind.String);
    }

    internal static string QuoteExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) == true || executablePath.Contains('"') == true)
        {
            throw new ArgumentException("The executable path cannot be safely quoted.", nameof(executablePath));
        }

        return $"\"{executablePath}\"";
    }
}
