import assert from "node:assert/strict";
import test from "node:test";
import { createHarness } from "./rendererHarness.mjs";

test("layout recovery refits the existing active session without changing order, focus, output or generation", () => {
  const h = createHarness();
  const ids = ["00000000000000000000000000000001", "00000000000000000000000000000002"];
  ids.forEach((id, order) => h.deliver({ version: 2, type: "session-upsert", sessionId: id,
    payload: { name: "tab", state: "running", exitCode: null, order, canAddSession: true,
      sessionGeneration: 7, startingDirectory: "", homeDirectory: "", shellKind: "pwsh" } }));
  h.deliver({ version: 2, type: "activate-session", sessionId: ids[1], payload: {} });
  h.flushFrames();
  const active = h.api.sessions.get(ids[1]);
  active.terminal.write("retained output");
  const before = { fit: active.fitAddon.fitCount, focus: active.terminal.focusCount,
    element: h.document.activeElement, order: [...h.api.sessions.keys()] };
  for (let i = 0; i < 5; i++) {
    h.deliver({ version: 2, type: "recover-layout", payload: {} });
  }
  assert.equal(active.fitAddon.fitCount, before.fit + 5);
  assert.equal(active.terminal.focusCount, before.focus);
  assert.equal(h.document.activeElement, before.element);
  assert.equal(h.api.getActiveSessionId(), ids[1]);
  assert.equal(active.sessionGeneration, 7);
  assert.equal(active.terminal.disposeCount, 0);
  assert.equal(active.terminal.resetCount, 0);
  assert.deepEqual(active.terminal.writes, ["retained output"]);
  assert.deepEqual([...h.api.sessions.keys()], before.order);
  assert.equal(h.posted.some(m => ["input", "restart-session", "select-session", "reorder-session"].includes(m.type)), false);
});

test("layout recovery with no active session is inert", () => {
  const h = createHarness();
  const count = h.posted.length;
  h.deliver({ version: 2, type: "recover-layout", payload: {} });
  assert.equal(h.posted.length, count);
  assert.equal(h.api.sessions.size, 0);
  assert.equal(h.api.parseHostMessage({ version: 2, type: "recover-layout", sessionId: "bad", payload: {} }), undefined);
  assert.equal(h.api.parseHostMessage({ version: 2, type: "recover-layout", payload: { width: 1040 } }), undefined);
});
