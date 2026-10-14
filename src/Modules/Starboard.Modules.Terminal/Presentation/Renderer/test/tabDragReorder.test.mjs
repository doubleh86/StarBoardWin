import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createHarness } from "./rendererHarness.mjs";

const fixtures = JSON.parse(await readFile(new URL(
  "../../../../../../tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/cases.json", import.meta.url), "utf8"));
const base = fixtures.states.base;
// Read the shipped definitions so this matrix uses all four product palettes.
const themeSource = await readFile(new URL("../../../../Starboard.Modules.Preferences/Application/ThemeCatalog.cs", import.meta.url), "utf8");
const themes = [...themeSource.matchAll(/Create\("([^"]+)", ([^\n]+)\n\s+\[([^\]]+)\]/g)].map((match) => {
  const colors = [...match[2].matchAll(/"(#[0-9A-F]+)"/g)].map((color) => color[1]);
  return { name: match[1], canvas: colors[0], foreground: colors[1], muted: colors[2], accent: colors[3],
    cursor: colors[4], selection: colors[5], ansiPalette: [...match[3].matchAll(/"(#[0-9A-F]+)"/g)].map((color) => color[1]) };
});
assert.equal(themes.length, 4);
const message = (type, payload, sessionId) => ({ version: 2, type, payload,
  ...(sessionId === undefined ? {} : { sessionId }) });
function upsert(harness, session, order, extra = {}) {
  harness.deliver(message("session-upsert", { name: `탭 ${order}`, state: "running", exitCode: null,
    order, canAddSession: true, sessionGeneration: session.sessionGeneration, startingDirectory: "C:\\Work",
    homeDirectory: "C:\\Users", shellKind: "pwsh", ...extra }, session.sessionId));
}
function sync(harness, state) {
  const payload = { order: state.sessions, activeSessionId: state.activeSessionId };
  harness.deliver(message("workspace-sync-begin", payload));
  for (const id of harness.api.sessions.keys()) {
    if (state.sessions.some((session) => session.sessionId === id) === false) {
      harness.deliver(message("remove-session", {}, id));
    }
  }
  state.sessions.forEach((session, index) => upsert(harness, session, index));
  harness.deliver(message("activate-session", {}, state.activeSessionId));
  harness.deliver(message("workspace-sync-complete", payload));
}
function initialized(state = base) {
  const harness = createHarness();
  sync(harness, state);
  harness.flushFrames();
  harness.nodes.get("#session-tabs").clientHeight = 31;
  harness.posted.length = 0;
  return harness;
}
const reorderRequests = (harness) => harness.posted.filter((item) => item.type === "reorder-session");
const marker = (harness) => harness.document.body.children.find((node) => node.className === "tab-insertion-marker");
const preview = (harness) => harness.document.body.children.find((node) => node.className === "tab-drag-preview");
function down(harness, sessionId = base.sessions[0].sessionId, properties = {}) {
  const entry = harness.api.sessions.get(sessionId);
  const x = entry.tabItem.getBoundingClientRect().left + 20;
  return harness.dispatch("pointerdown", entry.tabButton, { clientX: x, ...properties });
}
function move(harness, x, properties = {}) {
  return harness.dispatch("pointermove", harness.document, { clientX: x, ...properties });
}
function release(harness, x, properties = {}) {
  return harness.dispatch("pointerup", harness.document, { clientX: x, buttons: 0, ...properties });
}
function assertClean(harness) {
  assert.equal(harness.captures.size, 0);
  assert.equal(harness.frameCount(), 0);
  assert.equal(marker(harness)?.hidden ?? true, true);
  assert.equal(preview(harness)?.hidden ?? true, true);
  assert.equal(preview(harness)?.textContent ?? "", "");
  for (const entry of harness.api.sessions.values()) {
    assert.equal(entry.tabItem.dataset.dragSource, undefined);
  }
  assert.equal(harness.nodes.get("#session-tabs").dataset.reordering, undefined);
  for (const type of ["pointermove", "pointerup", "pointercancel"]) {
    assert.equal((harness.document.listeners.get(type) ?? []).length, 0);
  }
}

