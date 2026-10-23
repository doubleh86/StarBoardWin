import assert from "node:assert/strict";
import test from "node:test";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
import { build } from "esbuild";
import { createHarness } from "./rendererHarness.mjs";

const xtermSource = new URL("../node_modules/@xterm/xterm/src/", import.meta.url);
const writerBundle = await build({
  entryPoints: [fileURLToPath(new URL("common/input/WriteBuffer.ts", xtermSource))],
  nodePaths: [fileURLToPath(xtermSource)], bundle: true, write: false,
  format: "iife", globalName: "upstream", platform: "node",
});

const sessionId = "10000000000000000000000000000002";
const message = (type, payload) => ({ version: 2, type, sessionId, payload });
function upsert(harness, generation) {
  harness.deliver(message("session-upsert", {
    name: "출력", state: "running", exitCode: null, order: 0, canAddSession: true,
    sessionGeneration: generation, startingDirectory: "C:\\Work", homeDirectory: "C:\\Users",
    shellKind: "pwsh",
  }));
}
function initialized() {
  const harness = createHarness();
  upsert(harness, 1);
  harness.deliver(message("activate-session", {}));
  harness.posted.length = 0;
  return harness;
}
function acknowledgements(harness) { return harness.posted.filter((entry) => entry.type === "output-ack"); }

test("output is acknowledged only after xterm finishes parsing, in write order", () => {
  const harness = initialized();
  const entry = harness.api.sessions.get(sessionId);
  for (let sequence = 1; sequence <= 4; sequence++) {
    harness.deliver(message("output", { sessionGeneration: 1, sequence, data: "한글😀" }));
  }
  assert.equal(entry.terminal.writes.length, 4);
  assert.equal(acknowledgements(harness).length, 0);
  entry.terminal.completeWrites();
  assert.deepEqual(acknowledgements(harness).map((ack) => ack.payload.sequence), [1, 2, 3, 4]);
  for (const ack of acknowledgements(harness)) {
    assert.equal(ack.sessionId, sessionId);
    assert.equal(ack.payload.sessionGeneration, 1);
    assert.equal(ack.payload.rendererInstanceId, "10000000000000000000000000000001");
  }
});

test("late parsing callbacks from removed or replaced sessions cannot release new capacity", () => {
  for (const replace of [false, true]) {
    const harness = initialized();
    const previous = harness.api.sessions.get(sessionId).terminal;
    harness.deliver(message("output", { sessionGeneration: 1, sequence: 1, data: "old" }));
    if (replace) { upsert(harness, 2); }
    else { harness.deliver(message("remove-session", {})); }
    previous.completeWrites();
    assert.equal(acknowledgements(harness).length, 0);
  }
});

test("old-generation output and invalid sequence never produce an ACK", () => {
  const harness = initialized();
  harness.deliver(message("output", { sessionGeneration: 2, sequence: 1, data: "stale" }));
  for (const sequence of [0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, "1"]) {
    harness.deliver(message("output", { sessionGeneration: 1, sequence, data: "invalid" }));
  }
  const terminal = harness.api.sessions.get(sessionId).terminal;
  assert.equal(terminal.writes.includes("stale"), false);
  terminal.completeWrites();
  assert.equal(acknowledgements(harness).length, 0);
});

test("maximum clipboard data survives xterm bracket wrapping and renderer input serialization", () => {
  const harness = initialized();
  const terminal = harness.api.sessions.get(sessionId).terminal;
  terminal.bracketedPasteMode = true;
  const data = "\\".repeat(1_048_576);
  harness.deliver(message("paste", { sessionGeneration: 1, data }));
  const input = harness.posted.find((entry) => entry.type === "input");
  assert.ok(input);
  assert.equal(input.payload.data, `\x1b[200~${data}\x1b[201~`);
  assert.equal(JSON.parse(JSON.stringify(input)).payload.data, input.payload.data);
});

test("actual upstream write queue drains over 50M characters within an ACK-limited host window", () => {
  const timers = [];
  const context = vm.createContext({
    console, performance, queueMicrotask,
    setTimeout: (callback) => timers.push(callback), clearTimeout() {},
  });
  vm.runInContext(writerBundle.outputFiles[0].text, context);
  let parsedCharacters = 0;
  const writer = new context.upstream.WriteBuffer((data) => { parsedCharacters += data.length; });
  const harness = initialized();
  harness.api.sessions.get(sessionId).terminal.write = (data, callback) => writer.write(data, callback);
  let inFlight = 0;
  let completed = 0;
  let sent = 0;
  let maximumQueued = 0;
  const post = harness.window.chrome.webview.postMessage;
  harness.window.chrome.webview.postMessage = (value) => {
    post(value);
    if (value.type === "output-ack") { inFlight -= 65_536; completed++; }
  };
  try {
    // Simulated host window; C# buffer tests verify the production 256 Ki bound separately.
    while (completed < 800) {
      while (sent < 800 && inFlight < 262_144) {
        sent++;
        inFlight += 65_536;
        harness.deliver(message("output", { sessionGeneration: 1, sequence: sent, data: "x".repeat(65_536) }));
        maximumQueued = Math.max(maximumQueued, writer._pendingData);
      }
      assert.equal(inFlight, 262_144);
      const before = completed;
      while (timers.length > 0) { timers.shift()(); }
      assert.ok(completed > before, "actual xterm parsing must release host capacity");
    }
    assert.equal(completed, 800);
    assert.equal(parsedCharacters, 52_428_800);
    assert.equal(maximumQueued, 262_144);
    assert.equal(writer._pendingData, 0);
  } finally {
    writer.dispose();
  }
});
