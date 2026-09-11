using System.IO;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Application;

internal sealed record TerminalPathDropFormatResult(bool Succeeded, string? QuotedInput, string? ErrorMessage);

internal static class TerminalPathDropFormatter
{
    internal const int MaximumPathCount = 32;
    internal const int MaximumInputLength = 32_767;

    internal static TerminalPathDropFormatResult Format(IReadOnlyList<string> paths, TerminalShellKind shellKind)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return Failed("드롭한 파일이나 폴더 경로를 확인하지 못했습니다.");
        }

        if (paths.Count > MaximumPathCount)
        {
            return Failed($"한 번에 {MaximumPathCount}개까지만 경로를 넣을 수 있습니다.");
        }

        if (shellKind is not TerminalShellKind.Pwsh and
            not TerminalShellKind.PowerShell and
            not TerminalShellKind.Cmd)
        {
            return Failed("현재 탭의 shell에서 안전한 경로 인용 방식을 확정할 수 없습니다.");
        }

        var quotedPaths = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            var validationError = ValidatePath(path, shellKind);
            if (validationError is not null)
            {
                return Failed(validationError);
            }

            var quotedPath = shellKind == TerminalShellKind.Cmd
                ? $"\"{path}\""
                : $"'{path.Replace("'", "''", StringComparison.Ordinal)}'";
            quotedPaths.Add(quotedPath);
        }

        var quotedInput = string.Join(' ', quotedPaths);
        if (quotedInput.Length > MaximumInputLength)
        {
            return Failed("드롭한 경로가 너무 길어 shell 입력에 안전하게 넣을 수 없습니다.");
        }

        return new TerminalPathDropFormatResult(true, quotedInput, null);
    }

    private static string? ValidatePath(string path, TerminalShellKind shellKind)
    {
        if (string.IsNullOrWhiteSpace(path) == true)
        {
            return "빈 경로는 shell 입력에 넣을 수 없습니다.";
        }

        if (path.Length > MaximumInputLength || path.Any(character => char.IsControl(character)) == true)
        {
            return "제어 문자가 있거나 너무 긴 경로는 shell 입력에 넣을 수 없습니다.";
        }

        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) == true ||
            path.StartsWith("\\\\.\\", StringComparison.Ordinal) == true ||
            Path.IsPathFullyQualified(path) == false)
        {
            return "표준 Windows 절대 경로만 shell 입력에 넣을 수 있습니다.";
        }

        if (path.Contains('"') == true)
        {
            return "현재 shell에서 안전하게 인용할 수 없는 문자가 경로에 있습니다.";
        }

        if (shellKind == TerminalShellKind.Cmd &&
            (path.Contains('%') == true || path.Contains('!') == true))
        {
            return "cmd.exe에서 `%`나 `!`가 있는 경로는 안전하게 인용할 수 없습니다.";
        }

        return null;
    }

    private static TerminalPathDropFormatResult Failed(string message)
    {
        return new TerminalPathDropFormatResult(false, null, message);
    }
}