for (const theme of themes) {
  for (const count of [1, 3, 8]) {
    test(`simulated narrow overflow: ${count} tabs, long Korean names, ${theme.name}`, () => {
      const sessions = Array.from({ length: count }, (_, index) => ({
        sessionId: (index + 1).toString(16).padStart(32, "0"), sessionGeneration: index + 1,
      }));
      const harness = initialized({ sessions, activeSessionId: sessions.at(-1).sessionId });
      harness.deliver(message("apply-appearance", { fontFamily: "Cascadia Mono", fontSize: 14, theme }));
      sessions.forEach((session, index) => upsert(harness, session, index, {
        name: `아주 긴 한글 탭 이름 ${index} — long terminal workspace title`,
      }));
      harness.flushFrames();
      const list = harness.nodes.get("#session-tabs");
      list.clientWidth = 180;
      list.scrollWidth = count * 100;
      const active = harness.api.sessions.get(sessions.at(-1).sessionId);
      active.terminal.focus();
      harness.posted.length = 0;
      down(harness, sessions[0].sessionId);
      move(harness, 175);
      for (let frame = 0; frame < 35 && harness.frameCount() > 0; frame++) { harness.stepFrame(); }
      release(harness, 175);
      assert.equal(reorderRequests(harness).length, count === 1 ? 0 : 1);
      if (count > 1) { assert.ok(list.scrollLeft > 0); }
      assert.ok(list.scrollLeft <= Math.max(0, list.scrollWidth - list.clientWidth));
      assert.equal(harness.document.activeElement, count === 1 ? active.tabButton : active.terminal.textarea);
      assert.deepEqual(harness.startOrder(), sessions);
      for (const session of sessions) {
        const entry = harness.api.sessions.get(session.sessionId);
        assert.ok(entry.tabLabel.textContent.includes("아주 긴 한글"));
        assert.equal(entry.terminal.options.theme.background, theme.canvas);
        assert.equal(entry.terminal.resetCount, 0);
      }
      assertClean(harness);
    });
  }
}

// Shared wire fixtures drive the production event listeners. Host-only malformed messages
// have no corresponding browser gesture and are covered by the contract/runtime suites.
for (const item of fixtures.cases.filter((item) => item.rendererAction !== "not-generated")) {
  test(`shared renderer fixture: ${item.id}`, () => {
    const state = fixtures.states[item.dragState];
    const harness = initialized(state);
    const sourceId = item.message?.sessionId.toLowerCase() ?? state.sessions[0].sessionId;
    const entry = harness.api.sessions.get(sourceId);
    const focused = harness.api.sessions.get(state.activeSessionId).terminal.textarea;
    focused.focus();
    down(harness, sourceId);
    const targetIndex = state.sessions.findIndex((session) =>
      session.sessionId === item.message?.payload.targetSessionId?.toLowerCase());
    const x = targetIndex < 0 ? -10 : targetIndex * 100 + (item.message.payload.position === "before" ? 25 : 75);
    if (item.rendererAction === "click") {
      const startX = entry.tabItem.getBoundingClientRect().left + 20;
      move(harness, startX + 6);
      release(harness, startX + 6);
      assert.equal(marker(harness).hidden, true);
      harness.dispatch("click", entry.tabButton);
      assert.equal(harness.posted.filter((posted) => posted.type === "select-session").length, 1);
    } else {
      // Cross the threshold even for self/missing targets and cancellation fixtures.
      move(harness, entry.tabItem.getBoundingClientRect().left + 35);
      move(harness, x);
      if (item.hostState !== item.dragState) {
        const host = fixtures.states[item.hostState];
        if (host.rendererReady !== true || host.rendererInstanceId !== state.rendererInstanceId) {
          harness.dispatch("pagehide", harness.window);
        } else {
          sync(harness, host);
        }
      }
      const cancel = item.rendererAction;
      if (cancel === "cancel:escape") { harness.dispatch("keydown", harness.document, { key: "Escape" }); }
      if (cancel === "cancel:pointercancel") { harness.dispatch("pointercancel", entry.tabButton); }
      if (cancel === "cancel:lost-capture") { entry.tabButton.releasePointerCapture(1); }
      if (cancel === "cancel:renderer-lifetime") { harness.dispatch("pagehide", harness.window); }
      if (item.id === "background-output-keeps-drag") {
        harness.deliver(message("output", { data: "한글", sessionGeneration: entry.sessionGeneration }, sourceId));
      }
      release(harness, cancel === "cancel:outside-drop" ? -10 : x);
      release(harness, x);
      assert.equal(harness.api.getActiveSessionId(), fixtures.states[item.hostState].activeSessionId);
      if (focused.isConnected === true && state.sessions.length > 1) {
        assert.equal(harness.document.activeElement, focused);
      }
      assert.equal(harness.posted.filter((posted) => posted.type === "select-session").length, 0);
    }
    const requests = reorderRequests(harness);
    assert.equal(requests.length, item.expected.sendCount);
    if (requests.length === 1) {
      const expected = { ...item.message, sessionId: sourceId, payload: {
        rendererInstanceId: state.rendererInstanceId,
        sessionGeneration: entry.sessionGeneration,
        targetSessionId: item.message.payload.targetSessionId.toLowerCase(),
        position: item.message.payload.position,
        startOrder: state.sessions,
      } };
      assert.deepEqual(JSON.parse(JSON.stringify(requests[0])), expected);
    }
    assertClean(harness);
  });
}

