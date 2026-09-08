import { FitAddon } from "@xterm/addon-fit";
import { Terminal, type ITheme } from "@xterm/xterm";
import "@xterm/xterm/css/xterm.css";
import "./styles.css";

const ProtocolVersion = 2;
const MaximumTabs = 8;
const SessionIdPattern = /^[0-9a-f]{32}$/i;
const EmptySessionId = "00000000000000000000000000000000";

type GlobalRendererMessageType =
  | "ready"
  | "new-tab"
  | "select-next"
  | "select-previous"
  | "renderer-error";

type SessionRendererMessageType =
  | "select-session"
  | "input"
  | "resize"
  | "copy"
  | "paste-request"
  | "close-session"
  | "restart-session"
  | "session-error";

type RendererMessage =
  | {
      version: number;
      type: GlobalRendererMessageType;
      sessionId?: never;
      payload: Record<string, unknown>;
    }
  | {
      version: number;
      type: SessionRendererMessageType;
      sessionId: string;
      payload: Record<string, unknown>;
    };

type GlobalHostMessageType = "initialize" | "apply-appearance";

type SessionHostMessageType =
  | "session-upsert"
  | "activate-session"
  | "output"
  | "paste"
  | "reset"
  | "remove-session"
  | "session-error";

type HostMessage =
  | {
      version: number;
      type: GlobalHostMessageType;
      sessionId?: never;
      payload: AppearancePayload;
    }
  | {
      version: number;
      type: SessionHostMessageType;
      sessionId: string;
      payload: Record<string, unknown>;
    };

type AppearancePayload = {
  fontFamily: string;
  fontSize: number;
  theme: {
    canvas: string;
    foreground: string;
    muted: string;
    accent: string;
    cursor: string;
    selection: string;
    ansiPalette: string[];
  };
};

type SessionState = "starting" | "running" | "restarting" | "exited" | "failed";

type SessionPayload = {
  name: string;
  state: SessionState;
  exitCode: number | null;
  order: number;
  canAddSession: boolean;
};

type SessionEntry = {
  id: string;
  name: string;
  state: SessionState;
  exitCode: number | null;
  order: number;
  terminal: Terminal;
  fitAddon: FitAddon;
  pane: HTMLElement;
  mount: HTMLElement;
  status: HTMLElement;
  statusMessage: HTMLElement;
  restartButton: HTMLButtonElement;
  tabItem: HTMLElement;
  tabButton: HTMLButtonElement;
  tabLabel: HTMLElement;
  tabStatus: HTMLElement;
  closeButton: HTMLButtonElement;
  errorMessage?: string;
  lastColumns: number;
  lastRows: number;
  fitErrorReported: boolean;
};

declare global {
  interface Window {
    chrome?: {
      webview?: {
        addEventListener: (
          type: "message",
          listener: (event: MessageEvent<unknown>) => void,
        ) => void;
        postMessage: (message: RendererMessage) => void;
      };
    };
  }
}

function getRequiredElement<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector);
  if (element === null) {
    throw new Error(`The terminal workspace mount point is missing: ${selector}`);
  }

  return element;
}

const tabList = getRequiredElement<HTMLElement>("#session-tabs");
const newTabButton = getRequiredElement<HTMLButtonElement>("#new-tab");
const workspace = getRequiredElement<HTMLElement>("#terminal-workspace");

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const sessions = new Map<string, SessionEntry>();
let activeSessionId: string | undefined;
let currentAppearance: AppearancePayload | undefined;
let canAddSession = true;
let requestedTerminalFocusSessionId: string | undefined;
let requestedTabFocusSessionId: string | undefined;
let focusTerminalOnNextActivation = false;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && Array.isArray(value) === false;
}

function getSessionIdentifier(message: Record<string, unknown>): string | undefined {
  const sessionId = message.sessionId;
  if (
    typeof sessionId !== "string" ||
    SessionIdPattern.test(sessionId) === false ||
    sessionId.toLowerCase() === EmptySessionId
  ) {
    return undefined;
  }

  return sessionId.toLowerCase();
}

