using System.IO;
using System.Text.RegularExpressions;

namespace Starboard.Modules.Terminal.Domain;

internal sealed record TerminalStartingDirectoryUpdateResult(bool Succeeded, string? NormalizedPath,
                                                             string? ErrorMessage);

internal static class TerminalStartingDirectory
{
    private static readonly Regex _localAbsolutePathPattern = new("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant,
                                                                  TimeSpan.FromMilliseconds(100));
    private static readonly Regex _expansionPattern = new("%[^%\\r\\n]+%|![^!\\r\\n]+!|\\$(?:[A-Za-z_][A-Za-z0-9_]*|[0-9]+|\\{|\\()",
                                                          RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                                                          TimeSpan.FromMilliseconds(100));

    internal static TerminalStartingDirectoryUpdateResult ValidateExisting(string? path)
    {
        if (TryNormalizeSupportedPath(path, out var normalizedPath, out var errorMessage) == false)
        {
            return new TerminalStartingDirectoryUpdateResult(false, null, errorMessage);
        }

        if (Directory.Exists(normalizedPath) == false)
        {
            return new TerminalStartingDirectoryUpdateResult(false, null,
                                                             "시작 폴더가 없거나 접근할 수 없습니다.");
        }

        return new TerminalStartingDirectoryUpdateResult(true, normalizedPath, null);
    }

    internal static string GetRequiredExisting(string path)
    {
        var result = ValidateExisting(path);
        if (result.Succeeded == false || result.NormalizedPath is null)
        {
            throw new DirectoryNotFoundException(result.ErrorMessage);
        }

        return result.NormalizedPath;
    }

    internal static bool IsSupportedLocalAbsolutePath(string? path)
    {
        return TryNormalizeSupportedPath(path, out _, out _);
    }

    private static bool TryNormalizeSupportedPath(string? path, out string normalizedPath, out string errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = "시작 폴더에 로컬 절대 경로를 입력해 주세요.";
        if (string.IsNullOrWhiteSpace(path) == true)
        {
            return false;
        }

        var candidate = path.Trim();
        if (_localAbsolutePathPattern.IsMatch(candidate) == false ||
            candidate.StartsWith("\\\\", StringComparison.Ordinal) == true ||
            candidate.StartsWith("//", StringComparison.Ordinal) == true)
        {
            return false;
        }

        if (_expansionPattern.IsMatch(candidate) == true || candidate.Contains('`', StringComparison.Ordinal) == true)
        {
            errorMessage = "환경 변수와 명령 치환은 시작 폴더에서 사용할 수 없습니다.";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(candidate);
            return _localAbsolutePathPattern.IsMatch(normalizedPath) == true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
