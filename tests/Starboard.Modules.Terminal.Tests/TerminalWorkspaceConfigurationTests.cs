using Starboard.Modules.Terminal;
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
    public void ValidateMoreThanEightTabsRejectsWorkspace()
    {
        var tabs = Enumerable.Range(1, 9)
            .Select(index => new TerminalWorkspaceTabConfiguration(
                new TerminalTabConfigurationId(CreateGuid(index)), $"탭 {index}", index - 1,
                $"C:\\Work\\{index}", TerminalShellKind.Automatic))
            .ToArray();
        var configuration = new TerminalWorkspaceConfiguration(1, tabs, tabs[0].ConfigurationId);

        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(configuration));
    }

    [TestMethod]
    public void ValidateUnknownActiveIdentifierRejectsWorkspace()
    {
        var tab = new TerminalWorkspaceTabConfiguration(new TerminalTabConfigurationId(CreateGuid(1)), "작업", 0,
                                                        "C:\\Work", TerminalShellKind.Automatic);
        var configuration = new TerminalWorkspaceConfiguration(1, [tab],
                                                               new TerminalTabConfigurationId(CreateGuid(2)));

        Assert.ThrowsExactly<ArgumentException>(() => TerminalWorkspaceConfigurationValidator.Validate(configuration));
    }

    [TestMethod]
    public void ValidateNonContiguousOrderRejectsWorkspace()
    {
        var tab = new TerminalWorkspaceTabConfiguration(new TerminalTabConfigurationId(CreateGuid(1)), "작업", 1,
                                                        "C:\\Work", TerminalShellKind.Automatic);
        var configuration = new TerminalWorkspaceConfiguration(1, [tab], tab.ConfigurationId);

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

    [TestMethod]
    public void CreateShutdownResultSavedStatusReportsSuccessfulFlush()
    {
        var result = TerminalModule.CreateShutdownResult(
            new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Saved, null));

        Assert.AreEqual(TerminalWorkspacePersistenceOperation.ShutdownFlush, result.Operation);
        Assert.AreEqual(TerminalWorkspacePersistenceStatus.Succeeded, result.Status);
        Assert.IsNull(result.FailureDetail);
    }

    [TestMethod]
    public void CreateShutdownResultFailurePreservesUserSafeMessage()
    {
        var result = TerminalModule.CreateShutdownResult(
            new TerminalWorkspaceSaveStatus(TerminalWorkspaceSaveState.Failed, "종료 전에 저장하지 못했습니다."));

        Assert.AreEqual(TerminalWorkspacePersistenceOperation.ShutdownFlush, result.Operation);
        Assert.AreEqual(TerminalWorkspacePersistenceStatus.Failed, result.Status);
        Assert.AreEqual("종료 전에 저장하지 못했습니다.", result.FailureDetail);
    }

    private static Guid CreateGuid(int value)
    {
        return Guid.Parse($"20000000-0000-0000-0000-{value:D12}");
    }
}