function getPayload(message: Record<string, unknown>): Record<string, unknown> | undefined {
  return isRecord(message.payload) ? message.payload : undefined;
}

function isGlobalHostMessageType(value: string): value is GlobalHostMessageType {
  return value === "initialize" || value === "apply-appearance";
}

function isSessionHostMessageType(value: string): value is SessionHostMessageType {
  return (
    value === "session-upsert" ||
    value === "activate-session" ||
    value === "output" ||
    value === "paste" ||
    value === "reset" ||
    value === "remove-session" ||
    value === "session-error"
  );
}

function parseHostMessage(value: unknown): HostMessage | undefined {
  if (
    isRecord(value) === false ||
    value.version !== ProtocolVersion ||
    typeof value.type !== "string"
  ) {
    return undefined;
  }

  const payload = getPayload(value);
  if (payload === undefined) {
    return undefined;
  }

  if (isGlobalHostMessageType(value.type) === true) {
    if (value.sessionId !== undefined || isAppearancePayload(payload) === false) {
      return undefined;
    }

    return {
      version: ProtocolVersion,
      type: value.type,
      payload,
    };
  }

  if (isSessionHostMessageType(value.type) === false) {
    return undefined;
  }

  const sessionId = getSessionIdentifier(value);
  if (sessionId === undefined) {
    return undefined;
  }

  return {
    version: ProtocolVersion,
    type: value.type,
    sessionId,
    payload,
  };
}

function postGlobal(
  type: GlobalRendererMessageType,
  payload: Record<string, unknown> = {},
): void {
  window.chrome?.webview?.postMessage({
    version: ProtocolVersion,
    type,
    payload,
  });
}

function postSession(
  type: SessionRendererMessageType,
  sessionId: string,
  payload: Record<string, unknown> = {},
): void {
  if (
    SessionIdPattern.test(sessionId) === false ||
    sessionId.toLowerCase() === EmptySessionId
  ) {
    return;
  }

  window.chrome?.webview?.postMessage({
    version: ProtocolVersion,
    type,
    sessionId,
    payload,
  });
}

function toTerminalTheme(payload: AppearancePayload): ITheme {
  const palette = payload.theme.ansiPalette;
  return {
    background: payload.theme.canvas,
    foreground: payload.theme.foreground,
    cursor: payload.theme.cursor,
    cursorAccent: payload.theme.canvas,
    selectionBackground: payload.theme.selection,
    black: palette[0],
    red: palette[1],
    green: palette[2],
    yellow: palette[3],
    blue: palette[4],
    magenta: palette[5],
    cyan: palette[6],
    white: palette[7],
    brightBlack: palette[8],
    brightRed: palette[9],
    brightGreen: palette[10],
    brightYellow: palette[11],
    brightBlue: palette[12],
    brightMagenta: palette[13],
    brightCyan: palette[14],
    brightWhite: palette[15],
  };
}

function createTerminal(): Terminal {
  const rootStyle = getComputedStyle(document.documentElement);
  return new Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: reducedMotion.matches === false,
    cursorStyle: "block",
    fontFamily: rootStyle.getPropertyValue("--font-mono").trim(),
    fontSize: Number.parseFloat(rootStyle.getPropertyValue("--font-size-terminal")),
    lineHeight: Number.parseFloat(rootStyle.getPropertyValue("--line-height-terminal")),
    scrollback: 10_000,
  });
}

function stateLabel(entry: SessionEntry): string {
  switch (entry.state) {
    case "starting":
      return "시작 중";
    case "restarting":
      return "다시 시작 중";
    case "exited":
      return `종료됨${entry.exitCode === null ? "" : `, exit ${entry.exitCode}`}`;
    case "failed":
      return "오류";
    default:
      return "실행 중";
  }
}

