import assert from "node:assert/strict";
import test from "node:test";
import { shouldApplyCurrentDirectory } from "../src/savedTabDirectoryPolicy.ts";

const request = {
  requestId: "request-a",
  sessionId: "tab-a",
  sessionGeneration: 2,
  edited: false,
  dialogIsCurrent: true,
};
const response = {
  requestId: "request-a",
  sessionId: "tab-a",
  sessionGeneration: 2,
  currentDirectory: "C:\\한글 폴더",
};

test("accepts the current tab and session location", () => {
  assert.equal(shouldApplyCurrentDirectory(request, response, 2), true);
});

test("rejects another tab, session generation, or dialog request", () => {
  assert.equal(shouldApplyCurrentDirectory(request, { ...response, sessionId: "tab-b" }, 2), false);
  assert.equal(shouldApplyCurrentDirectory(request, { ...response, sessionGeneration: 1 }, 2), false);
  assert.equal(shouldApplyCurrentDirectory(request, response, 3), false);
  assert.equal(shouldApplyCurrentDirectory(request, { ...response, requestId: "old" }, 2), false);
});

test("keeps user edits and a canceled or replaced dialog", () => {
  assert.equal(shouldApplyCurrentDirectory({ ...request, edited: true }, response, 2), false);
  assert.equal(shouldApplyCurrentDirectory({ ...request, dialogIsCurrent: false }, response, 2), false);
  assert.equal(shouldApplyCurrentDirectory(request, { ...response, currentDirectory: null }, 2), false);
});
