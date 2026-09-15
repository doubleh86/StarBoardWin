using System.IO;
using Starboard.Windows.Composition;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class PortableReleaseSmokeCheckTests
{
    [TestMethod]
    public void PortablePackagePolicyIncludesTheCanonicalTrayIconAndRejectsUserData()
    {
        var source = ReadRepositoryFile("scripts", "package-portable.ps1");

        StringAssert.Contains(source, "\"Assets/Starboard.ico\"");
        StringAssert.Contains(source, "src/Starboard.Windows/Assets/Starboard.ico");
        StringAssert.Contains(source, "\"settings.json\"");
        StringAssert.Contains(source, "\"saved-tabs.json\"");
        StringAssert.Contains(source, "\"saved-tabs.json.bak\"");
        StringAssert.Contains(source, "\"saved-tabs.json.tmp\"");
        StringAssert.Contains(source, "\"workspace.json\"");
        StringAssert.Contains(source, "\"workspace.json.bak\"");
        StringAssert.Contains(source, "\"command-history.txt\"");
        StringAssert.Contains(source, "\"command-output.txt\"");
        StringAssert.Contains(source, "\"terminal-command.txt\"");
        StringAssert.Contains(source, "\"terminal-output.txt\"");
    }

    [TestMethod]
    public void DisplayTextIncludesAvailableShortBuildCommit()
    {
        var buildInfo = new ProductBuildInfo("1.2.3", "0123456789abcdef0123456789abcdef01234567");

        Assert.AreEqual("버전 1.2.3 · 빌드 0123456", buildInfo.DisplayText);
    }

    [TestMethod]
    public void DisplayTextOmitsUnavailableBuildCommit()
    {
        var buildInfo = new ProductBuildInfo("1.2.3", null);

        Assert.AreEqual("버전 1.2.3", buildInfo.DisplayText);
    }

    [TestMethod]
    public void TryRunWithoutSmokeSwitchLeavesNormalStartupUnchanged()
    {
        var result = PortableReleaseSmokeCheck.TryRun(["Starboard.exe"], new ProductBuildInfo("1.2.3", null),
                                                      @"C:\release");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ValidateMatchingMetadataFilesAndShellSucceeds()
    {
        var arguments = new[]
        {
            "Starboard.exe",
            "--portable-smoke-test",
            "--expected-version",
            "1.2.3",
            "--expected-commit",
            "0123456789abcdef0123456789abcdef01234567",
        };

        var result = PortableReleaseSmokeCheck.Validate(arguments,
                                                        new ProductBuildInfo("1.2.3", "0123456789abcdef0123456789abcdef01234567"),
                                                        @"C:\release", _ => true,
                                                        () => @"C:\Program Files\PowerShell\7\pwsh.exe");

        Assert.AreEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void ValidateMismatchedBuildMetadataFailsBeforeFileChecks()
    {
        var fileCheckWasCalled = false;
        var arguments = new[]
        {
            "Starboard.exe",
            "--portable-smoke-test",
            "--expected-version",
            "1.2.4",
            "--expected-commit",
            "fedcba9876543210fedcba9876543210fedcba98",
        };

        var result = PortableReleaseSmokeCheck.Validate(arguments,
                                                        new ProductBuildInfo("1.2.3", "0123456789abcdef0123456789abcdef01234567"),
                                                        @"C:\release",
                                                        _ =>
                                                        {
                                                            fileCheckWasCalled = true;
                                                            return true;
                                                        },
                                                        () => @"C:\Windows\System32\cmd.exe");

        Assert.AreEqual(3, result.ExitCode);
        Assert.IsFalse(fileCheckWasCalled);
    }

    [TestMethod]
    public void ValidateMissingRendererFailsBeforeShellCheck()
    {
        var shellCheckWasCalled = false;
        var arguments = new[]
        {
            "Starboard.exe",
            "--portable-smoke-test",
            "--expected-version",
            "1.2.3",
            "--expected-commit",
            "0123456789abcdef0123456789abcdef01234567",
        };

        var result = PortableReleaseSmokeCheck.Validate(arguments,
                                                        new ProductBuildInfo("1.2.3", "0123456789abcdef0123456789abcdef01234567"),
                                                        @"C:\release",
                                                        path => path.EndsWith(Path.Combine("Renderer", "app.js"), StringComparison.OrdinalIgnoreCase) == false,
                                                        () =>
                                                        {
                                                            shellCheckWasCalled = true;
                                                            return @"C:\Windows\System32\cmd.exe";
                                                        });

        Assert.AreEqual(4, result.ExitCode);
        Assert.IsFalse(shellCheckWasCalled);
    }

    private static string ReadRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.Parent is not null &&
               File.Exists(Path.Combine(directory.FullName, "Starboard.Windows.sln")) == false)
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine([directory.FullName, .. relativeSegments]));
    }
}