function createSession(sessionId: string, payload: SessionPayload): SessionEntry {
  const pane = document.createElement("section");
  pane.id = `session-pane-${sessionId}`;
  pane.className = "session-pane";
  pane.setAttribute("role", "tabpanel");
  pane.setAttribute("aria-labelledby", `session-tab-${sessionId}`);
  pane.hidden = true;

  const mount = document.createElement("div");
  mount.className = "terminal-mount";
  mount.setAttribute("aria-label", `${payload.name} terminal`);

  const status = document.createElement("div");
  status.className = "session-status";
  status.setAttribute("role", "status");
  status.setAttribute("aria-live", "polite");
  status.hidden = true;

  const statusMessage = document.createElement("span");
  statusMessage.className = "session-status-message";

  const restartButton = document.createElement("button");
  restartButton.className = "session-restart";
  restartButton.type = "button";
  restartButton.textContent = "다시 시작";
  restartButton.addEventListener("click", () => {
    requestedTerminalFocusSessionId = sessionId;
    postSession("restart-session", sessionId);
  });
  status.append(statusMessage, restartButton);
  pane.append(mount, status);
  workspace.append(pane);

  const tabItem = document.createElement("div");
  tabItem.className = "tab-item";
  tabItem.setAttribute("role", "presentation");

  const tabButton = document.createElement("button");
  tabButton.id = `session-tab-${sessionId}`;
  tabButton.className = "tab-button";
  tabButton.type = "button";
  tabButton.setAttribute("role", "tab");
  tabButton.setAttribute("aria-controls", pane.id);
  tabButton.addEventListener("click", () => requestSelection(sessionId));
  tabButton.addEventListener("keydown", (event) => handleTabKeyDown(event, sessionId));

  const tabStatus = document.createElement("span");
  tabStatus.className = "tab-status";
  tabStatus.setAttribute("aria-hidden", "true");

  const tabLabel = document.createElement("span");
  tabLabel.className = "tab-label";
  tabButton.append(tabStatus, tabLabel);

  const closeButton = document.createElement("button");
  closeButton.className = "tab-close";
  closeButton.type = "button";
  closeButton.textContent = "×";
  closeButton.addEventListener("click", () => {
    focusTerminalOnNextActivation = true;
    postSession("close-session", sessionId);
  });
  tabItem.append(tabButton, closeButton);

  const terminal = createTerminal();
  const fitAddon = new FitAddon();
  terminal.loadAddon(fitAddon);
  terminal.open(mount);
  terminal.onData((data) => postSession("input", sessionId, { data }));
  terminal.attachCustomKeyEventHandler((event) => {
    if (event.type !== "keydown") {
      return true;
    }

    if (
      event.ctrlKey === true &&
      event.altKey === false &&
      event.metaKey === false &&
      event.code === "KeyC" &&
      terminal.hasSelection() === true
    ) {
      postSession("copy", sessionId, { data: terminal.getSelection() });
      return false;
    }

    if (
      event.ctrlKey === true &&
      event.altKey === false &&
      event.metaKey === false &&
      event.code === "KeyV"
    ) {
      postSession("paste-request", sessionId);
      return false;
    }

    return true;
  });
  mount.addEventListener("pointerdown", () => terminal.focus());

  const entry: SessionEntry = {
    id: sessionId,
    name: payload.name,
    state: payload.state,
    exitCode: payload.exitCode,
    order: payload.order,
    terminal,
    fitAddon,
    pane,
    mount,
    status,
    statusMessage,
    restartButton,
    tabItem,
    tabButton,
    tabLabel,
    tabStatus,
    closeButton,
    lastColumns: 0,
    lastRows: 0,
    fitErrorReported: false,
  };

  applyTerminalOptions(entry);
  return entry;
}

function applyTerminalOptions(entry: SessionEntry): void {
  if (currentAppearance === undefined) {
    return;
  }

  entry.terminal.options.fontFamily = currentAppearance.fontFamily;
  entry.terminal.options.fontSize = currentAppearance.fontSize;
  entry.terminal.options.theme = toTerminalTheme(currentAppearance);
}

