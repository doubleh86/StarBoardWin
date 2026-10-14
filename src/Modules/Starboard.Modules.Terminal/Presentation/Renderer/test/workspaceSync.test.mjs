import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createHarness } from "./rendererHarness.mjs";

const fixtures = JSON.parse(await readFile(new URL(
  "../../../../../../tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/cases.json", import.meta.url), "utf8"));
const base = fixtures.states.base;
const payloadFor = (state) => ({ order: state.sessions, activeSessionId: state.activeSessionId });
const globalMessage = (type, payload) => ({ version: 2, type, payload });
const sessionMessage = (type, sessionId, payload = {}) => ({ version: 2, type, sessionId, payload });
function upsert(harness, session, order, extra = {}) {
  harness.deliver(sessionMessage("session-upsert", session.sessionId, {
    name: `한글 ${order}`, state: "running", exitCode: null, order, canAddSession: true,
    sessionGeneration: session.sessionGeneration, startingDirectory: "C:\\Work", homeDirectory: "C:\\Users",
    shellKind: "pwsh", ...extra,
  }));
}
function sync(harness, state) {
  harness.deliver(globalMessage("workspace-sync-begin", payloadFor(state)));
  for (const id of harness.api.sessions.keys()) {
    if (state.sessions.some((session) => session.sessionId === id) === false) {
      harness.deliver(sessionMessage("remove-session", id));
    }
  }
  state.sessions.forEach((session, index) => upsert(harness, session, index));
  if (state.activeSessionId !== null) {
    harness.deliver(sessionMessage("activate-session", state.activeSessionId));
  }
  harness.deliver(globalMessage("workspace-sync-complete", payloadFor(state)));
  harness.flushFrames();
}
function initialized() { const harness = createHarness(); sync(harness, base); return harness; }
function domOrder(harness) { return harness.nodes.get("#session-tabs").children.map((item) =>
  [...harness.api.sessions.values()].find((entry) => entry.tabItem === item).id); }

test("requires begin, every ID and generation, and completion before exposing a copied start order", () => {
  const harness = createHarness();
  base.sessions.forEach((session, index) => upsert(harness, session, index));
  assert.equal(harness.startOrder(), undefined);
  harness.deliver(globalMessage("workspace-sync-complete", payloadFor(base)));
  assert.equal(harness.startOrder(), undefined);
  sync(harness, base);
  assert.deepEqual(harness.startOrder(), base.sessions);
  const copy = harness.api.workspaceSync.getDragStartOrder();
  copy[0].sessionGeneration = 999;
  copy.reverse();
  assert.deepEqual(harness.startOrder(), base.sessions);
});

test("partial reordered upserts cannot start a drag or publish intermediate DOM order", () => {
  const harness = initialized();
  const state = { ...base, sessions: [...base.sessions].reverse() };
  harness.deliver(globalMessage("workspace-sync-begin", payloadFor(state)));
  assert.equal(harness.startOrder(), undefined);
  state.sessions.forEach((session, index) => {
    upsert(harness, session, index);
    assert.equal(harness.startOrder(), undefined);
    assert.deepEqual(domOrder(harness), base.sessions.map((item) => item.sessionId));
  });
  harness.deliver(sessionMessage("activate-session", state.activeSessionId));
  assert.equal(harness.startOrder(), undefined);
  harness.deliver(globalMessage("workspace-sync-complete", payloadFor(state)));
  assert.deepEqual(harness.startOrder(), state.sessions);
  assert.deepEqual(domOrder(harness), state.sessions.map((item) => item.sessionId));
});

for (const [name, change] of [
  ["addition", (sessions) => [...sessions, { sessionId: "00000000000000000000000000000005", sessionGeneration: 1 }]],
  ["deletion", (sessions) => sessions.slice(1)],
  ["restart of another tab", (sessions) => sessions.map((session, index) => index === 0
    ? { ...session, sessionGeneration: session.sessionGeneration + 1 } : session)],
  ["order change", (sessions) => [...sessions].reverse()],
]) {
  test(`${name} invalidates the complete start order until all messages are confirmed`, () => {
    const harness = initialized();
    const state = { ...base, sessions: change(base.sessions) };
    harness.deliver(globalMessage("workspace-sync-begin", payloadFor(state)));
    assert.equal(harness.startOrder(), undefined);
    sync(harness, state);
    assert.deepEqual(harness.startOrder(), state.sessions);
    assert.equal(harness.api.getActiveSessionId(), base.activeSessionId);
  });
}

