using Starboard.Modules.Terminal.Contracts;
using Starboard.Modules.Terminal.Domain;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalWorkspaceConfigurationTests
{
    [TestMethod]
    public void ValidateConfigurationContainsNoRuntimeSessionIdentity()
    {
        var firstId = new TerminalTabConfigurationId(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        var secondId = new TerminalTabConfigurationId(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        var configuration = new TerminalWorkspaceConfiguration(1,
                                                               [
                                                                    new TerminalWorkspaceTabConfiguration(firstId, "서버", 0,
                                                                                                          "C:\\Work\\Server", TerminalShellKind.Pwsh),
                                                                    new TerminalWorkspaceTabConfiguration(secondId, "웹", 1,
                                                                                                          "C:\\Work\\Web", TerminalShellKind.Automatic),
                                                               ],
                                                               secondId);

        TerminalWorkspaceConfigurationValidator.Validate(configuration);

        Assert.AreEqual(secondId, configuration.ActiveTabConfigurationId);
        Assert.AreEqual(TerminalShellKind.Automatic, configuration.Tabs[1].ShellKind);
        Assert.IsFalse(typeof(TerminalWorkspaceConfiguration).GetProperties()
                       .Any(property => property.Name.Contains("Session", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ValidateDuplicateConfigurationIdentifierRejectsWorkspace()
    {
        var identifier = new TerminalTabConfigurationId(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        var configuration = new TerminalWorkspaceConfiguration(1,
                                                               [
                                                                    new TerminalWorkspaceTabConfiguration(identifier, "첫 탭", 0,
                                                                                                          "C:\\Work\\One", TerminalShellKind.Pwsh),
                                                                    new TerminalWorkspaceTabConfiguration(identifier, "둘째 탭", 1,
                                                                                                          "C:\\Work\\Two", TerminalShellKind.Cmd),
                                                               ],
                                                               identifier);

        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(configuration));
    }

    [TestMethod]
    [DataRow("..\\Work")]
    [DataRow("\\\\server\\share")]
    [DataRow("\\\\?\\C:\\Work")]
    [DataRow("C:\\%USERPROFILE%")]
    [DataRow("C:\\$(Get-Location)")]
    public void ValidateUnsupportedStartingDirectoryRejectsWorkspace(string startingDirectory)
    {
        var identifier = new TerminalTabConfigurationId(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        var configuration = new TerminalWorkspaceConfiguration(1,
                                                               [
                                                                    new TerminalWorkspaceTabConfiguration(identifier, "작업", 0,
                                                                                                          startingDirectory, TerminalShellKind.PowerShell),
                                                               ],
                                                               identifier);

        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(configuration));
    }

    [TestMethod]
    public void PersistenceResultFailureRetainsOperationForRetryUi()
    {
        var result = new TerminalWorkspacePersistenceResult(TerminalWorkspacePersistenceOperation.ShutdownFlush,
                                                            TerminalWorkspacePersistenceStatus.Failed,
                                                            "구성을 저장하지 못했습니다.");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(TerminalWorkspacePersistenceOperation.ShutdownFlush, result.Operation);
    }
}