test("actual drag displays insertion, preserves tab keyboard focus and suppresses click/doubleclick", () => {
  const harness = initialized();
  const entry = harness.api.sessions.get(base.sessions[0].sessionId);
  const focus = harness.api.sessions.get(base.activeSessionId).tabButton;
  focus.focus();
  down(harness);
  assert.equal(harness.document.activeElement, focus);
  move(harness, 250);
  assert.equal(marker(harness).hidden, false);
  assert.equal(marker(harness).style.left, "298.5px");
  assert.deepEqual(harness.startOrder(), base.sessions);
  release(harness, 250);
  for (const type of ["click", "dblclick"]) {
    const event = harness.dispatch(type, entry.tabButton);
    assert.equal(event.defaultPrevented, true);
  }
  assert.equal(harness.document.activeElement, focus);
  assert.equal(harness.posted.filter((item) => item.type === "select-session").length, 0);
  assert.equal(harness.document.body.children.some((node) => node.className === "tab-name-dialog"), false);
  // Suppression is restricted to the tab list; close/new/menu/resize remain separate controls.
  assert.equal(harness.dispatch("click", harness.nodes.get("#panel-resize-zone")).defaultPrevented, false);
  assert.equal(harness.dispatch("click", entry.closeButton).defaultPrevented, false);
  assert.equal(harness.posted.filter((item) => item.type === "close-session").length, 1);
  assertClean(harness);
});

test("two through eight tabs use their full confirmed order without activating the dragged tab", () => {
  for (let count = 2; count <= 8; count++) {
    const state = { ...fixtures.states.eight, sessions: fixtures.states.eight.sessions.slice(0, count) };
    const harness = initialized(state);
    down(harness, state.sessions[count - 1].sessionId);
    move(harness, 25);
    release(harness, 25);
    const requests = reorderRequests(harness);
    assert.equal(requests.length, 1);
    assert.deepEqual(JSON.parse(JSON.stringify(requests[0].payload.startOrder)), state.sessions);
    assert.equal(harness.api.getActiveSessionId(), state.activeSessionId);
    assertClean(harness);
  }
});

test("ordinary doubleclick is retained below threshold and post-drag suppression expires for subsequent clicks", () => {
  const harness = initialized();
  const entry = harness.api.sessions.get(base.sessions[0].sessionId);
  down(harness);
  move(harness, 26);
  release(harness, 26);
  assert.equal(harness.dispatch("dblclick", entry.tabButton).defaultPrevented, false);
  const dialog = harness.document.body.children.find((node) => node.className === "tab-name-dialog");
  assert.equal(dialog.open, true);
  const input = dialog.children[1].children[0];
  harness.dispatch("compositionstart", input);
  harness.dispatch("keydown", input, { key: "Enter", isComposing: true });
  assert.equal(harness.posted.filter((item) => item.type === "rename-session").length, 0);
  harness.dispatch("compositionend", input);
  harness.dispatch("keydown", input, { key: "Enter", isComposing: false });
  assert.equal(harness.posted.filter((item) => item.type === "rename-session").length, 1);
  assert.equal(dialog.isConnected, false);
  assert.equal(reorderRequests(harness).length, 0);
  const dragged = initialized();
  const button = dragged.api.sessions.get(base.sessions[0].sessionId).tabButton;
  down(dragged);
  move(dragged, 250);
  release(dragged, 250);
  assert.equal(dragged.dispatch("click", button, { detail: 0 }).defaultPrevented, false);
  assert.equal(dragged.dispatch("click", button).defaultPrevented, true);
  dragged.advanceTime(501);
  assert.equal(dragged.dispatch("click", button).defaultPrevented, false);
});

