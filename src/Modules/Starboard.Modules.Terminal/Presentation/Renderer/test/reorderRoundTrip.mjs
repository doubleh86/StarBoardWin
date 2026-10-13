// Line-oriented bridge for TerminalTabReorderTests. Production renderer code runs
// in the existing simulated DOM/xterm harness; the host is the real C# coordinator.
import assert from "node:assert/strict";
import { createInterface } from "node:readline";
import { createHarness } from "./rendererHarness.mjs";

let harness = createHarness();
let entries;
let active;
let fitCount;
const reply = (value) => process.stdout.write(`${JSON.stringify(value)}\n`);
const requests = () => harness.posted.filter((message) => message.type === "reorder-session");
function drop(cancel = false) {
  const order = harness.startOrder();
  const source = harness.api.sessions.get(order[0].sessionId);
  harness.posted.length = 0;
  harness.dispatch("pointerdown", source.tabButton, { clientX: 20 });
  harness.dispatch("pointermove", harness.document, { clientX: 250 });
  if (cancel) { harness.dispatch("keydown", harness.document, { key: "Escape" }); }
  harness.dispatch("pointerup", harness.document, { clientX: 250, buttons: 0 });
  assert.equal(requests().length, cancel ? 0 : 1);
  assert.equal(harness.captures.size, 0);
  return requests()[0] ?? null;
}
function verifyPreservation() {
  assert.equal(harness.api.getActiveSessionId(), active.id);
  assert.equal(harness.document.activeElement, active.terminal.textarea);
  assert.equal(active.fitAddon.fitCount, fitCount);
  assert.equal(harness.posted.some((message) => message.type === "resize"), false);
  for (const entry of entries) {
    assert.equal(harness.api.sessions.get(entry.id), entry);
    assert.equal(entry.terminal.disposeCount, 0);
    assert.equal(entry.terminal.resetCount, 0);
    assert.deepEqual(entry.terminal.writes, ["기존 output / scrollback 모형"]);
  }
}

for await (const line of createInterface({ input: process.stdin, crlfDelay: Infinity })) {
  const command = JSON.parse(line);
  if (command.fresh) { harness = createHarness(); }
  const previousOrder = harness.startOrder();
  for (const [index, message] of command.messages.entries()) {
    harness.deliver(message);
    if (index < command.messages.length - 1) {
      assert.equal(harness.startOrder(), undefined);
      if (previousOrder !== undefined) {
        const domOrder = harness.nodes.get("#session-tabs").children.map((item) =>
          [...harness.api.sessions.values()].find((entry) => entry.tabItem === item).id);
        assert.deepEqual(domOrder, previousOrder.map((session) => session.sessionId));
      }
    }
  }
  harness.flushFrames();
  const complete = command.messages.at(-1).payload;
  assert.deepEqual(harness.startOrder(), complete.order);
  assert.equal(harness.api.getActiveSessionId(), complete.activeSessionId);
  if (command.action === "start") {
    entries = [...harness.api.sessions.values()];
    active = harness.api.sessions.get(complete.activeSessionId);
    active.terminal.focus();
    fitCount = active.fitAddon.fitCount;
    for (const entry of entries) { entry.terminal.write("기존 output / scrollback 모형"); }
    harness.posted.length = 0;
    assert.equal(drop(true), null);
    verifyPreservation();
    reply(drop());
  } else if (command.action === "next") {
    verifyPreservation();
    const request = drop();
    assert.deepEqual(JSON.parse(JSON.stringify(request.payload.startOrder)), complete.order);
    verifyPreservation();
    reply(request);
  } else if (command.action === "restored") {
    reply({ restored: true, names: complete.order.map((session) =>
      harness.api.sessions.get(session.sessionId).tabLabel.textContent) });
  } else {
    verifyPreservation();
    reply({ preserved: true });
  }
}

