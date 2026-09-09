using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalStartingDirectoryTests
{
    [TestMethod]
    public void ValidateExistingDirectoryWithSpacesAndKoreanReturnsNormalizedPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Starboard 한글 폴더 {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var result = TerminalStartingDirectory.ValidateExisting(directory);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(Path.GetFullPath(directory), result.NormalizedPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("relative\\folder")]
    [DataRow("\\\\server\\share")]
    [DataRow("\\\\?\\C:\\Work")]
    [DataRow("C:\\%USERPROFILE%")]
    [DataRow("C:\\$env:TEMP")]
    [DataRow("C:\\$(whoami)")]
    [DataRow("C:\\!TEMP!")]
    public void IsSupportedLocalAbsolutePathWithUnsafeOrExpandablePathReturnsFalse(string path)
    {
        Assert.IsFalse(TerminalStartingDirectory.IsSupportedLocalAbsolutePath(path));
    }

    [TestMethod]
    public void GetRequiredExistingAfterDirectoryDeletionThrowsDirectoryNotFoundException()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Starboard removed {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Directory.Delete(directory, recursive: true);

        Assert.ThrowsExactly<DirectoryNotFoundException>(() => TerminalStartingDirectory.GetRequiredExisting(directory));
    }
}
