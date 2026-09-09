using Starboard.Modules.Terminal.Application;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class ShellResolverTests
{
    [TestMethod]
    public void FindExecutableWithFullyQualifiedExistingPathReturnsFullPath()
    {
        var assemblyPath = typeof(ShellResolverTests).Assembly.Location;

        var result = ShellResolver.FindExecutable(assemblyPath);

        Assert.AreEqual(Path.GetFullPath(assemblyPath), result);
    }

    [TestMethod]
    public void FindExecutableWithSearchDirectoryFindsNamedFile()
    {
        var assemblyPath = typeof(ShellResolverTests).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException("The test assembly has no directory.");

        var result = ShellResolver.FindExecutable(Path.GetFileName(assemblyPath), [directory]);

        Assert.AreEqual(Path.GetFullPath(assemblyPath), result);
    }

    [TestMethod]
    public void FindExecutableWithMissingFileReturnsNull()
    {
        var result = ShellResolver.FindExecutable("missing-starboard-shell.exe", [Path.GetTempPath()]);

        Assert.IsNull(result);
    }
}
