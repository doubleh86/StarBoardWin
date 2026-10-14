import test from "node:test";
import assert from "node:assert/strict";
import { createHarness } from "./rendererHarness.mjs";

const id = "10000000000000000000000000000001";
const other = "10000000000000000000000000000002";
function setup() {
  const h = createHarness();
  for (const [order, sessionId] of [id, other].entries()) {
    h.deliver({ version: 2, type: "session-upsert", sessionId,
      payload: { name: "tab", state: "running", exitCode: null, order, canAddSession: true,
        sessionGeneration: 7, startingDirectory: "", homeDirectory: "", shellKind: "pwsh" } });
  }
  h.deliver({ version: 2, type: "activate-session", sessionId: id, payload: {} });
  h.flushFrames();
  const entry = h.api.sessions.get(id);
  entry.terminal.focus();
  h.posted.length = 0;
  return { h, entry, key: (code, props = {}) => h.dispatch("keydown", entry.terminal.textarea,
    { code, key: code === "Escape" ? "Escape" : code === "Enter" ? "Enter" : code.slice(3), ctrlKey: true, ...props }) };
}

for (const shiftKey of [false, true]) {
  test(`production Ctrl${shiftKey ? "+Shift" : ""}+C copies selection once and cancels browser default`, () => {
    const { h, entry, key } = setup();
    entry.terminal.selection = "synthetic-selection";
    const event = key("KeyC", { shiftKey });
    assert.equal(event.defaultPrevented, true);
    assert.equal(h.posted.filter(m => m.type === "copy").length, 1);
    assert.equal(h.posted.some(m => m.type === "input"), false);
    key("KeyC", { shiftKey, repeat: true });
    assert.equal(h.posted.filter(m => m.type === "copy").length, 1);
    const diagnostic = h.posted.find(m => m.type === "input-diagnostic");
    assert.deepEqual(Object.keys(diagnostic.payload).sort(), ["capture", "focus", "modal", "route", "selection", "suppressed"]);
    assert.equal(JSON.stringify(diagnostic).includes("synthetic-selection"), false);
  });
  test(`production Ctrl${shiftKey ? "+Shift" : ""}+V and native paste use the host only`, () => {
    const { h, entry, key } = setup();
    const event = key("KeyV", { shiftKey });
    assert.equal(event.defaultPrevented, true);
    key("KeyV", { shiftKey, repeat: true });
    assert.equal(h.posted.filter(m => m.type === "paste-request").length, 1);
    // A canceled keyboard default does not generate a native browser paste.
    h.dispatch("paste", entry.terminal.textarea);
    assert.equal(h.posted.filter(m => m.type === "paste-request").length, 2);
    assert.equal(h.posted.some(m => m.type === "input"), false);
    h.deliver({ version: 2, type: "paste", sessionId: id, payload: { data: "single", sessionGeneration: 7 } });
    assert.deepEqual(h.posted.filter(m => m.type === "input").map(m => m.payload.data), ["single"]);
    assert.equal(h.posted.filter(m => m.type === "input-diagnostic" && m.payload.route === 5).length, 1);
  });
}

test("selection-free Ctrl+C keeps interrupt and AltGr/IME never request clipboard", () => {
  const { h, entry, key } = setup();
  assert.equal(key("KeyC").defaultPrevented, false);
  assert.deepEqual(h.posted.filter(m => m.type === "input").map(m => m.payload.data), ["\u0003"]);
  entry.terminal.selection = "selected";
  key("KeyC", { altKey: true });
  key("KeyV", { altKey: true });
  key("KeyC", { isComposing: true });
  key("KeyV", { isComposing: true });
  assert.equal(h.posted.some(m => m.type === "copy" || m.type === "paste-request"), false);
});

function confirmation(h) {
  h.deliver({ version: 2, type: "confirmation-request", sessionId: id,
    payload: { requestId: other, sessionGeneration: 7, kind: "paste", sessionName: "tab", clipboardText: "a\r\nb" } });
  return h.document.body.children.find(e => e.className === "confirmation-dialog");
}

