// Fixture integrity only. Runtime and renderer tests must call their own production code.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const fixture = JSON.parse(readFileSync(new URL("./cases.json", import.meta.url), "utf8"));
const normalize = (id) => id.toLowerCase();
const orderOf = (state) => state.sessions.map((session) => normalize(session.sessionId));
const validId = (id) => typeof id === "string" && /^[0-9a-f]{32}$/i.test(id) && /^0{32}$/.test(id) === false;
const validGeneration = (generation) => Number.isSafeInteger(generation) && generation >= 1;
const object = (value) => value !== null && typeof value === "object" && Array.isArray(value) === false;

// Validate the documented wire shape, without simulating host acceptance or mutating an order.
function validWireShape(message) {
  if (object(message) === false || message.version !== 2 || message.type !== "reorder-session" ||
      validId(message.sessionId) === false || object(message.payload) === false) {
    return false;
  }
  const payload = message.payload;
  if (validId(payload.rendererInstanceId) === false || validId(payload.targetSessionId) === false ||
      validGeneration(payload.sessionGeneration) === false ||
      ["before", "after"].includes(payload.position) === false ||
      Array.isArray(payload.startOrder) === false || payload.startOrder.length < 1 ||
      payload.startOrder.length > 8) {
    return false;
  }
  if (payload.startOrder.every((entry) => object(entry) && validId(entry.sessionId) &&
      validGeneration(entry.sessionGeneration)) === false) {
    return false;
  }
  const ids = payload.startOrder.map((entry) => normalize(entry.sessionId));
  const source = payload.startOrder.find((entry) => normalize(entry.sessionId) === normalize(message.sessionId));
  return new Set(ids).size === ids.length && source?.sessionGeneration === payload.sessionGeneration;
}

test("shared fixture versions, state references and IDs are coherent", () => {
  assert.equal(fixture.fixtureVersion, 1);
  assert.equal(fixture.protocolVersion, 2);
  assert.deepEqual(fixture.persistenceModes, [false, true]);
  const allIds = [...fixture.cases, ...fixture.legacyMoves].map((entry) => entry.id);
  assert.equal(new Set(allIds).size, allIds.length);
  for (const state of Object.values(fixture.states)) {
    assert.ok(state.sessions.length >= 1 && state.sessions.length <= 8);
    assert.equal(new Set(orderOf(state)).size, state.sessions.length);
    assert.ok(state.sessions.every((entry) => validId(entry.sessionId) && validGeneration(entry.sessionGeneration)));
    assert.ok(orderOf(state).includes(state.activeSessionId));
    assert.equal(typeof state.rendererReady, "boolean");
    assert.ok(state.rendererInstanceId === null || validId(state.rendererInstanceId));
    if (state.rendererReady === true) {
      assert.ok(validId(state.rendererInstanceId));
    }
  }
});

