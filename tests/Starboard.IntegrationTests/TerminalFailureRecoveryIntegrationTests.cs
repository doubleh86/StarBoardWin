using System.IO;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class TerminalFailureRecoveryIntegrationTests
{
    [TestMethod]
    public void ConfirmedPasteUsesRendererPasteAndRevalidatesClipboardLifetime()
    {
        // Native WebView2 dispatch is not exercised by this host wiring guard.
        var source = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                        "TerminalView.xaml.cs");
        var start = source.IndexOf("if (pending.Kind == ConfirmationKind.Paste)", StringComparison.Ordinal);
        var end = source.IndexOf("if (pending.Kind == ConfirmationKind.UrlOpen)", start, StringComparison.Ordinal);
        var paste = source[start..end];
        var capture = paste.IndexOf("CreateClipboardRequest(sessionId)", StringComparison.Ordinal);
        var consume = paste.IndexOf("await sessionCoordinator.TakeConfirmedPasteAsync(", StringComparison.Ordinal);
        var revalidate = paste.IndexOf("IsClipboardRequestCurrent(request) == true", StringComparison.Ordinal);
        var deliver = paste.IndexOf("SendSessionMessage(\"paste\"", StringComparison.Ordinal);

        Assert.IsTrue(capture >= 0 && capture < consume && consume < revalidate && revalidate < deliver);
        StringAssert.Contains(paste, "request.Session == response.Token.Session");
        StringAssert.Contains(paste, "sessionGeneration = response.Token.Session.Generation");
        Assert.IsFalse(paste.Contains("WriteInputAsync(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReorderHostWiringGuardsSenderAndDelimitsOneCurrentWorkspaceSnapshot()
    {
        // Static wiring guard complements the executable renderer/coordinator round trip.
        // It does not exercise native WebView2 event dispatch.
        var source = ReadRepositoryFile("src", "Modules", "Starboard.Modules.Terminal", "Presentation",
                                        "TerminalView.xaml.cs");
        StringAssert.Contains(source, "ReorderSession(reorderRequest, rendererGeneration, isStaleSender == false);");
        StringAssert.Contains(source, "isReorderEnvelope == true && CreateReorderContext().IsAvailable == true");
        var reorderStart = source.IndexOf("private void ReorderSession(", StringComparison.Ordinal);
        var reorderEnd = source.IndexOf("private void SelectStartingDirectory(", reorderStart, StringComparison.Ordinal);
        var reorder = source[reorderStart..reorderEnd];
        StringAssert.Contains(reorder, "context.CanApply(request, requestRendererGeneration, isCurrentSender) == true");
        StringAssert.Contains(reorder, "sessionCoordinator.Reorder(request)");
        StringAssert.Contains(reorder, "result == TerminalTabReorderResult.Moved");
        Assert.IsTrue(reorder.IndexOf("return;", StringComparison.Ordinal) <
                      reorder.IndexOf("SyncWorkspace(sessionCoordinator.Snapshot)", StringComparison.Ordinal));

        var syncStart = source.IndexOf("private void SyncWorkspace(", StringComparison.Ordinal);
        var syncEnd = source.IndexOf("private void Session_NewOutputStateChanged(", syncStart, StringComparison.Ordinal);
        var sync = source[syncStart..syncEnd];
        var snapshot = sync.IndexOf("var rendererWorkspace = sessionCoordinator.RendererWorkspace;", StringComparison.Ordinal);
        var begin = sync.IndexOf("SerializeWorkspaceSyncMessage(\"workspace-sync-begin\", rendererWorkspace)", StringComparison.Ordinal);
        var upsert = sync.IndexOf("SendSessionMessage(\"session-upsert\"", StringComparison.Ordinal);
        var activate = sync.IndexOf("SendSessionMessage(\"activate-session\"", StringComparison.Ordinal);
        var complete = sync.IndexOf("SerializeWorkspaceSyncMessage(\"workspace-sync-complete\", rendererWorkspace)", StringComparison.Ordinal);
        Assert.IsTrue(snapshot >= 0 && snapshot < begin && begin < upsert && upsert < activate && activate < complete);
        StringAssert.Contains(sync, "snapshot = rendererWorkspace.Workspace;");
        StringAssert.Contains(sync, "sessionGeneration = rendererWorkspace.Order[index].Generation");
    }

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
