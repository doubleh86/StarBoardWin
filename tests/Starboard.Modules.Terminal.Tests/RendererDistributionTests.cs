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
        StringAssert.Contains(script, "apply-appearance");
        StringAssert.Contains(script, "terminal.options.fontFamily");
        StringAssert.Contains(script, "terminal.options.fontSize");
        StringAssert.Contains(script, "terminal.options.theme");
        StringAssert.Contains(script, "terminal.write(s.data");
        StringAssert.Contains(script, ".pane.hidden=n===!1");
        StringAssert.Contains(script, "scrollback:1e4");
        StringAssert.Contains(styles, ".tab-list");
        StringAssert.Contains(styles, ".session-pane");
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
