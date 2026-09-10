using System.IO;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class SavedTerminalTabsIntegrationTests
{
    [TestMethod]
    public void TerminalModuleComposesSavedTabPersistenceBeforeViewAndDisposesItBeforeSessions()
    {
        var source = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "TerminalModule.cs");

        StringAssert.Contains(source,
                              "new FileTerminalSavedTabStore(FileTerminalSavedTabStore.GetDefaultPath())");
        StringAssert.Contains(source,
                              "new TerminalSavedTabService(savedTabStore, sessionCoordinator, diagnosticLog)");
        StringAssert.Contains(source,
                              "new TerminalView(diagnosticLog, sessionCoordinator, workspacePersistence, savedTabService)");

        var viewShutdown = source.IndexOf("await terminalView.DisposeAsync();", StringComparison.Ordinal);
        var savedTabsShutdown = source.IndexOf("await savedTabService.DisposeAsync();", StringComparison.Ordinal);
        var sessionsShutdown = source.IndexOf("await sessionCoordinator.DisposeAsync();", StringComparison.Ordinal);
        Assert.IsTrue(viewShutdown >= 0 && viewShutdown < savedTabsShutdown && savedTabsShutdown < sessionsShutdown,
                      "Renderer input must stop before saved-tab requests, and saved-tab launches must stop before sessions.");
    }

    [TestMethod]
    public void TerminalViewRoutesSavedTabCommandsAndResynchronizesAfterRendererRecovery()
    {
        var source = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                        "TerminalView.xaml.cs");

        foreach (var messageType in new[]
                 {
                     "CreateSavedTab",
                     "UpdateSavedTab",
                     "DeleteSavedTab",
                     "LaunchSavedTab",
                     "CancelSavedTabLaunch",
                 })
        {
            StringAssert.Contains(source, $"case RendererMessageType.{messageType}:");
        }

        StringAssert.Contains(source, "savedTabLaunchRequestIds.Add(request.RequestId) == false");
        StringAssert.Contains(source, "rendererGeneration == requestRendererGeneration");
        StringAssert.Contains(source, "RendererProtocol.SerializeSavedTabsSnapshot(");
        StringAssert.Contains(source, "await InitializeSavedTabsAsync(cancellationToken);");
        StringAssert.Contains(source, "await StartWorkspaceAsync(terminalOptions.RestoreWorkspaceOnLaunch,");
    }

    [TestMethod]
    public void PortablePackagingPolicyRejectsSavedTabPrimaryBackupAndTemporaryFiles()
    {
        var source = ReadRepositoryFile("scripts", "package-portable.ps1");

        StringAssert.Contains(source, "\"saved-tabs.json\"");
        StringAssert.Contains(source, "\"saved-tabs.json.bak\"");
        StringAssert.Contains(source, "\"saved-tabs.json.tmp\"");
        StringAssert.Contains(source, "$file.Extension -in @(\".bak\", \".tmp\", \".log\"");
        Assert.IsFalse(source.Contains("[System.IO.Path]::GetRelativePath", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("$startInfo.ArgumentList", StringComparison.Ordinal));
    }

    private static string ReadRepositoryFile(params string[] pathSegments)
    {
        var root = FindSolutionRoot();
        return File.ReadAllText(Path.Combine([root, .. pathSegments]));
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Starboard.Windows.sln")) == true)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Starboard.Windows.sln could not be located.");
    }
}
