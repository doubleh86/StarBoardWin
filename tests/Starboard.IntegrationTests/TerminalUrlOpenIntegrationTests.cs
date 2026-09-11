using System.IO;

namespace Starboard.IntegrationTests;

[TestClass]
public sealed class TerminalUrlOpenIntegrationTests
{
    [TestMethod]
    public void RendererAndHostKeepUrlOpenBehindValidatedConfirmationBoundary()
    {
        var solutionRoot = FindSolutionRoot();
        var rendererSource = File.ReadAllText(Path.Combine(solutionRoot, "src", "Modules",
                                                           "Starboard.Modules.Terminal", "Presentation",
                                                           "Renderer", "src", "index.ts"));
        var hostSource = File.ReadAllText(Path.Combine(solutionRoot, "src", "Modules",
                                                       "Starboard.Modules.Terminal", "Presentation",
                                                       "TerminalView.xaml.cs"));
        var serviceSource = File.ReadAllText(Path.Combine(solutionRoot, "src", "Modules",
                                                          "Starboard.Modules.Terminal", "Application",
                                                          "TerminalUrlOpenService.cs"));

        StringAssert.Contains(rendererSource, "postSession(\"open-url-request\"");
        StringAssert.Contains(rendererSource, "preview.textContent = request.targetUrl;");
        StringAssert.Contains(hostSource, "PresentUrlOpenConfirmation");
        StringAssert.Contains(hostSource, "response.Result != TerminalConfirmationResult.Confirmed");
        StringAssert.Contains(hostSource, "urlOpenService.TryOpenAsync(pending.UrlTarget");
        StringAssert.Contains(serviceSource, "UseShellExecute = true");
        Assert.IsFalse(hostSource.Contains("pending.UrlTarget.AbsoluteUri", StringComparison.Ordinal));
        Assert.IsFalse(hostSource.Contains("message.UrlOpenTarget.AbsoluteUri", StringComparison.Ordinal));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Starboard.Windows.sln")) == true)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not find the solution root.");
        return string.Empty;
    }
}