test("modal editing keys retain defaults and delayed paste/input cannot reach shell", () => {
  const { h, entry, key } = setup();
  const dialog = confirmation(h);
  const input = h.document.createElement("textarea");
  dialog.append(input);
  input.focus();
  for (const code of ["KeyC", "KeyV", "KeyA", "KeyF", "Tab"]) {
    assert.equal(h.dispatch("keydown", input, { code, ctrlKey: true }).defaultPrevented, false);
  }
  key("KeyV");
  entry.terminal.onInput("must-be-blocked");
  h.deliver({ version: 2, type: "paste", sessionId: id, payload: { data: "stale", sessionGeneration: 7 } });
  assert.equal(h.posted.some(m => m.type === "input" || m.type === "paste-request"), false);
});

for (const reset of ["fresh-press", "keyup", "blur", "hidden"]) {
  test(`confirmation Escape suppression recovers after ${reset}`, () => {
    const { h, entry, key } = setup();
    const dialog = confirmation(h);
    h.dispatch("keydown", dialog, { key: "Escape", code: "Escape" });
    assert.equal(h.posted.filter(m => m.type === "confirmation-response").length, 1);
    assert.equal(h.posted.find(m => m.type === "confirmation-response").payload.result, "cancelled");
    assert.equal(h.dispatch("keypress", entry.terminal.textarea, { code: "Escape" }).defaultPrevented, true);
    assert.equal(key("Escape", { ctrlKey: false, repeat: true }).defaultPrevented, true);
    if (reset === "keyup") { h.dispatch("keyup", h.document, { code: "Escape" }); }
    if (reset === "blur") { h.dispatch("blur", h.window); }
    if (reset === "hidden") { h.document.hidden = true; h.dispatch("visibilitychange", h.document); }
    assert.equal(key("Escape", { ctrlKey: false }).defaultPrevented, false);
    entry.terminal.selection = "selected";
    key("KeyC");
    key("KeyV");
    assert.equal(h.posted.filter(m => m.type === "copy").length, 1);
    assert.equal(h.posted.filter(m => m.type === "paste-request").length, 1);
  });
}

test("editor focus protects rename/saved/search editing and rejects late or stale-generation paste", () => {
  const { h, entry, key } = setup();
  for (const role of ["rename", "saved", "search"]) {
    if (role === "rename") { h.api.startRename(id); }
    if (role === "saved") { h.api.showSavedTabsDialog({ name: "fixture", startingDirectory: "", shellKind: "pwsh" }); }
    if (role === "search") { h.api.showSearch(id); }
    const dialog = h.document.body.querySelector("dialog[open]");
    const editor = role === "search" ? entry.pane.querySelector("input") : dialog.querySelector("input");
    assert.ok(editor);
    editor.focus();
    for (const code of ["KeyC", "KeyV", "KeyF", "Tab"]) {
      assert.equal(h.dispatch("keydown", editor, { code, ctrlKey: true }).defaultPrevented, false);
    }
    key("KeyV");
    entry.terminal.onInput("blocked");
    if (role === "search") { h.api.closeSearch(true); } else { dialog.close(); }
  }
  entry.terminal.focus();
  h.deliver({ version: 2, type: "paste", sessionId: id, payload: { data: "stale", sessionGeneration: 6 } });
  h.deliver({ version: 2, type: "activate-session", sessionId: other, payload: {} });
  h.deliver({ version: 2, type: "paste", sessionId: id, payload: { data: "late", sessionGeneration: 7 } });
  assert.equal(h.posted.some(m => m.type === "input" || m.type === "paste-request"), false);
});

