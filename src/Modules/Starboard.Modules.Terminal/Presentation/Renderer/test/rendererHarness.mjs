import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
import { build } from "esbuild";

// Execute the production message listener, DOM reconciliation and activation code.
// Browser layout and the terminal surface are simulated; paste uses xterm's actual clipboard code.
const source = await readFile(new URL("../src/index.ts", import.meta.url), "utf8");
const bundle = await build({
  stdin: {
    contents: `${source}\nexport { sessions, workspaceSync, requestSelection, parseHostMessage,
      startRename, showSavedTabsDialog, showSearch, closeSearch };
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

const clipboardSource = await readFile(new URL("../node_modules/@xterm/xterm/src/browser/Clipboard.ts", import.meta.url), "utf8");
const clipboardBundle = await build({
  stdin: { contents: clipboardSource, loader: "ts" },
  bundle: true,
  write: false,
  format: "iife",
  globalName: "xtermClipboard",
});
const { paste: pasteWithXterm } = vm.runInNewContext(`${clipboardBundle.outputFiles[0].text}\nxtermClipboard;`);

export function createHarness() {
  const frames = new Map();
  let nextFrame = 1;
  const captures = new Map();
  let now = 1000;
  const posted = [];
  let listener;
  function eventTarget(target) {
    target.listeners = new Map();
    target.addEventListener = (type, callback, capture = false) => {
      const listeners = target.listeners.get(type) ?? [];
      listeners.push({ callback, capture: capture === true });
      target.listeners.set(type, listeners);
      target.events?.set(type, callback);
    };
    target.removeEventListener = (type, callback, capture = false) => {
      target.listeners.set(type, (target.listeners.get(type) ?? []).filter((item) =>
        item.callback !== callback || item.capture !== (capture === true)));
    };
    return target;
  }
  const document = eventTarget({ activeElement: null, hidden: false, hasFocus: () => true });
  const window = eventTarget({});

  class Element {
    children = [];
    parentElement = null;
    dataset = {};
    attributes = new Map();
    classList = { toggle() {}, contains: (name) => (this.className ?? "").split(" ").includes(name) };
    style = { setProperty() {}, getPropertyValue: () => "14" };
    clientWidth = 800;
    clientHeight = 200;
    focusCount = 0;
    scrollCount = 0;
    events = new Map();
    scrollLeft = 0;
    scrollWidth = 800;

    constructor(tag = "div") { this.tagName = tag.toUpperCase(); eventTarget(this); }

    querySelector(selector) {
      for (const child of this.children) {
        if ((selector.startsWith(".") && child.classList.contains(selector.slice(1))) ||
          (selector === "dialog[open]" && child.tagName === "DIALOG" && child.open === true) ||
          selector.toUpperCase() === child.tagName) { return child; }
        const found = child.querySelector(selector);
        if (found !== null) { return found; }
      }
      return null;
    }
    replaceChildren(...nodes) { for (const child of [...this.children]) { child.remove(); } this.append(...nodes); }

    get isConnected() { return this === document.body || this.parentElement?.isConnected === true; }
    contains(node) { return node === this || this.children.some((child) => child.contains(node)); }
    setPointerCapture(id) { captures.set(id, this); }
    hasPointerCapture(id) { return captures.get(id) === this; }
    releasePointerCapture(id) { captures.delete(id); dispatch("lostpointercapture", this, { pointerId: id }); }
    getBoundingClientRect() {
      if (this.rect !== undefined) { return this.rect; }
      if (this.className === "tab-drag-preview") {
        const width = Math.min(220, Number.parseFloat(this.style.maxWidth));
        const height = Math.min(25, Number.parseFloat(this.style.maxHeight));
        return { left: 0, right: width, top: 0, bottom: height, width, height };
      }
      const index = this.parentElement?.children.indexOf(this) ?? 0;
      const left = this.parentElement === nodes.get("#session-tabs")
        ? index * 100 - this.parentElement.scrollLeft : 0;
      const width = this.parentElement === nodes.get("#session-tabs") ? 100 : this.clientWidth;
      return { left, right: left + width, top: 0, bottom: 31, width, height: 31 };
    }
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
    showModal() { this.open = true; }
    close() { this.open = false; dispatch("close", this); }
    select() { this.selected = true; }
  }

  document.body = new Element();
  document.documentElement = new Element();
  document.documentElement.clientHeight = 600;
  document.createElement = (tag) => new Element(tag);
  const nodes = new Map(["#session-tabs", "#new-tab", "#panel-resize-zone", "#saved-tabs", "#terminal-workspace"]
    .map((id) => [id, new Element()]));
  for (const node of nodes.values()) { document.body.append(node); }
  document.querySelector = (selector) => nodes.get(selector) ?? document.body.querySelector(selector);
  document.activeElement = document.body;

  function dispatch(type, target = document, properties = {}) {
    const event = { type, target, button: 0, buttons: 1, pointerId: 1, pointerType: "mouse",
      isPrimary: true, clientX: 20, clientY: 10, detail: 1, defaultPrevented: false,
      ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, repeat: false, isComposing: false,
      preventDefault() { this.defaultPrevented = true; },
      stopPropagation() { this.stopped = true; },
      stopImmediatePropagation() { this.stopped = true; this.immediate = true; }, ...properties };
    if (["pointermove", "pointerup", "pointercancel"].includes(type) && captures.has(event.pointerId)) {
      target = captures.get(event.pointerId);
      event.target = target;
    }
    const path = [];
    for (let node = target; node != null && node !== document && node !== window; node = node.parentElement) {
      path.push(node);
    }
    if (target !== window) { path.push(document); }
    path.push(window);
    function invoke(node, capture) {
      for (const item of [...(node.listeners.get(type) ?? [])]) {
        if (item.capture === capture && event.immediate !== true) { item.callback(event); }
      }
    }
    for (const node of [...path].reverse()) { if (event.stopped !== true) { invoke(node, true); } }
    for (const node of path) { if (event.stopped !== true) { invoke(node, false); } }
    if (type === "pointerdown" && event.defaultPrevented === false && target !== document && target !== window) {
      target.focus();
    }
    return event;
  }

  class Terminal {
    options = {};
    cols = 80;
    rows = 24;
    writes = [];
    focusCount = 0;
    disposeCount = 0;
    resetCount = 0;
    bracketedPasteMode = false;
    loadAddon(addon) { addon.terminal = this; }
    selection = "";
    hasSelection() { return this.selection.length > 0; }
    getSelection() { return this.selection; }
    open(mount) {
      this.textarea = new Element("textarea");
      this.textarea.className = "xterm-helper-textarea";
      mount.append(this.textarea);
      this.textarea.addEventListener("keydown", (event) => {
        const allowed = this.keyHandler(event);
        if (allowed === true && event.ctrlKey === true && event.code === "KeyC" &&
          event.altKey === false && event.metaKey === false && event.defaultPrevented === false) {
          this.onInput("\u0003");
        }
      });
      this.textarea.addEventListener("paste", () => this.onInput("unsafe-native-paste"));
    }
    onData(callback) { this.onInput = callback; }
    attachCustomKeyEventHandler(callback) { this.keyHandler = callback; }
    focus() { this.focusCount++; this.textarea.focus(); }
    write(data) { this.writes.push(data); }
    paste(data) {
      pasteWithXterm(data, this.textarea, {
        decPrivateModes: { bracketedPasteMode: this.bracketedPasteMode },
        triggerDataEvent: (value) => this.onInput(value),
      }, { rawOptions: { ignoreBracketedPasteMode: false } });
    }
    reset() { this.resetCount++; }
    dispose() { this.disposeCount++; }
  }
  class FitAddon { fitCount = 0; fit() { this.fitCount++; } }
  class SearchAddon { onDidChangeResults() {} dispose() {} clearDecorations() {} }
  const context = vm.createContext({
    document,
    window: Object.assign(window, {
      matchMedia: () => ({ matches: false, addEventListener() {} }),
      chrome: { webview: {
        addEventListener: (_type, callback) => { listener = callback; },
        postMessage: (message) => posted.push(message),
      } },
    }),
    crypto: { randomUUID: () => "10000000-0000-0000-0000-000000000001" },
    HTMLElement: Element,
    ResizeObserver: class { observe() {} },
    Date: class extends Date { static now() { return now; } },
    requestAnimationFrame: (callback) => { const id = nextFrame++; frames.set(id, callback); return id; },
    cancelAnimationFrame: (id) => frames.delete(id),
    getComputedStyle: () => document.documentElement.style,
    TestTerminal: Terminal,
    TestFitAddon: FitAddon,
    TestSearchAddon: SearchAddon,
    TestWebLinksAddon: class {},
  });
  vm.runInContext(bundle.outputFiles[0].text, context);

  return {
    api: context.rendererTest,
    document, window, nodes, posted, dispatch, captures,
    advanceTime: (milliseconds) => { now += milliseconds; },
    deliver: (message) => listener({ data: message }),
    frameCount: () => frames.size,
    stepFrame: () => { const entry = frames.entries().next().value;
      if (entry !== undefined) { frames.delete(entry[0]); entry[1](); } },
    flushFrames: () => { let count = 0; while (frames.size > 0) {
      if (++count > 1000) { throw new Error("Animation did not settle"); }
      const [id, callback] = frames.entries().next().value; frames.delete(id); callback();
    } },
    startOrder: () => {
      const order = context.rendererTest.workspaceSync.getDragStartOrder();
      return order === undefined ? undefined : JSON.parse(JSON.stringify(order));
    },
  };
}