function applyAppearance(payload: AppearancePayload): void {
  currentAppearance = payload;
  document.documentElement.style.setProperty("--color-canvas", payload.theme.canvas);
  document.documentElement.style.setProperty("--color-ink", payload.theme.foreground);
  document.documentElement.style.setProperty("--color-neutral", payload.theme.muted);
  document.documentElement.style.setProperty("--color-accent", payload.theme.accent);

  for (const entry of sessions.values()) {
    applyTerminalOptions(entry);
  }

  fitActiveSession(true);
}

function requestSelection(sessionId: string, focusTab = false): void {
  if (focusTab === true) {
    requestedTabFocusSessionId = sessionId;
  } else {
    requestedTerminalFocusSessionId = sessionId;
  }

  postSession("select-session", sessionId);
}

function orderedSessions(): SessionEntry[] {
  return [...sessions.values()].sort((left, right) => left.order - right.order);
}

function handleTabKeyDown(event: KeyboardEvent, sessionId: string): void {
  const entries = orderedSessions();
  const currentIndex = entries.findIndex((entry) => entry.id === sessionId);
  if (currentIndex < 0) {
    return;
  }

  let nextIndex: number | undefined;
  if (event.key === "ArrowRight") {
    nextIndex = (currentIndex + 1) % entries.length;
  } else if (event.key === "ArrowLeft") {
    nextIndex = (currentIndex - 1 + entries.length) % entries.length;
  } else if (event.key === "Home") {
    nextIndex = 0;
  } else if (event.key === "End") {
    nextIndex = entries.length - 1;
  }

  if (nextIndex === undefined) {
    return;
  }

  event.preventDefault();
  requestSelection(entries[nextIndex].id, true);
}

function renderTab(entry: SessionEntry): void {
  const label = stateLabel(entry);
  const isActive = entry.id === activeSessionId;
  entry.tabItem.dataset.state = entry.state;
  entry.tabButton.setAttribute("aria-selected", isActive.toString());
  entry.tabButton.setAttribute("aria-label", `${entry.name}, ${label}`);
  entry.tabButton.setAttribute(
    "aria-busy",
    (entry.state === "starting" || entry.state === "restarting").toString(),
  );
  entry.tabButton.tabIndex = isActive ? 0 : -1;
  entry.tabLabel.textContent = entry.name;
  entry.closeButton.setAttribute("aria-label", `${entry.name} 탭 닫기`);
  entry.restartButton.setAttribute("aria-label", `${entry.name} shell 다시 시작`);
}

function renderTabs(): void {
  for (const entry of orderedSessions()) {
    renderTab(entry);
    tabList.append(entry.tabItem);
  }

  newTabButton.disabled = canAddSession === false || sessions.size >= MaximumTabs;
}

function updateSessionStatus(entry: SessionEntry): void {
  const isLoading = entry.state === "starting" || entry.state === "restarting";
  const isError =
    entry.state === "exited" || entry.state === "failed" || entry.errorMessage !== undefined;

  entry.status.classList.toggle("is-loading", isLoading);
  entry.status.classList.toggle("is-error", isError);
  entry.restartButton.hidden = isError === false;
  entry.pane.setAttribute("aria-busy", isLoading.toString());

  if (isLoading === true) {
    entry.status.setAttribute("role", "status");
    entry.statusMessage.textContent = stateLabel(entry);
    entry.status.hidden = false;
    return;
  }

  if (isError === true) {
    entry.statusMessage.textContent = entry.errorMessage ?? stateLabel(entry);
    entry.status.setAttribute("role", "alert");
    entry.status.hidden = false;
    return;
  }

  entry.status.setAttribute("role", "status");
  entry.status.hidden = true;
}

