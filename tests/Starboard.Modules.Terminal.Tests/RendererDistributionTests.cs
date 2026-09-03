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
        StringAssert.Contains(styles, ".tab-list");
        StringAssert.Contains(styles, ".session-pane");
    }

    private static string ReadRendererAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Renderer", fileName);
        Assert.IsTrue(File.Exists(path), $"Renderer asset is missing: {path}");

        return File.ReadAllText(path);
    }
}