for (const entry of fixture.cases) {
  test(`fixture integrity: ${entry.id}`, () => {
    const host = fixture.states[entry.hostState];
    const drag = fixture.states[entry.dragState];
    const expected = entry.expected;
    assert.ok(host && drag, "both state references must exist");
    assert.ok(["applied", "no-op", "rejected", "invalid-message", "cancelled", "click"].includes(expected.result));
    assert.ok(["current", "local-current", "none"].includes(expected.resync));
    assert.ok(["request", "cancelled", "click", "not-generated"].includes(expected.rendererOutcome));
    assert.equal(expected.sendCount, expected.rendererOutcome === "request" ? 1 : 0);
    assert.equal(expected.workspaceChangedCount, expected.result === "applied" ? 1 : 0);
    assert.deepEqual([...expected.order].sort(), orderOf(host).sort(), "expected order must preserve current membership");
    if (expected.result !== "applied") {
      assert.deepEqual(expected.order, orderOf(host), "cancel, rejection and no-op must preserve current order");
    }
    if (entry.message === null) {
      assert.equal(expected.parseAccepted, null);
      assert.equal(expected.sendCount, 0);
      assert.ok(entry.rendererAction === "click" || entry.rendererAction.startsWith("cancel:"));
      return;
    }
    assert.equal(validWireShape(entry.message), expected.parseAccepted, "wire shape and parse expectation disagree");
    if (expected.parseAccepted === false) {
      assert.equal(expected.result, "invalid-message");
      assert.equal(entry.rendererAction, "not-generated");
      assert.equal(expected.rendererOutcome, "not-generated");
      return;
    }
    assert.equal(entry.rendererAction, "drop");
    assert.deepEqual(entry.message.payload.startOrder.map((session) => ({
      sessionId: normalize(session.sessionId), sessionGeneration: session.sessionGeneration,
    })), drag.sessions, "valid requests must capture the complete drag-start state");
    if (expected.sendCount === 1) {
      assert.deepEqual(host.sessions, drag.sessions);
      assert.equal(host.rendererInstanceId, entry.message.payload.rendererInstanceId);
    }
    if (expected.result === "applied" || expected.result === "no-op") {
      const source = normalize(entry.message.sessionId);
      const target = normalize(entry.message.payload.targetSessionId);
      assert.notEqual(source, target);
      const sourceIndex = expected.order.indexOf(source);
      const targetIndex = expected.order.indexOf(target);
      assert.equal(sourceIndex - targetIndex, entry.message.payload.position === "before" ? -1 : 1);
      assert.deepEqual(expected.order.filter((id) => id !== source), orderOf(host).filter((id) => id !== source),
        "relative order of all other tabs must be unchanged");
      if (expected.result === "applied") {
        assert.notDeepEqual(expected.order, orderOf(host));
      }
    }
    if (expected.reason === "missing-session") {
      assert.equal(orderOf(host).includes(normalize(entry.message.sessionId)), false);
    }
    if (expected.reason === "missing-target") {
      assert.equal(orderOf(host).includes(normalize(entry.message.payload.targetSessionId)), false);
    }
    if (expected.reason === "self-target") {
      assert.equal(normalize(entry.message.sessionId), normalize(entry.message.payload.targetSessionId));
    }
    if (expected.reason === "stale-generation") {
      assert.notEqual(host.sessions.find((session) => session.sessionId === normalize(entry.message.sessionId))
        .sessionGeneration, entry.message.payload.sessionGeneration);
    }
    if (expected.reason === "stale-order") {
      assert.notDeepEqual(host.sessions, drag.sessions);
    }
    if (expected.reason === "stale-renderer") {
      assert.notEqual(host.rendererInstanceId, entry.message.payload.rendererInstanceId);
    }
    if (expected.reason === "renderer-unavailable") {
      assert.equal(host.rendererReady, false);
      assert.equal(expected.resync, "none");
    }
  });
}

test("fixtures cover required independent failure and preservation boundaries", () => {
  const required = [
    "move-forward-middle", "move-backward-middle", "move-to-front", "move-to-end-inactive",
    "move-active-id-preserved", "two-tabs-swap", "eight-tabs-end-to-front",
    "same-position-before", "same-position-after", "cancel-escape", "cancel-pointercancel",
    "cancel-lost-capture", "cancel-outside-drop", "cancel-renderer-lifetime", "click-below-threshold",
    "one-tab-no-request", "source-closed", "target-closed", "source-restarted", "target-restarted",
    "other-tab-restarted", "concurrent-left-right", "concurrent-add", "duplicate-delayed-request",
    "renderer-replaced", "renderer-not-ready", "missing-target", "self-target",
    "background-output-keeps-drag", "order-restored-to-start", "uppercase-guid-equivalence",
    "unknown-field-does-not-control-order", "duplicate-start-id", "too-many-start-tabs",
    "source-absent-from-start", "source-start-generation-mismatch", "generation-unsafe-integer",
  ];
  const ids = new Set(fixture.cases.map((entry) => entry.id));
  for (const id of required) {
    assert.ok(ids.has(id), `missing required fixture: ${id}`);
  }
});

for (const entry of fixture.legacyMoves) {
  test(`unchanged left/right wire fixture: ${entry.id}`, () => {
    assert.equal(entry.message.version, 2);
    assert.equal(entry.message.type, "move-session");
    assert.deepEqual(Object.keys(entry.message.payload), ["direction"]);
    assert.ok(["left", "right"].includes(entry.message.payload.direction));
    const before = orderOf(fixture.states[entry.hostState]);
    assert.deepEqual([...entry.expected.order].sort(), [...before].sort());
    const delta = entry.expected.order.indexOf(entry.message.sessionId) - before.indexOf(entry.message.sessionId);
    const expectedDelta = entry.expected.workspaceChangedCount === 0 ? 0 :
      entry.message.payload.direction === "left" ? -1 : 1;
    assert.equal(delta, expectedDelta);
  });
}
