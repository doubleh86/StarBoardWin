import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
import { build } from "esbuild";

// Execute the production message listener, DOM reconciliation and activation code.
// Only browser layout and xterm are simulated; no production functions are replaced.
const source = await readFile(new URL("../src/index.ts", import.meta.url), "utf8");
const bundle = await build({
  stdin: {
    contents: `${source}\nexport { sessions, workspaceSync, requestSelection, parseHostMessage };
      export function getActiveSessionId() { return activeSessionId; }`,
    resolveDir: fileURLToPath(new URL("../src/", import.meta.url)),
    loader: "ts",
  },
  bundle: true,
  write: false,
  format: "iife",
  globalName: "rendererTest",
  loader: { ".css": "empty" },
  plugins: [{
    name: "simulated-xterm",
    setup(builder) {
      builder.onResolve({ filter: /^@xterm\// }, ({ path }) => ({ path, namespace: "simulated" }));
      builder.onLoad({ filter: /./, namespace: "simulated" }, ({ path }) => ({
        contents: path.endsWith(".css") ? "" :
          "export const Terminal = globalThis.TestTerminal; export const FitAddon = globalThis.TestFitAddon;" +
          "export const SearchAddon = globalThis.TestSearchAddon; export const WebLinksAddon = globalThis.TestWebLinksAddon;",
        loader: "js",
      }));
    },
  }],
});

export function createHarness() {
  const frames = [];
  const posted = [];
  let listener;
  const document = { activeElement: null, hasFocus: () => true, addEventListener() {} };

  class Element {
    children = [];
    parentElement = null;
    dataset = {};
    attributes = new Map();
    classList = { toggle() {} };
    style = { setProperty() {}, getPropertyValue: () => "14" };
    clientWidth = 800;
    clientHeight = 200;
    focusCount = 0;
    scrollCount = 0;
    events = new Map();

    get isConnected() { return this === document.body || this.parentElement?.isConnected === true; }
    contains(node) { return node === this || this.children.some((child) => child.contains(node)); }
    addEventListener(type, callback) { this.events.set(type, callback); }
    setAttribute(name, value) { this.attributes.set(name, value); }
    getAttribute(name) { return this.attributes.get(name); }
    append(...nodes) { for (const node of nodes) { this.insertBefore(node, null); } }
    insertBefore(node, before) {
      node.remove();
      const index = before === null ? this.children.length : this.children.indexOf(before);
      this.children.splice(index, 0, node);
      node.parentElement = this;
    }
    remove() {
      if (this.parentElement !== null) {
        // Reproduce the focus loss from moving a focused subtree in Chromium.
        if (this.contains(document.activeElement) === true) { document.activeElement = document.body; }
        this.parentElement.children.splice(this.parentElement.children.indexOf(this), 1);
        this.parentElement = null;
      }
    }
    focus(options) { this.focusCount++; this.lastFocusOptions = options; document.activeElement = this; }
    scrollIntoView() { this.scrollCount++; }
  }

  document.body = new Element();
  document.documentElement = new Element();
  document.createElement = () => new Element();
  const nodes = new Map(["#session-tabs", "#new-tab", "#panel-resize-zone", "#saved-tabs", "#terminal-workspace"]
    .map((id) => [id, new Element()]));
  for (const node of nodes.values()) { document.body.append(node); }
  document.querySelector = (selector) => nodes.get(selector);
  document.activeElement = document.body;

  class Terminal {
    options = {};
    cols = 80;
    rows = 24;
    writes = [];
    focusCount = 0;
    disposeCount = 0;
    resetCount = 0;
    loadAddon(addon) { addon.terminal = this; }
    open(mount) { this.textarea = new Element(); mount.append(this.textarea); }
    onData(callback) { this.onInput = callback; }
    attachCustomKeyEventHandler(callback) { this.keyHandler = callback; }
    focus() { this.focusCount++; this.textarea.focus(); }
    write(data) { this.writes.push(data); }
    paste(data) { this.onInput(data); }
    reset() { this.resetCount++; }
    dispose() { this.disposeCount++; }
  }
  class FitAddon { fitCount = 0; fit() { this.fitCount++; } }
  class SearchAddon { onDidChangeResults() {} dispose() {} clearDecorations() {} }
  const context = vm.createContext({
    document,
    window: {
      matchMedia: () => ({ matches: false, addEventListener() {} }),
      addEventListener() {},
      chrome: { webview: {
        addEventListener: (_type, callback) => { listener = callback; },
        postMessage: (message) => posted.push(message),
      } },
    },
    crypto: { randomUUID: () => "10000000-0000-0000-0000-000000000001" },
    HTMLElement: Element,
    ResizeObserver: class { observe() {} },
    requestAnimationFrame: (callback) => frames.push(callback),
    getComputedStyle: () => document.documentElement.style,
    TestTerminal: Terminal,
    TestFitAddon: FitAddon,
    TestSearchAddon: SearchAddon,
    TestWebLinksAddon: class {},
  });
  vm.runInContext(bundle.outputFiles[0].text, context);

  return {
    api: context.rendererTest,
    document, nodes, posted,
    deliver: (message) => listener({ data: message }),
    flushFrames: () => { while (frames.length > 0) { frames.shift()(); } },
    startOrder: () => {
      const order = context.rendererTest.workspaceSync.getDragStartOrder();
      return order === undefined ? undefined : JSON.parse(JSON.stringify(order));
    },
  };
}
