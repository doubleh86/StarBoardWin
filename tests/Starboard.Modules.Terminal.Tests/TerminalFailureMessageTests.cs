using Microsoft.Web.WebView2.Core;
using Starboard.Modules.Terminal.Presentation;

namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class TerminalFailureMessageTests
{
    [TestMethod]
    public void MissingWebViewRuntimeProvidesOfflineInstallerGuidanceWithoutRawFailureDetail()
    {
        var exception = new WebView2RuntimeNotFoundException("sensitive-local-detail");

        var message = TerminalView.ToUserMessage(exception);

        StringAssert.Contains(message, "Evergreen Standalone Installer");
        StringAssert.Contains(message, "README");
        Assert.IsFalse(message.Contains(exception.Message, StringComparison.Ordinal));
        Assert.IsTrue(TerminalView.IsRecoverableStartupException(exception));
    }

    [TestMethod]
    public void GenericStartupFailureDoesNotExposeExceptionOrTerminalContent()
    {
        var exception = new InvalidOperationException("command-and-output-must-not-escape");

        var message = TerminalView.ToUserMessage(exception);

        Assert.IsFalse(message.Contains(exception.Message, StringComparison.Ordinal));
        StringAssert.Contains(message, "로컬 로그");
    }
}