for (const role of ["rename", "saved", "search"]) {
  for (const reset of ["fresh-press", "keyup", "blur"]) {
    test(`${role} Escape cannot leak held keys after focus returns and recovers on ${reset}`, () => {
      const { h, entry, key } = setup();
      if (role === "rename") { h.api.startRename(id); }
      if (role === "saved") { h.api.showSavedTabsDialog({ name: "fixture", startingDirectory: "", shellKind: "pwsh" }); }
      if (role === "search") { h.api.showSearch(id); }
      const dialog = h.document.body.querySelector("dialog[open]");
      const editor = role === "search" ? entry.pane.querySelector("input") : dialog.querySelector("input");
      editor.focus();
      if (role === "saved") { h.dispatch("cancel", dialog); }
      else { h.dispatch("keydown", editor, { code: "Escape", key: "Escape" }); }
      entry.terminal.focus();
      assert.equal(key("Escape", { ctrlKey: false, repeat: true }).defaultPrevented, true);
      assert.equal(h.dispatch("keypress", entry.terminal.textarea, { code: "Escape" }).defaultPrevented, true);
      if (reset === "keyup") { h.dispatch("keyup", h.document, { code: "Escape" }); }
      if (reset === "blur") { h.dispatch("blur", h.window); }
      assert.equal(key("Escape", { ctrlKey: false }).defaultPrevented, false);
      entry.terminal.selection = "selected";
      key("KeyC");
      key("KeyV");
      assert.equal(h.posted.filter(m => m.type === "copy").length, 1);
      assert.equal(h.posted.filter(m => m.type === "paste-request").length, 1);
      assert.equal(h.posted.some(m => m.type === "input"), false);
    });
  }
}

test("IME application shortcuts and search Escape preserve composition and editor focus", () => {
  const { h, entry, key } = setup();
  for (const code of ["KeyF", "KeyT", "KeyW", "Tab"]) {
    assert.equal(key(code, { shiftKey: true, isComposing: true }).defaultPrevented, false);
  }
  assert.equal(h.posted.some(m => ["new-tab", "close-session", "select-next", "select-previous"].includes(m.type)), false);
  h.api.showSearch(id);
  const input = entry.pane.querySelector("input");
  const event = h.dispatch("keydown", input, { key: "Escape", code: "Escape", isComposing: true });
  assert.equal(event.defaultPrevented, false);
  assert.equal(h.document.activeElement, input);
  assert.equal(input.isConnected, true);
});

for (const exit of ["Enter", "native-cancel"]) {
  for (const reset of ["fresh-press", "keyup", "blur"]) {
    test(`rename ${exit} consumes the closing key and recovers on ${reset}`, () => {
      const { h, entry, key } = setup();
      h.api.startRename(id);
      const dialog = h.document.body.querySelector("dialog[open]");
      const code = exit === "Enter" ? "Enter" : "Escape";
      if (exit === "Enter") {
        const event = h.dispatch("keydown", dialog.querySelector("input"), { code, key: code });
        assert.equal(event.defaultPrevented, true);
      } else {
        dialog.querySelector("button").focus();
        assert.equal(h.dispatch("cancel", dialog).defaultPrevented, true);
      }
      assert.equal(h.document.body.querySelector("dialog[open]"), null);
      assert.equal(h.posted.filter(m => m.type === "rename-session").length, exit === "Enter" ? 1 : 0);
      entry.terminal.focus();
      assert.equal(key(code, { ctrlKey: false, repeat: true }).defaultPrevented, true);
      assert.equal(h.dispatch("keypress", entry.terminal.textarea, { code }).defaultPrevented, true);
      if (reset === "keyup") { h.dispatch("keyup", h.document, { code }); }
      if (reset === "blur") { h.dispatch("blur", h.window); }
      assert.equal(key(code, { ctrlKey: false }).defaultPrevented, false);
      entry.terminal.selection = "selected";
      key("KeyC");
      key("KeyV");
      assert.equal(h.posted.filter(m => m.type === "copy").length, 1);
      assert.equal(h.posted.filter(m => m.type === "paste-request").length, 1);
      assert.equal(h.posted.some(m => m.type === "input"), false);
    });
  }
}