test("out-of-batch structural mutations invalidate readiness; output and name/state changes keep it", () => {
  const harness = initialized();
  const session = base.sessions[0];
  const terminal = harness.api.sessions.get(session.sessionId).terminal;
  harness.deliver(sessionMessage("output", session.sessionId, { ...session, data: "한글 output" }));
  harness.deliver(sessionMessage("new-output-state", session.sessionId, { ...session, hasNewOutput: true }));
  upsert(harness, session, 0, { name: "새 이름", state: "exited", exitCode: 0 });
  assert.deepEqual(harness.startOrder(), base.sessions);
  assert.deepEqual(terminal.writes, ["한글 output"]);
  upsert(harness, session, 3);
  assert.equal(harness.startOrder(), undefined);
  sync(harness, base);
  upsert(harness, { ...session, sessionGeneration: session.sessionGeneration + 1 }, 0);
  assert.equal(harness.startOrder(), undefined);
  const restarted = { ...base, sessions: base.sessions.map((item) => item.sessionId === session.sessionId
    ? { ...item, sessionGeneration: item.sessionGeneration + 1 } : item) };
  sync(harness, restarted);
  harness.deliver(sessionMessage("remove-session", session.sessionId));
  assert.equal(harness.startOrder(), undefined);
});

test("mismatched, missing and stale entries cannot complete; a later full snapshot recovers", () => {
  const harness = createHarness();
  harness.deliver(globalMessage("workspace-sync-begin", payloadFor(base)));
  base.sessions.slice(1).forEach((session, index) => upsert(harness, session, index + 1));
  harness.deliver(globalMessage("workspace-sync-complete", payloadFor(base)));
  assert.equal(harness.startOrder(), undefined);
  upsert(harness, base.sessions[0], 0);
  harness.deliver(globalMessage("workspace-sync-complete", {
    ...payloadFor(base), order: [...base.sessions].reverse(),
  }));
  assert.equal(harness.startOrder(), undefined);
  harness.deliver(globalMessage("workspace-sync-complete", {
    ...payloadFor(base), activeSessionId: base.sessions[0].sessionId,
  }));
  assert.equal(harness.startOrder(), undefined);
  sync(harness, base);
  const restart = { ...base, sessions: base.sessions.map((session) => ({
    ...session, sessionGeneration: session.sessionGeneration + 1,
  })) };
  harness.deliver(globalMessage("workspace-sync-begin", payloadFor(restart)));
  harness.deliver(globalMessage("workspace-sync-complete", payloadFor(restart)));
  assert.equal(harness.startOrder(), undefined);
  sync(harness, restart);
  assert.deepEqual(harness.startOrder(), restart.sessions);
});

test("reorder and rejection resync preserve terminal, keyboard focus and active ID without resize", () => {
  const harness = initialized();
  const entries = [...harness.api.sessions.values()];
  const active = harness.api.sessions.get(base.activeSessionId);
  active.terminal.focus();
  harness.posted.length = 0;
  const fitCount = active.fitAddon.fitCount;
  const focusCount = active.terminal.focusCount;
  sync(harness, { ...base, sessions: [...base.sessions].reverse() });
  assert.equal(harness.document.activeElement, active.terminal.textarea);
  sync(harness, { ...base, sessions: [...base.sessions].reverse() });
  assert.equal(harness.document.activeElement, active.terminal.textarea);
  assert.equal(active.fitAddon.fitCount, fitCount);
  assert.equal(active.terminal.focusCount, focusCount);
  assert.equal(harness.api.getActiveSessionId(), base.activeSessionId);
  assert.equal(harness.posted.some((message) => message.type === "resize"), false);
  for (const entry of entries) {
    assert.equal(harness.api.sessions.get(entry.id), entry);
    assert.equal(entry.terminal.disposeCount, 0);
    assert.equal(entry.terminal.resetCount, 0);
  }
});

test("DOM reorder restores only the same focused tab element and preserves search or external focus", () => {
  const harness = initialized();
  const active = harness.api.sessions.get(base.activeSessionId);
  active.tabButton.focus();
  sync(harness, { ...base, sessions: [...base.sessions].reverse() });
  assert.equal(harness.document.activeElement, active.tabButton);
  assert.equal(active.tabButton.lastFocusOptions.preventScroll, true);
  const searchInput = harness.document.createElement("input");
  active.pane.append(searchInput);
  searchInput.focus();
  sync(harness, base);
  assert.equal(harness.document.activeElement, searchInput);
  const outside = harness.nodes.get("#saved-tabs");
  outside.focus();
  sync(harness, base);
  assert.equal(harness.document.activeElement, outside);
});

