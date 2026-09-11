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
        StringAssert.Contains(styles, ".tab-list{display:flex;flex:1 1 auto");
        StringAssert.Contains(styles, ".tab-item[data-selected=true]");
        StringAssert.Contains(styles, ".terminal-mount{background-color:var(--color-canvas)");
        StringAssert.Contains(styles, ".xterm{height:100%;padding:var(--space-xs) var(--space-sm) calc(var(--space-xs) + 6px)}");
    }

    [TestMethod]
    public void BundledRendererUsesModalTabNameEditorAndKeepsCloseAreaSeparate()
    {
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(script, "tab-name-dialog");
        StringAssert.Contains(script, "showModal()");
        StringAssert.Contains(script, "compositionstart");
        StringAssert.Contains(script, "isComposing");
        StringAssert.Contains(script, "\\uD0ED \\uC774\\uB984 \\uBCC0\\uACBD");
        StringAssert.Contains(styles, ".tab-list{display:flex;flex:1 1 auto");
        StringAssert.Contains(styles, ".tab-item{display:grid;flex:0 0 auto;grid-template-columns:minmax(0,138px) 28px");
        StringAssert.Contains(styles, ".tab-name-dialog");
        Assert.IsFalse(styles.Contains(".tab-rename-input", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BundledRendererDoesNotDrawTerminalFocusBorder()
    {
        var styles = ReadRendererAsset("app.css");

        Assert.IsFalse(styles.Contains(".xterm.focus::after", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BundledRendererContainsSavedTabMenuAndManagementRuntime()
    {
        var html = ReadRendererAsset("index.html");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(html, "id=\"saved-tabs\"");
        Assert.IsTrue(html.IndexOf("id=\"new-tab\"", StringComparison.Ordinal) <
                      html.IndexOf("id=\"saved-tabs\"", StringComparison.Ordinal));
        StringAssert.Contains(script, "saved-tabs-snapshot");
        StringAssert.Contains(script, "saved-tab-operation-result");
        StringAssert.Contains(script, "saved-tab-launch-result");
        StringAssert.Contains(script, "create-saved-tab");
        StringAssert.Contains(script, "update-saved-tab");
        StringAssert.Contains(script, "delete-saved-tab");
        StringAssert.Contains(script, "launch-saved-tab");
        StringAssert.Contains(script, "cancel-saved-tab-launch");
        StringAssert.Contains(script, "running-tab-limit-reached");
        StringAssert.Contains(script, "starting-directory-unavailable");
        StringAssert.Contains(script, "shell-unavailable");
        StringAssert.Contains(styles, ".saved-tabs-menu");
        StringAssert.Contains(styles, ".saved-tabs-dialog");
        StringAssert.Contains(styles, "max-height:min(360px,calc(100vh - 44px))");
    }

    [TestMethod]
    public void BundledRendererContainsActiveTabOnlyOutputSearchRuntime()
    {
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(script, "findNext");
        StringAssert.Contains(script, "findPrevious");
        StringAssert.Contains(script, "clearDecorations");
        StringAssert.Contains(script, "\\uACB0\\uACFC \\uC5C6\\uC74C");
        StringAssert.Contains(script, "\"KeyF\"");
        StringAssert.Contains(styles, ".terminal-search");
        StringAssert.Contains(styles, "width:min(340px,calc(100% - 16px))");
    }

    [TestMethod]
    public void ActiveTabSearchSourceLifecycleAndNavigationStaysLocalToRenderer()
    {
        var source = ReadRendererSource("index.ts");

        StringAssert.Contains(source, "terminal.loadAddon(searchAddon);");
        StringAssert.Contains(source, "entry?.searchAddon.clearDecorations();");
        StringAssert.Contains(source, "if (searchOverlay?.sessionId !== sessionId) {");
        StringAssert.Contains(source, "if (searchOverlay?.sessionId === sessionId) {");
        StringAssert.Contains(source, "event.stopPropagation();");
        Assert.IsFalse(source.Contains("postSession(\"input\", sessionId, { data: term", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BundledRendererPathDropUsesAdditionalObjectsAndNeverReadsFilesOrSynthesizesEnter()
    {
        var source = ReadRendererSource("index.ts");
        var hostSource = ReadTerminalSource("Presentation", "TerminalView.xaml.cs");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(source, "postMessageWithAdditionalObjects");
        StringAssert.Contains(source, "rendererInstanceId: RendererInstanceId");
        StringAssert.Contains(source, "sessionGeneration: entry.sessionGeneration");
        StringAssert.Contains(source, "event.dataTransfer.files");
        Assert.IsFalse(source.Contains("FileReader", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(".arrayBuffer()", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(".text()", StringComparison.Ordinal));
        StringAssert.Contains(hostSource, "message.RendererInstanceId != rendererInstanceId");
        StringAssert.Contains(hostSource, "rendererInstanceId = Guid.Empty;");
        StringAssert.Contains(hostSource, "additionalObject is not CoreWebView2File file");
        StringAssert.Contains(script, "drop-paths");
        StringAssert.Contains(script, "postMessageWithAdditionalObjects");
        StringAssert.Contains(script, "path-drop-result");
        StringAssert.Contains(styles, ".path-drop-feedback");
        StringAssert.Contains(styles, ".confirmation-dialog[data-kind=path-drop]");
    }

    [TestMethod]
    public void BundledRendererUrlOpenRequiresCtrlClickAndKeepsNavigationInHost()
    {
        var source = ReadRendererSource("index.ts");
        var hostSource = ReadTerminalSource("Presentation", "TerminalView.xaml.cs");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");
        var webLinksLicense = ReadRendererAsset("xterm-addon-web-links-LICENSE.txt");

        StringAssert.Contains(source, "new WebLinksAddon(");
        StringAssert.Contains(source, "event.button !== 0");
        StringAssert.Contains(source, "event.ctrlKey !== true");
        StringAssert.Contains(source, "entry.terminal.hasSelection() === true");
        StringAssert.Contains(source, "activation: \"ctrl-click\"");
        StringAssert.Contains(source, "allowNonHttpProtocols: false");
        StringAssert.Contains(source, "preview.textContent = request.targetUrl;");
        Assert.IsFalse(source.Contains("fetch(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("XMLHttpRequest", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("window.open", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("location.href", StringComparison.Ordinal));
        StringAssert.Contains(hostSource, "message.RendererInstanceId != rendererInstanceId");
        StringAssert.Contains(hostSource, "TerminalUrlOpenConfirmationRequest");
        StringAssert.Contains(hostSource, "ApplyUrlOpenConfirmationAsync");
        StringAssert.Contains(script, "open-url-request");
        StringAssert.Contains(script, "ctrl-click");
        StringAssert.Contains(styles, ".confirmation-dialog[data-kind=url-open]");
        StringAssert.Contains(webLinksLicense, "Permission is hereby granted, free of charge");
    }

    private static string ReadRendererAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Renderer", fileName);
        Assert.IsTrue(File.Exists(path), $"Renderer asset is missing: {path}");

        return File.ReadAllText(path);
    }

    private static string ReadRendererSource(string fileName)
    {
        var solutionRoot = FindSolutionRoot();
        var path = Path.Combine(solutionRoot, "src", "Modules", "Starboard.Modules.Terminal", "Presentation", "Renderer", "src", fileName);
        Assert.IsTrue(File.Exists(path), $"Renderer source is missing: {path}");

        return File.ReadAllText(path);
    }

    private static string ReadTerminalSource(params string[] relativePath)
    {
        var solutionRoot = FindSolutionRoot();
        var path = Path.Combine([solutionRoot, "src", "Modules", "Starboard.Modules.Terminal", .. relativePath]);
        Assert.IsTrue(File.Exists(path), $"Terminal source is missing: {path}");

        return File.ReadAllText(path);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Starboard.Windows.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not find the solution root.");
        return string.Empty;
    }
}