test("threshold is strictly more than six CSS pixels and foreign pointers cannot finish a drag", () => {
  const harness = initialized();
  down(harness);
  move(harness, 26);
  assert.equal(marker(harness).hidden, true);
  assert.equal(harness.nodes.get("#session-tabs").dataset.reordering, undefined);
  move(harness, 27);
  assert.equal(harness.nodes.get("#session-tabs").dataset.reordering, "true");
  release(harness, 250, { pointerId: 2 });
  assert.equal(harness.captures.size, 1);
  release(harness, 250);
  assert.equal(reorderRequests(harness).length, 1);
  assertClean(harness);
});

test("only primary left mouse tab label gestures are candidates; no terminal/close/new/menu/resize interception", () => {
  const harness = initialized();
  const entry = harness.api.sessions.get(base.sessions[0].sessionId);
  for (const properties of [{ button: 1 }, { button: 2 }, { pointerType: "touch" }, { isPrimary: false }]) {
    assert.equal(down(harness, entry.id, properties).defaultPrevented, false);
    assert.equal(harness.captures.size, 0);
  }
  for (const target of [entry.closeButton, entry.mount, harness.nodes.get("#new-tab"),
    harness.nodes.get("#saved-tabs"), harness.nodes.get("#panel-resize-zone")]) {
    harness.dispatch("pointerdown", target);
    assert.equal(harness.captures.size, 0);
  }
  assert.equal(reorderRequests(harness).length, 0);
});

test("renames, output and same-configuration sync keep the drag; a later order or generation change cancels", () => {
  for (const mutation of ["order", "generation", "remove", "add"]) {
    const harness = initialized();
    down(harness);
    move(harness, 250);
    upsert(harness, base.sessions[0], 0, { name: "이름 변경", state: "exited", exitCode: 0 });
    harness.deliver(message("new-output-state", { sessionGeneration: 1, hasNewOutput: true }, base.sessions[2].sessionId));
    sync(harness, base);
    assert.equal(marker(harness).hidden, false);
    if (mutation === "order") { upsert(harness, base.sessions[0], 3); }
    if (mutation === "generation") { upsert(harness, { ...base.sessions[1], sessionGeneration: 9 }, 1); }
    if (mutation === "remove") { harness.deliver(message("remove-session", {}, base.sessions[3].sessionId)); }
    if (mutation === "add") { upsert(harness, fixtures.states.tabAdded.sessions[4], 4); }
    release(harness, 250);
    assert.equal(reorderRequests(harness).length, 0);
    assertClean(harness);
  }
});

test("overflow scrolls only the list at bounded steps, updates insertion and cancels its animation", () => {
  const harness = initialized(fixtures.states.eight);
  const list = harness.nodes.get("#session-tabs");
  list.clientWidth = 220;
  list.scrollWidth = 800;
  down(harness);
  move(harness, 219);
  assert.equal(harness.frameCount(), 1);
  harness.stepFrame();
  assert.equal(list.scrollLeft, 8);
  assert.equal(harness.nodes.get("#terminal-workspace").scrollLeft, 0);
  assert.equal(marker(harness).style.left, "190.5px");
  for (let count = 0; count < 15; count++) { harness.stepFrame(); }
  assert.equal(list.scrollLeft, 128);
  move(harness, 1);
  harness.stepFrame();
  assert.equal(list.scrollLeft, 120);
  move(harness, -1);
  harness.stepFrame();
  assert.equal(harness.frameCount(), 0);
  assert.equal(list.scrollLeft, 120);
  move(harness, 219);
  harness.dispatch("keydown", harness.document, { key: "Escape" });
  assert.equal(reorderRequests(harness).length, 0);
  assertClean(harness);
});

test("blur, hidden document, page lifetime and missing buttons clean candidates and active drags", () => {
  for (const reason of ["blur", "hidden", "pagehide", "buttons"]) {
    const harness = initialized();
    down(harness);
    move(harness, 250);
    if (reason === "hidden") { harness.document.hidden = true; harness.dispatch("visibilitychange"); }
    if (reason === "blur" || reason === "pagehide") { harness.dispatch(reason, harness.window); }
    if (reason === "buttons") { move(harness, 250, { buttons: 0 }); }
    release(harness, 250);
    assert.equal(reorderRequests(harness).length, 0);
    assertClean(harness);
    if (reason === "pagehide") {
      assert.equal(marker(harness), undefined);
      assert.equal(harness.document.listeners.get("click").length, 0);
      down(harness);
      assert.equal(harness.captures.size, 0);
    }
  }
});

