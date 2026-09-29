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
        var source = ReadRendererSource("index.ts");
        var hostSource = ReadTerminalSource("Presentation", "TerminalView.xaml.cs");
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
        StringAssert.Contains(source, "payload.sessionGeneration === entry.sessionGeneration");
        StringAssert.Contains(source, "payload.sessionGeneration < entry.sessionGeneration");
        StringAssert.Contains(hostSource, "new { sessionGeneration = generation, data = output }");
        StringAssert.Contains(script, "sessionGeneration===r.sessionGeneration");
    }

    [TestMethod]
    public void BundledRendererKeepsTabActionsSeparatedAndReservesTerminalBottomPadding()
    {
        var html = ReadRendererAsset("index.html");
        var styles = ReadRendererAsset("app.css");

        Assert.IsTrue(html.IndexOf("id=\"session-tabs\"", StringComparison.Ordinal) <
                      html.IndexOf("id=\"new-tab\"", StringComparison.Ordinal));
        StringAssert.Contains(styles, ".tab-list{display:flex;flex:0 1 auto;min-width:0;overflow-x:auto");
        StringAssert.Contains(styles, ".tab-item[data-selected=true]");
        StringAssert.Contains(styles, ".terminal-mount{background-color:var(--color-canvas)");
        StringAssert.Contains(styles, ".xterm{display:block;width:100%;min-width:0;height:100%;padding:var(--space-xs) var(--space-sm) calc(var(--space-xs) + 6px)}");
    }

    [TestMethod]
    public void BundledRendererKeepsTabControlsOutsideScrollableListAndSeparatesProfileMenu()
    {
        var html = ReadRendererAsset("index.html");
        var source = ReadRendererSource("index.ts");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");
        var tabListIndex = html.IndexOf("id=\"session-tabs\"", StringComparison.Ordinal);
        var newTabIndex = html.IndexOf("id=\"new-tab\"", StringComparison.Ordinal);
        var savedTabsIndex = html.IndexOf("id=\"saved-tabs\"", StringComparison.Ordinal);

        Assert.IsTrue(tabListIndex >= 0);
        Assert.IsTrue(newTabIndex > tabListIndex);
        Assert.IsTrue(savedTabsIndex > newTabIndex);
        StringAssert.Contains(html, "class=\"tab-strip-spacer\"");
        StringAssert.Contains(html, "id=\"new-tab\"");
        StringAssert.Contains(html, "aria-haspopup=\"menu\"");
        StringAssert.Contains(styles, ".tab-list{display:flex;flex:0 1 auto;min-width:0;overflow-x:auto;overflow-y:hidden");
        StringAssert.Contains(styles, ".tab-strip-spacer{position:relative;flex:1 1 auto;min-width:0;height:31px}");
        StringAssert.Contains(styles, ".new-tab{width:32px;height:31px;flex:0 0 32px");
        StringAssert.Contains(styles, ".saved-tabs{width:32px;height:31px;flex:0 0 32px");
        StringAssert.Contains(source, "newTabButton.addEventListener(\"click\", () => {");
        StringAssert.Contains(source, "showLaunchProfilesMenu();");
        StringAssert.Contains(source, "dismissLaunchProfilesMenu();");
        StringAssert.Contains(source, "launchDefaultProfile();");
        StringAssert.Contains(source, "newTabButton.disabled === false");
        StringAssert.Contains(source, "savedTabsButton.addEventListener(\"click\", () => {");
        StringAssert.Contains(script, "new-tab");
    }

    [TestMethod]
    public void BundledRendererProvidesWideNeutralPanelResizeAffordanceWithoutInterceptingTabControls()
    {
        var html = ReadRendererAsset("index.html");
        var source = ReadRendererSource("index.ts");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(html, "id=\"panel-resize-zone\"");
        Assert.IsTrue(html.IndexOf("id=\"new-tab\"", StringComparison.Ordinal) <
                      html.IndexOf("id=\"panel-resize-zone\"", StringComparison.Ordinal));
        Assert.IsTrue(html.IndexOf("id=\"panel-resize-zone\"", StringComparison.Ordinal) <
                      html.IndexOf("id=\"saved-tabs\"", StringComparison.Ordinal));
        StringAssert.Contains(source, "panelResizeZone.getBoundingClientRect().width >= 48");
        StringAssert.Contains(source, "panelResizeZone.addEventListener(\"pointerdown\", beginPanelResize)");
        StringAssert.Contains(source, "postGlobal(\"begin-panel-resize\")");
        StringAssert.Contains(script, "begin-panel-resize");
        StringAssert.Contains(styles, ".tab-strip-spacer[data-resize-enabled=true]{cursor:ns-resize}");
        StringAssert.Contains(styles, "width:24px;height:2px");
        Assert.IsFalse(styles.Contains("yellow", StringComparison.OrdinalIgnoreCase));
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
        StringAssert.Contains(styles, ".tab-list{display:flex;flex:0 1 auto;min-width:0;overflow-x:auto");
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
    public void BundledRendererSavesCurrentTabFromItsKeyboardAccessibleContextMenu()
    {
        var source = ReadRendererSource("index.ts");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(source, "showSavedTabsDialog({");
        StringAssert.Contains(source, "shellKind: entry.shellKind");
        StringAssert.Contains(source, "if (savedTabsDialogFocusReturnSessionId !== undefined) {");
        StringAssert.Contains(source, "const returnTab = returnSessionId === undefined ? undefined : sessions.get(returnSessionId)?.tabButton;");
        StringAssert.Contains(script, "key===\"F10\"");
        StringAssert.Contains(script, "\\uC800\\uC7A5\\uD55C \\uD0ED\\uC5D0 \\uCD94\\uAC00\\u2026");
        StringAssert.Contains(script, "tab-context-menu-limit");
        StringAssert.Contains(script, "aria-describedby");
        StringAssert.Contains(script, "saved-tab-guidance");
        StringAssert.Contains(styles, ".tab-context-menu-limit");
        StringAssert.Contains(styles, ".saved-tab-guidance");
    }

    [TestMethod]
    public void BundledRendererProvidesProfileLaunchRetryAndTabDuplicationWithoutGrowingTheTabStrip()
    {
        var source = ReadRendererSource("index.ts");
        var script = ReadRendererAsset("app.js");
        var styles = ReadRendererAsset("app.css");

        StringAssert.Contains(source, "launch-profiles-result");
        StringAssert.Contains(source, "retry-launch-profiles");
        StringAssert.Contains(source, "postSession(\"new-tab\"");
        StringAssert.Contains(source, "postSession(\"duplicate-tab\"");
        StringAssert.Contains(source, "pendingProfileLaunches");
        StringAssert.Contains(source, "pendingTabDuplicates");
        StringAssert.Contains(source, "launch-profile-tab-limit");
        StringAssert.Contains(source, "aria-describedby");
        StringAssert.Contains(source, "if (launchProfilesMenu !== undefined) {");
        StringAssert.Contains(source, "showLaunchProfilesMenu();");
        StringAssert.Contains(source, "newTabButton.focus();");
        StringAssert.Contains(script, "launch-profiles-result");
        StringAssert.Contains(script, "duplicate-tab");
        StringAssert.Contains(script, "\\uC774 \\uD0ED \\uAD6C\\uC131 \\uBCF5\\uC81C");
        StringAssert.Contains(styles, ".saved-tabs-section-heading");
        StringAssert.Contains(styles, ".tab-strip{display:flex;min-width:0;height:32px");
    }

    [TestMethod]
    public void BundledRendererKeepsSavedTabsMenuFreeOfLaunchProfilesAndFillsTerminalWidth()
    {
        var source = ReadRendererSource("index.ts");
        var styles = ReadRendererAsset("app.css");
        var savedMenuStart = source.IndexOf("function showSavedTabsMenu()", StringComparison.Ordinal);
        var savedMenuEnd = source.IndexOf("function appendSavedTabEditor", StringComparison.Ordinal);

        Assert.IsTrue(savedMenuStart >= 0);
        Assert.IsTrue(savedMenuEnd > savedMenuStart);
        var savedMenuSource = source[savedMenuStart..savedMenuEnd];
        Assert.IsFalse(savedMenuSource.Contains("appendLaunchProfilesMenu", StringComparison.Ordinal));
        Assert.IsFalse(savedMenuSource.Contains("retry-launch-profiles", StringComparison.Ordinal));
        StringAssert.Contains(styles, ".launch-profiles-menu,.saved-tabs-menu");
        StringAssert.Contains(styles, ".xterm{display:block;width:100%;min-width:0;height:100%");
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
    public void RendererRoutesEachPasteGestureThroughOneHostClipboardRequest()
    {
        var source = ReadRendererSource("index.ts");
        var normalizedSource = source.ReplaceLineEndings("\n");
        const string pasteRequest = "postSession(\"paste-request\", sessionId);";

        StringAssert.Contains(
            normalizedSource,
            "function requestClipboardPaste(event: Event, sessionId: string): void {");
        StringAssert.Contains(normalizedSource, "event.preventDefault();");
        StringAssert.Contains(normalizedSource, "event.stopImmediatePropagation();");
        StringAssert.Contains(normalizedSource, "requestClipboardPaste(event, sessionId);");
        StringAssert.Contains(normalizedSource, "mount.addEventListener(\n    \"paste\",");
        Assert.AreEqual(1, normalizedSource.Split(pasteRequest, StringSplitOptions.None).Length - 1);
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
