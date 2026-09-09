namespace Starboard.Modules.Terminal.Tests;

[TestClass]
public sealed class RendererDistributionTests
{
    [TestMethod]
    public void BundledRendererDocumentUsesOnlyLocalAssetsAndBlocksNetwork()
    {
        var html = ReadRendererAsset("index.html");

        StringAssert.Contains(html, "connect-src 'none'");
        StringAssert.Contains(html, "src=\"./app.js\"");
        StringAssert.Contains(html, "href=\"./app.css\"");
        Assert.IsFalse(html.Contains("http://", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains("https://", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BundledRendererContainsSessionAwareTabRuntime()
    {
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(script, "session-upsert");
        StringAssert.Contains(script, "activate-session");
        StringAssert.Contains(script, "remove-session");
        StringAssert.Contains(script, "select-session");
        StringAssert.Contains(script, "close-session");
        StringAssert.Contains(script, "restart-session");
        StringAssert.Contains(script, "rename-session");
        StringAssert.Contains(script, "move-session");
        StringAssert.Contains(script, "set-starting-directory");
        StringAssert.Contains(script, "homeDirectory");
        StringAssert.Contains(script, "compositionstart");
        StringAssert.Contains(script, "\"F10\"");
        StringAssert.Contains(script, "apply-appearance");
        StringAssert.Contains(script, "terminal.options.fontFamily");
        StringAssert.Contains(script, "terminal.options.fontSize");
        StringAssert.Contains(script, "terminal.options.theme");
        StringAssert.Contains(script, "terminal.write(s.data");
        StringAssert.Contains(script, ".pane.hidden=n===!1");
        StringAssert.Contains(script, "scrollback:1e4");
        StringAssert.Contains(styles, ".tab-list");
        StringAssert.Contains(styles, ".session-pane");
        StringAssert.Contains(styles, ".tab-context-menu");
        StringAssert.Contains(styles, ".starting-directory-dialog");
        StringAssert.Contains(styles, ".session-recovery");
    }

    [TestMethod]
    public void BundledRendererContainsSafetyConfirmationAndUnreadOutputRuntime()
    {
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(script, "confirmation-request");
        StringAssert.Contains(script, "confirmation-cancel");
        StringAssert.Contains(script, "confirmation-response");
        StringAssert.Contains(script, "new-output-state");
        StringAssert.Contains(script, "\\uC0C8 \\uCD9C\\uB825 \\uC788\\uC74C");
        StringAssert.Contains(script, "aria-readonly");
        StringAssert.Contains(script, "\\uC904\\uBC14\\uAFC8\\uC740 \\u21B5\\uB85C \\uD45C\\uC2DC\\uB429\\uB2C8\\uB2E4.");
        StringAssert.Contains(script, ".autofocus=!0");
        StringAssert.Contains(script, ".repeat===!0");
        StringAssert.Contains(script, ".isComposing===!0");
        StringAssert.Contains(styles, ".confirmation-dialog");
        StringAssert.Contains(styles, ".confirmation-dialog[data-kind=paste]");
        StringAssert.Contains(styles, ".confirmation-preview");
        StringAssert.Contains(styles, ".tab-new-output");
    }

    [TestMethod]
    public void BundledRendererKeepsTabActionsSeparatedAndReservesTerminalBottomPadding()
    {
        var html = ReadRendererAsset("index.html");
        var styles = ReadRendererAsset("app.css");

        Assert.IsTrue(html.IndexOf("id=\"session-tabs\"", StringComparison.Ordinal) <
                      html.IndexOf("id=\"new-tab\"", StringComparison.Ordinal));
        StringAssert.Contains(styles, ".tab-list{display:flex;flex:0 1 auto");
        StringAssert.Contains(styles, ".tab-item[data-selected=true]");
        StringAssert.Contains(styles, ".terminal-mount{background-color:var(--color-canvas)");
        StringAssert.Contains(styles, ".xterm{height:100%;padding:var(--space-xs) var(--space-sm) calc(var(--space-xs) + 6px)}");
    }

    [TestMethod]
    public void BundledRendererDoesNotDrawTerminalFocusBorder()
    {
        var styles = ReadRendererAsset("app.css");

        Assert.IsFalse(styles.Contains(".xterm.focus::after", StringComparison.Ordinal));
    }

    private static string ReadRendererAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Renderer", fileName);
        Assert.IsTrue(File.Exists(path), $"Renderer asset is missing: {path}");

        return File.ReadAllText(path);
    }
}
