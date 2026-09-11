using Starboard.Modules.Terminal.Application;
using Starboard.Modules.Terminal.Contracts;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalPathDropFormatterTests
{
    [TestMethod]
    public void FormatPowerShellPathsQuotesSpacesKoreanAndMetacharactersWithoutLineBreak()
    {
        var paths = new[]
        {
            "C:\\한글 폴더\\a'b&$(test).txt",
            "D:\\work\\[draft];done",
        };

        var result = TerminalPathDropFormatter.Format(paths, TerminalShellKind.Pwsh);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("'C:\\한글 폴더\\a''b&$(test).txt' 'D:\\work\\[draft];done'", result.QuotedInput);
        Assert.IsFalse(result.QuotedInput!.Contains('\r'));
        Assert.IsFalse(result.QuotedInput.Contains('\n'));
    }

    [TestMethod]
    public void FormatCmdPathQuotesWhitespaceAndSupportedMetacharacters()
    {
        var result = TerminalPathDropFormatter.Format(["C:\\한글 폴더\\a&b^(draft).txt"], TerminalShellKind.Cmd);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("\"C:\\한글 폴더\\a&b^(draft).txt\"", result.QuotedInput);
    }

    [TestMethod]
    [DataRow("C:\\work\\%PATH%\\file.txt")]
    [DataRow("C:\\work\\delayed!name!.txt")]
    public void FormatCmdExpansionCharactersRejectsUnsafeInput(string path)
    {
        var result = TerminalPathDropFormatter.Format([path], TerminalShellKind.Cmd);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.QuotedInput);
        StringAssert.Contains(result.ErrorMessage, "cmd.exe");
    }

    [TestMethod]
    [DataRow("relative\\file.txt")]
    [DataRow("\\\\?\\C:\\work\\file.txt")]
    [DataRow("C:\\work\\line\nbreak.txt")]
    public void FormatUnsupportedPathRejectsInput(string path)
    {
        var result = TerminalPathDropFormatter.Format([path], TerminalShellKind.PowerShell);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.QuotedInput);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [TestMethod]
    public void FormatUnknownShellOrTooManyPathsRejectsInput()
    {
        var unknownShell = TerminalPathDropFormatter.Format(["C:\\work\\file.txt"], TerminalShellKind.Automatic);
        var tooManyPaths = Enumerable.Range(0, TerminalPathDropFormatter.MaximumPathCount + 1)
            .Select(index => $"C:\\work\\{index}.txt")
            .ToArray();
        var tooMany = TerminalPathDropFormatter.Format(tooManyPaths, TerminalShellKind.Pwsh);

        Assert.IsFalse(unknownShell.Succeeded);
        Assert.IsFalse(tooMany.Succeeded);
    }
}
