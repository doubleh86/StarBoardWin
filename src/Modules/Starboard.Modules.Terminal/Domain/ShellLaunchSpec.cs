using System.Collections.ObjectModel;

namespace Starboard.Modules.Terminal.Domain;

internal sealed record ShellLaunchSpec
{
    internal ShellLaunchSpec(string executablePath, string arguments, string workingDirectory)
        : this(executablePath,
               arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
               workingDirectory)
    {
    }

    internal ShellLaunchSpec(string executablePath, IEnumerable<string> arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var argumentSnapshot = arguments.ToArray();
        if (argumentSnapshot.Any(argument => argument is null) == true)
        {
            throw new ArgumentException("A shell launch argument cannot be null.", nameof(arguments));
        }

        ExecutablePath = executablePath;
        Arguments = new ReadOnlyCollection<string>(argumentSnapshot);
        WorkingDirectory = workingDirectory;
    }

    internal string ExecutablePath { get; }

    internal IReadOnlyList<string> Arguments { get; }

    internal string WorkingDirectory { get; init; }
}