function upsertSession(sessionId: string, payload: SessionPayload): void {
  let entry = sessions.get(sessionId);
  if (entry === undefined) {
    entry = createSession(sessionId, payload);
    sessions.set(sessionId, entry);
  }

  entry.name = payload.name;
  entry.state = payload.state;
  entry.exitCode = payload.exitCode;
  entry.order = payload.order;
  if (
    payload.state === "running" ||
    payload.state === "starting" ||
    payload.state === "restarting"
  ) {
    entry.errorMessage = undefined;
  }

  canAddSession = payload.canAddSession;
  entry.mount.setAttribute("aria-label", `${payload.name} terminal`);
  updateSessionStatus(entry);
  renderTabs();
}

function activateSession(sessionId: string): void {
  const nextEntry = sessions.get(sessionId);
  if (nextEntry === undefined) {
    return;
  }

  activeSessionId = sessionId;
  for (const entry of sessions.values()) {
    const isActive = entry.id === sessionId;
    entry.pane.hidden = isActive === false;
    entry.tabButton.setAttribute("aria-selected", isActive.toString());
    entry.tabButton.tabIndex = isActive ? 0 : -1;
  }

  nextEntry.tabItem.scrollIntoView({ block: "nearest", inline: "nearest" });
  const shouldFocusTab = requestedTabFocusSessionId === sessionId;
  const shouldFocusTerminal =
    requestedTerminalFocusSessionId === sessionId || focusTerminalOnNextActivation === true;
  if (shouldFocusTab === true) {
    requestedTabFocusSessionId = undefined;
  }
  if (requestedTerminalFocusSessionId === sessionId) {
    requestedTerminalFocusSessionId = undefined;
  }
  focusTerminalOnNextActivation = false;

  requestAnimationFrame(() => {
    fitSession(nextEntry, true);
    if (document.hasFocus() === true) {
      if (shouldFocusTab === true) {
        nextEntry.tabButton.focus();
      } else if (shouldFocusTerminal === true) {
        nextEntry.terminal.focus();
      }
    }
  });
}

function removeSession(sessionId: string): void {
  const entry = sessions.get(sessionId);
  if (entry === undefined) {
    return;
  }

  sessions.delete(sessionId);
  entry.terminal.dispose();
  entry.pane.remove();
  entry.tabItem.remove();
  if (activeSessionId === sessionId) {
    activeSessionId = undefined;
  }

  renderTabs();
}

function setSessionError(sessionId: string, message: string): void {
  const entry = sessions.get(sessionId);
  if (entry === undefined) {
    return;
  }

  entry.errorMessage = message;
  updateSessionStatus(entry);
  renderTab(entry);
}

function fitSession(entry: SessionEntry, forceReport: boolean): void {
  if (
    entry.id !== activeSessionId ||
    entry.mount.clientWidth <= 0 ||
    entry.mount.clientHeight <= 0
  ) {
    return;
  }

  try {
    entry.fitAddon.fit();
    const changed =
      entry.lastColumns !== entry.terminal.cols || entry.lastRows !== entry.terminal.rows;
    entry.lastColumns = entry.terminal.cols;
    entry.lastRows = entry.terminal.rows;
    entry.fitErrorReported = false;
    if (changed === true || forceReport === true) {
      postSession("resize", entry.id, {
        columns: entry.terminal.cols,
        rows: entry.terminal.rows,
      });
    }
  } catch {
    if (entry.fitErrorReported === false) {
      entry.fitErrorReported = true;
      postSession("session-error", entry.id, { kind: "fit" });
    }
  }
}

function fitActiveSession(forceReport = false): void {
  if (activeSessionId === undefined) {
    return;
  }

  const entry = sessions.get(activeSessionId);
  if (entry !== undefined) {
    fitSession(entry, forceReport);
  }
}

function isAppearancePayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & AppearancePayload {
  if (
    typeof payload.fontFamily !== "string" ||
    payload.fontFamily.trim().length === 0 ||
    typeof payload.fontSize !== "number" ||
    Number.isFinite(payload.fontSize) === false ||
    payload.fontSize <= 0 ||
    isRecord(payload.theme) === false
  ) {
    return false;
  }

  const theme = payload.theme;
  return (
    typeof theme.canvas === "string" &&
    typeof theme.foreground === "string" &&
    typeof theme.muted === "string" &&
    typeof theme.accent === "string" &&
    typeof theme.cursor === "string" &&
    typeof theme.selection === "string" &&
    Array.isArray(theme.ansiPalette) === true &&
    theme.ansiPalette.length === 16 &&
    theme.ansiPalette.every((color) => typeof color === "string")
  );
}

function isSessionPayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & SessionPayload {
  const validStates: SessionState[] = [
    "starting",
    "running",
    "restarting",
    "exited",
    "failed",
  ];
  return (
    typeof payload.name === "string" &&
    typeof payload.state === "string" &&
    validStates.includes(payload.state as SessionState) &&
    (payload.exitCode === null || typeof payload.exitCode === "number") &&
    Number.isInteger(payload.order) === true &&
    typeof payload.canAddSession === "boolean"
  );
}

function handleHostMessage(value: unknown): void {
  const message = parseHostMessage(value);
  if (message === undefined) {
    return;
  }

  if (message.type === "initialize" || message.type === "apply-appearance") {
    applyAppearance(message.payload);
    return;
  }

  const sessionId = message.sessionId;
  const payload = message.payload;

  if (message.type === "session-upsert") {
    if (isSessionPayload(payload) === true) {
      upsertSession(sessionId, payload);
    }
    return;
  }

  if (message.type === "activate-session") {
    activateSession(sessionId);
    return;
  }

  if (message.type === "remove-session") {
    removeSession(sessionId);
    return;
  }

  const entry = sessions.get(sessionId);
  if (entry === undefined) {
    return;
  }

  if (message.type === "output" && typeof payload.data === "string") {
    entry.terminal.write(payload.data);
    return;
  }

  if (message.type === "paste" && typeof payload.data === "string") {
    entry.terminal.paste(payload.data);
    return;
  }

  if (message.type === "reset") {
    entry.terminal.reset();
    entry.errorMessage = undefined;
    updateSessionStatus(entry);
    return;
  }

  if (message.type === "session-error" && typeof payload.message === "string") {
    setSessionError(sessionId, payload.message);
  }
}

function handleApplicationShortcut(event: KeyboardEvent): void {
  if (
    event.type !== "keydown" ||
    event.ctrlKey === false ||
    event.altKey === true ||
    event.metaKey === true
  ) {
    return;
  }

  let handled = false;
  if (event.shiftKey === true && event.code === "KeyT") {
    if (newTabButton.disabled === false) {
      focusTerminalOnNextActivation = true;
      postGlobal("new-tab");
    }
    handled = true;
  } else if (event.code === "Tab") {
    focusTerminalOnNextActivation = true;
    postGlobal(event.shiftKey === true ? "select-previous" : "select-next");
    handled = true;
  } else if (
    event.shiftKey === true &&
    event.code === "KeyW" &&
    activeSessionId !== undefined
  ) {
    focusTerminalOnNextActivation = true;
    postSession("close-session", activeSessionId);
    handled = true;
  }

  if (handled === true) {
    event.preventDefault();
    event.stopPropagation();
  }
}

newTabButton.addEventListener("click", () => {
  if (newTabButton.disabled === false) {
    focusTerminalOnNextActivation = true;
    postGlobal("new-tab");
  }
});

reducedMotion.addEventListener("change", (event) => {
  for (const entry of sessions.values()) {
    entry.terminal.options.cursorBlink = event.matches === false;
  }
});

document.addEventListener("keydown", handleApplicationShortcut, true);
window.chrome?.webview?.addEventListener("message", (event) => {
  handleHostMessage(event.data);
});

const resizeObserver = new ResizeObserver(() => fitActiveSession());
resizeObserver.observe(workspace);

window.addEventListener("error", () => postGlobal("renderer-error", { kind: "runtime" }));
window.addEventListener("unhandledrejection", () =>
  postGlobal("renderer-error", { kind: "unhandled-rejection" }),
);

postGlobal("ready");