test("explicit mouse and keyboard selection retain fit/focus even when the active ID is unchanged", () => {
  const harness = initialized();
  const active = harness.api.sessions.get(base.activeSessionId);
  harness.api.requestSelection(active.id);
  sync(harness, base);
  assert.equal(harness.document.activeElement, active.terminal.textarea);
  harness.api.requestSelection(active.id, true);
  sync(harness, base);
  assert.equal(harness.document.activeElement, active.tabButton);
  const next = base.sessions[0].sessionId;
  harness.api.requestSelection(next);
  sync(harness, { ...base, activeSessionId: next });
  assert.equal(harness.document.activeElement, harness.api.sessions.get(next).terminal.textarea);
  assert.equal(harness.api.getActiveSessionId(), next);
  assert.equal(harness.posted.filter((message) => message.type === "select-session").length, 3);
  assert.equal(harness.posted.filter((message) => message.type === "resize").length, 4);
});

test("closing the active tab follows the current host activation, never the removed focus target", () => {
  const harness = initialized();
  const old = harness.api.sessions.get(base.activeSessionId);
  old.tabButton.focus();
  const state = { ...base, sessions: base.sessions.filter((session) => session.sessionId !== old.id),
    activeSessionId: base.sessions[0].sessionId };
  old.closeButton.events.get("click")();
  sync(harness, state);
  assert.equal(old.terminal.disposeCount, 1);
  assert.equal(harness.api.getActiveSessionId(), state.activeSessionId);
  assert.equal(harness.document.activeElement, harness.api.sessions.get(state.activeSessionId).terminal.textarea);
});

test("empty workspace completes and a new renderer never inherits another page's confirmed order", () => {
  const harness = initialized();
  sync(harness, { sessions: [], activeSessionId: null });
  assert.deepEqual(harness.startOrder(), []);
  assert.equal(harness.api.getActiveSessionId(), undefined);
  assert.equal(createHarness().startOrder(), undefined);
});

test("sync parser rejects malformed envelopes, duplicate IDs, invalid generations and unknown active IDs", () => {
  const harness = createHarness();
  const valid = globalMessage("workspace-sync-begin", payloadFor(base));
  for (const invalid of [
    null, [], { ...valid, version: 1 }, { ...valid, sessionId: base.activeSessionId },
    { ...valid, payload: null }, { ...valid, payload: { ...valid.payload, order: {} } },
    { ...valid, payload: { ...valid.payload, order: Array(9).fill(base.sessions[0]) } },
    { ...valid, payload: { ...valid.payload, order: [base.sessions[0], base.sessions[0]] } },
    { ...valid, payload: { order: [], activeSessionId: base.activeSessionId } },
    { ...valid, payload: { ...valid.payload, activeSessionId: null } },
    { ...valid, payload: { ...valid.payload, activeSessionId: "00000000000000000000000000000009" } },
    ...[0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, "1"].map((sessionGeneration) => ({
      ...valid, payload: { ...valid.payload, order: [{ ...base.sessions[0], sessionGeneration }] },
    })),
    { ...valid, payload: { order: [{ sessionId: "invalid", sessionGeneration: 1 }], activeSessionId: "invalid" } },
  ]) {
    assert.equal(harness.api.parseHostMessage(invalid), undefined);
  }
  assert.notEqual(harness.api.parseHostMessage(valid), undefined);
});

test("existing session messages retain output generation guards, paste, reset and standalone activation", () => {
  const harness = initialized();
  const session = base.sessions[0];
  const entry = harness.api.sessions.get(session.sessionId);
  harness.deliver(sessionMessage("output", entry.id, { sessionGeneration: 99, data: "stale" }));
  harness.deliver(sessionMessage("output", entry.id, { sessionGeneration: session.sessionGeneration, data: "current" }));
  assert.deepEqual(entry.terminal.writes, ["current"]);
  harness.deliver(sessionMessage("activate-session", entry.id));
  harness.deliver(sessionMessage("paste", entry.id, { data: "붙여넣기", sessionGeneration: session.sessionGeneration }));
  const pastedInput = harness.posted.filter(message => message.type === "input");
  assert.equal(pastedInput.length, 1);
  assert.equal(pastedInput[0].payload.data, "붙여넣기");
  harness.deliver(sessionMessage("reset", entry.id));
  assert.equal(entry.terminal.resetCount, 1);
  harness.deliver(sessionMessage("session-error", entry.id, { message: "error" }));
  assert.equal(entry.errorMessage, "error");
  harness.api.requestSelection(entry.id);
  harness.deliver(sessionMessage("activate-session", entry.id));
  harness.flushFrames();
  assert.equal(harness.document.activeElement, entry.terminal.textarea);
  assert.deepEqual(harness.startOrder(), base.sessions);
});
