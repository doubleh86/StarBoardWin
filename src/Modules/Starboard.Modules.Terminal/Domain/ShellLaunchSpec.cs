namespace Starboard.Modules.Terminal.Domain;

internal sealed record ShellLaunchSpec(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory);
