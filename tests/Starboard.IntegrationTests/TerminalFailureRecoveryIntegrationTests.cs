using System.IO;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class TerminalFailureRecoveryIntegrationTests
{
    [TestMethod]
    public void RendererRecoveryRecreatesWebViewAndResynchronizesLiveShellGenerations()
    {
        var source = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                        "TerminalView.xaml.cs");

        StringAssert.Contains(source, "rendererRequiresRecreation = true;");
        StringAssert.Contains(source, "RecreateRendererControl();");
        StringAssert.Contains(source, "renderer = CreateRendererControl();");
        StringAssert.Contains(source, "SyncWorkspace(sessionCoordinator.Snapshot);");
        StringAssert.Contains(source, "SchedulePendingOutputFlushes();");
        StringAssert.Contains(source, "sessionCoordinator.IsCurrentSession(output.Session) == false");
        StringAssert.Contains(source, "sessionCoordinator.IsCurrentSession(session)");
        StringAssert.Contains(source, "ReferenceEquals(sender, renderer.CoreWebView2) == false");
    }

    [TestMethod]
    public void RuntimeMissingSurfaceOffersOfflineGuidanceRetryAndHostOwnedExit()
    {
        var viewSource = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                            "TerminalView.xaml.cs");
        var viewMarkup = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                            "TerminalView.xaml");
        var moduleSource = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal",
                                              "TerminalModule.cs");
        var hostSource = ReadRepositoryFile("src", "Starboard.Windows", "Composition", "AppCoordinator.cs");

        StringAssert.Contains(viewSource, "exception is WebView2RuntimeNotFoundException");
        StringAssert.Contains(viewSource, "Evergreen \" +");
        StringAssert.Contains(viewSource, "Standalone Installer");
        StringAssert.Contains(viewMarkup, "x:Name=\"RetryButton\"");
        StringAssert.Contains(viewMarkup, "x:Name=\"ExitButton\"");
        StringAssert.Contains(moduleSource, "public event EventHandler? ExitRequested;");
        StringAssert.Contains(hostSource, "terminalModule.ExitRequested += HandleExitRequested;");
        StringAssert.Contains(hostSource, "terminalModule.ExitRequested -= HandleExitRequested;");
    }

    [TestMethod]
    public void RecoveryDiagnosticsNeverAppendRendererBacklogOrInputContent()
    {
        var source = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                        "TerminalView.xaml.cs");

        Assert.IsFalse(source.Contains("diagnosticLog.Write(output.Data", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("diagnosticLog.Write(data", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("exception.Message, exception", StringComparison.Ordinal));
        StringAssert.Contains(source, "the oldest data was discarded");
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