test("pending/incomplete workspace cannot begin a candidate and Escape cancellation never reaches shell shortcuts", () => {
  const harness = initialized();
  harness.deliver(message("workspace-sync-begin", { order: base.sessions, activeSessionId: base.activeSessionId }));
  assert.equal(down(harness).defaultPrevented, false);
  assert.equal(harness.captures.size, 0);
  sync(harness, base);
  down(harness);
  move(harness, 250);
  const event = harness.dispatch("keydown", harness.document, { key: "Escape", code: "Escape" });
  assert.equal(event.defaultPrevented, true);
  assert.equal(event.immediate, true);
  assert.equal(harness.posted.filter((item) => item.type === "input").length, 0);
  assertClean(harness);
});

test("visual feedback starts strictly after threshold, uses safe names and preserves source geometry/focus", () => {
  const harness = initialized();
  const entry = harness.api.sessions.get(base.sessions[0].sessionId);
  const name = "<img src=x onerror=alert(1)> 긴 한글 이름 & 작업공간";
  upsert(harness, base.sessions[0], 0, { name });
  const focus = harness.api.sessions.get(base.activeSessionId).terminal.textarea;
  focus.focus();
  const bounds = entry.tabItem.getBoundingClientRect();
  down(harness);
  move(harness, 26);
  assert.equal(entry.tabItem.dataset.dragSource, undefined);
  assert.equal(preview(harness).hidden, true);
  move(harness, 27);
  assert.equal(entry.tabItem.dataset.dragSource, "true");
  assert.equal(marker(harness).hidden, true);
  assert.equal(preview(harness).hidden, true);
  move(harness, 250);
  assert.equal(preview(harness).hidden, false);
  assert.equal(preview(harness).textContent, name);
  assert.equal(preview(harness).children.length, 0);
  assert.equal(preview(harness).getAttribute("aria-hidden"), "true");
  assert.equal(marker(harness).getAttribute("aria-hidden"), "true");
  assert.equal(harness.document.activeElement, focus);
  assert.equal(harness.api.getActiveSessionId(), base.activeSessionId);
  assert.deepEqual(entry.tabItem.getBoundingClientRect(), bounds);
  assert.equal(harness.dispatch("click", entry.closeButton).defaultPrevented, false);
  move(harness, -1);
  assert.equal(preview(harness).hidden, true);
  assert.equal(marker(harness).hidden, true);
  assert.equal(entry.tabItem.dataset.dragSource, "true");
  release(harness, -1);
  assert.equal(reorderRequests(harness).length, 0);
  assertClean(harness);
});

test("preview clamps to resized viewport and follows names without changing order", () => {
  const harness = initialized();
  const viewport = harness.document.documentElement;
  const list = harness.nodes.get("#session-tabs");
  viewport.clientWidth = 180;
  viewport.clientHeight = 60;
  list.clientWidth = 180;
  down(harness);
  move(harness, 175);
  assert.equal(preview(harness).style.maxWidth, "172px");
  assert.equal(preview(harness).style.left, "4px");
  assert.equal(preview(harness).style.top, "31px");
  upsert(harness, base.sessions[0], 0, { name: "새 이름 한글" });
  assert.equal(preview(harness).textContent, "새 이름 한글");
  viewport.clientWidth = 120;
  viewport.clientHeight = 40;
  harness.dispatch("resize", harness.window);
  assert.equal(preview(harness).style.maxWidth, "112px");
  assert.equal(preview(harness).style.left, "4px");
  assert.equal(preview(harness).style.top, "11px");
  assert.deepEqual(harness.startOrder(), base.sessions);
  harness.dispatch("keydown", harness.document, { key: "Escape" });
  assertClean(harness);
});

test("source removal and renderer recreation leave no visual state behind", () => {
  const harness = initialized();
  const source = harness.api.sessions.get(base.sessions[0].sessionId);
  down(harness);
  move(harness, 250);
  harness.deliver(message("remove-session", {}, source.id));
  assert.equal(source.tabItem.dataset.dragSource, undefined);
  assertClean(harness);
  down(harness, base.sessions[1].sessionId);
  move(harness, 350);
  harness.dispatch("pagehide", harness.window);
  assert.equal(preview(harness), undefined);
  assert.equal(marker(harness), undefined);
  assertClean(harness);
  const recovered = initialized();
  assertClean(recovered);
  down(recovered);
  move(recovered, 250);
  release(recovered, 250);
  assert.equal(reorderRequests(recovered).length, 1);
  assertClean(recovered);
});
