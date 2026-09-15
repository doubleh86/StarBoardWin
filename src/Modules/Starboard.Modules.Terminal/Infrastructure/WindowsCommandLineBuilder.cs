using System.Text;

namespace Starboard.Modules.Terminal.Infrastructure;

internal static class WindowsCommandLineBuilder
{
    internal static string Build(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var commandLine = new StringBuilder();
        AppendQuotedArgument(commandLine, executablePath);
        foreach (var argument in arguments)
        {
            commandLine.Append(' ');
            AppendQuotedArgument(commandLine, argument);
        }

        return commandLine.ToString();
    }

    private static void AppendQuotedArgument(StringBuilder commandLine, string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && argument.Any(character => char.IsWhiteSpace(character) || character == '"') == false)
        {
            commandLine.Append(argument);
            return;
        }

        commandLine.Append('"');
        var backslashCount = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                commandLine.Append('\\', (backslashCount * 2) + 1);
                commandLine.Append('"');
                backslashCount = 0;
                continue;
            }

            commandLine.Append('\\', backslashCount);
            backslashCount = 0;
            commandLine.Append(character);
        }

        commandLine.Append('\\', backslashCount * 2);
        commandLine.Append('"');
    }
}
