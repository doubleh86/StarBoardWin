import { FitAddon } from "@xterm/addon-fit";
import { SearchAddon, type ISearchOptions } from "@xterm/addon-search";
import { WebLinksAddon } from "@xterm/addon-web-links";
import { Terminal, type ITheme } from "@xterm/xterm";
import "@xterm/xterm/css/xterm.css";
import "./styles.css";

const ProtocolVersion = 2;
const MaximumTabs = 8;
const SessionIdPattern = /^[0-9a-f]{32}$/i;
const EmptySessionId = "00000000000000000000000000000000";
const MaximumUrlLength = 2_048;
const RendererInstanceId = crypto.randomUUID().replaceAll("-", "").toLowerCase();

type GlobalRendererMessageType =
  | "ready"
  | "select-next"
  | "select-previous"
  | "renderer-error"
  | "retry-launch-profiles"
  | "create-saved-tab"
  | "update-saved-tab"
  | "delete-saved-tab"
  | "launch-saved-tab"
  | "cancel-saved-tab-launch";

type SessionRendererMessageType =
  | "new-tab"
  | "duplicate-tab"
  | "select-session"
  | "input"
  | "resize"
  | "copy"
  | "paste-request"
  | "drop-paths"
  | "open-url-request"
  | "close-session"
  | "restart-session"
  | "rename-session"
  | "move-session"
  | "set-starting-directory"
  | "confirmation-response"
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

type GlobalHostMessageType =
  | "initialize"
  | "apply-appearance"
  | "workspace-save-status"
  | "launch-profiles-result"
  | "saved-tabs-snapshot"
  | "saved-tab-operation-result"
  | "saved-tab-launch-result";

type SessionHostMessageType =
  | "session-upsert"
  | "activate-session"
  | "output"
  | "paste"
  | "reset"
  | "remove-session"
  | "session-error"
  | "confirmation-request"
  | "confirmation-cancel"
  | "path-drop-result"
  | "url-open-result"
  | "new-output-state";

type HostMessage =
  | {
      version: number;
      type: "initialize" | "apply-appearance";
      sessionId?: never;
      payload: AppearancePayload;
    }
  | {
      version: number;
      type: "workspace-save-status";
      sessionId?: never;
      payload: WorkspaceSaveStatusPayload;
    }
  | {
      version: number;
      type: "saved-tabs-snapshot";
      sessionId?: never;
      payload: SavedTabsSnapshotPayload;
    }
  | {
      version: number;
      type: "saved-tab-operation-result";
      sessionId?: never;
      payload: SavedTabOperationResultPayload;
    }
  | {
      version: number;
      type: "saved-tab-launch-result";
      sessionId?: never;
      payload: SavedTabLaunchResultPayload;
    }
  | {
      version: number;
      type: "launch-profiles-result";
      sessionId?: never;
      payload: LaunchProfilesPayload;
    }
  | {
      version: number;
      type: SessionHostMessageType;
      sessionId: string;
      payload: Record<string, unknown>;
    };

type LaunchProfile = {
  profileId: string;
  kind: "built-in-shell" | "wsl-distribution";
  displayName: string;
  shellKind: "pwsh" | "powershell" | "cmd" | null;
  wslDistributionName: string | null;
};

type LaunchProfilesPayload = {
  status: "succeeded" | "wsl-unavailable" | "wsl-discovery-failed";
  profiles: LaunchProfile[];
  failureMessage: string | null;
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

type WorkspaceSaveStatusPayload = {
  state: "saved" | "failed" | "deleted" | "disabled";
  message: string | null;
};

type SavedTab = {
  savedTabId: string;
  name: string;
  startingDirectory: string;
  shellKind: "automatic" | "pwsh" | "powershell" | "cmd";
};

type SavedTabDraft = Omit<SavedTab, "savedTabId">;

type SavedTabsSnapshotPayload = {
  schemaVersion: number;
  maximumSavedTabs: number;
  runningTabCount: number;
  maximumRunningTabs: number;
  tabs: SavedTab[];
};

type SavedTabOperationResultPayload = {
  requestId: string;
  operation: "create" | "update" | "delete";
  status: string;
  failureMessage: string | null;
  snapshot: SavedTabsSnapshotPayload | null;
};

type SavedTabLaunchResultPayload = {
  requestId: string;
  savedTabId: string;
  status: string;
  sessionId: string | null;
  sessionGeneration: number | null;
  failureMessage: string | null;
  runningTabCount: number;
  maximumRunningTabs: number;
};

type SessionState = "starting" | "running" | "restarting" | "exited" | "failed";

type SessionPayload = {
  name: string;
  state: SessionState;
  exitCode: number | null;
  order: number;
  canAddSession: boolean;
  sessionGeneration: number;
  startingDirectory: string;
  homeDirectory: string;
  shellKind: SavedTab["shellKind"];
};

type ConfirmationKind = "close" | "paste" | "path-drop" | "url-open";

type ConfirmationRequestPayload = {
  requestId: string;
  sessionGeneration: number;
  kind: ConfirmationKind;
  sessionName: string;
  clipboardText?: string;
  quotedInput?: string;
  targetUrl?: string;
};

type ConfirmationCancelPayload = {
  requestId: string;
  sessionGeneration: number;
};

type NewOutputStatePayload = {
  sessionGeneration: number;
  hasNewOutput: boolean;
};

type PendingConfirmation = ConfirmationRequestPayload & {
  sessionId: string;
  dialog: HTMLDialogElement;
  preview?: HTMLElement;
};

type SessionEntry = {
  id: string;
  name: string;
  state: SessionState;
  exitCode: number | null;
  order: number;
  sessionGeneration: number;
  startingDirectory: string;
  homeDirectory: string;
  shellKind: SavedTab["shellKind"];
  terminal: Terminal;
  fitAddon: FitAddon;
  searchAddon: SearchAddon;
  pane: HTMLElement;
  mount: HTMLElement;
  status: HTMLElement;
  statusMessage: HTMLElement;
  restartButton: HTMLButtonElement;
  changeDirectoryButton: HTMLButtonElement;
  homeDirectoryButton: HTMLButtonElement;
  tabItem: HTMLElement;
  tabButton: HTMLButtonElement;
  tabLabel: HTMLElement;
  tabStatus: HTMLElement;
  newOutputIndicator: HTMLElement;
  closeButton: HTMLButtonElement;
  hasNewOutput: boolean;
  errorMessage?: string;
  lastColumns: number;
  lastRows: number;
  fitErrorReported: boolean;
};

type SearchOverlay = {
  sessionId: string;
  element: HTMLElement;
  input: HTMLInputElement;
  status: HTMLElement;
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
        postMessageWithAdditionalObjects: (
          message: RendererMessage,
          additionalObjects: readonly object[],
        ) => void;
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
const savedTabsButton = getRequiredElement<HTMLButtonElement>("#saved-tabs");
const workspace = getRequiredElement<HTMLElement>("#terminal-workspace");

const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const sessions = new Map<string, SessionEntry>();
let activeSessionId: string | undefined;
let currentAppearance: AppearancePayload | undefined;
let canAddSession = true;
let requestedTerminalFocusSessionId: string | undefined;
let requestedTabFocusSessionId: string | undefined;
let focusTerminalOnNextActivation = false;
let contextMenu: HTMLElement | undefined;
let contextMenuFocusReturnSessionId: string | undefined;
let savedTabsMenu: HTMLElement | undefined;
let savedTabsDialog: HTMLDialogElement | undefined;
let savedTabsDialogFocusReturnSessionId: string | undefined;
let savedTabsSnapshot: SavedTabsSnapshotPayload | undefined;
let savedTabsFeedback = "";
let pendingSavedTabLaunchRequestId: string | undefined;
let launchProfiles: LaunchProfilesPayload | undefined;
const pendingProfileLaunches = new Set<string>();
const pendingTabDuplicates = new Set<string>();
let pendingConfirmation: PendingConfirmation | undefined;
let searchOverlay: SearchOverlay | undefined;
const suppressedConfirmationKeys = new Set<string>();

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
  return (
    value === "initialize" ||
    value === "apply-appearance" ||
    value === "workspace-save-status" ||
    value === "launch-profiles-result"
  );
}

function isSessionHostMessageType(value: string): value is SessionHostMessageType {
  return (
    value === "session-upsert" ||
    value === "activate-session" ||
    value === "output" ||
    value === "paste" ||
    value === "reset" ||
    value === "remove-session" ||
    value === "session-error" ||
    value === "confirmation-request" ||
    value === "confirmation-cancel" ||
    value === "path-drop-result" ||
    value === "url-open-result" ||
    value === "new-output-state"
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

  if (value.type === "workspace-save-status") {
    if (
      value.sessionId !== undefined ||
      isWorkspaceSaveStatusPayload(payload) === false
    ) {
      return undefined;
    }

    return {
      version: ProtocolVersion,
      type: value.type,
      payload,
    };
  }

  if (value.type === "saved-tabs-snapshot") {
    if (value.sessionId !== undefined || isSavedTabsSnapshotPayload(payload) === false) {
      return undefined;
    }
    return { version: ProtocolVersion, type: value.type, payload };
  }

  if (value.type === "saved-tab-operation-result") {
    if (value.sessionId !== undefined || isSavedTabOperationResultPayload(payload) === false) {
      return undefined;
    }
    return { version: ProtocolVersion, type: value.type, payload };
  }

  if (value.type === "saved-tab-launch-result") {
    if (value.sessionId !== undefined || isSavedTabLaunchResultPayload(payload) === false) {
      return undefined;
    }
    return { version: ProtocolVersion, type: value.type, payload };
  }

  if (value.type === "launch-profiles-result") {
    if (value.sessionId !== undefined || isLaunchProfilesPayload(payload) === false) {
      return undefined;
    }
    return { version: ProtocolVersion, type: value.type, payload };
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

function postPathDrop(entry: SessionEntry, files: File[]): boolean {
  const webview = window.chrome?.webview;
  if (webview === undefined || typeof webview.postMessageWithAdditionalObjects !== "function") {
    return false;
  }

  webview.postMessageWithAdditionalObjects({
    version: ProtocolVersion,
    type: "drop-paths",
    sessionId: entry.id,
    payload: {
      rendererInstanceId: RendererInstanceId,
      sessionGeneration: entry.sessionGeneration,
    },
  }, files);
  return true;
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

function createTerminal(sessionId: string): Terminal {
  const rootStyle = getComputedStyle(document.documentElement);
  return new Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: reducedMotion.matches === false,
    cursorStyle: "block",
    fontFamily: rootStyle.getPropertyValue("--font-mono").trim(),
    fontSize: Number.parseFloat(rootStyle.getPropertyValue("--font-size-terminal")),
    lineHeight: Number.parseFloat(rootStyle.getPropertyValue("--line-height-terminal")),
    linkHandler: {
      activate: (event, targetUrl) => requestUrlOpen(event, sessionId, targetUrl),
      allowNonHttpProtocols: false,
    },
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

  const changeDirectoryButton = document.createElement("button");
  changeDirectoryButton.className = "session-recovery";
  changeDirectoryButton.type = "button";
  changeDirectoryButton.textContent = "폴더 변경";
  changeDirectoryButton.addEventListener("click", () => showStartingDirectoryEditor(sessionId));

  const homeDirectoryButton = document.createElement("button");
  homeDirectoryButton.className = "session-recovery";
  homeDirectoryButton.type = "button";
  homeDirectoryButton.textContent = "홈에서 다시 시도";
  homeDirectoryButton.addEventListener("click", () => {
    postSession("set-starting-directory", sessionId, {
      startingDirectory: sessions.get(sessionId)?.homeDirectory ?? payload.homeDirectory,
    });
    requestedTerminalFocusSessionId = sessionId;
    postSession("restart-session", sessionId);
  });
  status.append(statusMessage, changeDirectoryButton, homeDirectoryButton, restartButton);
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
  tabButton.addEventListener("contextmenu", (event) => showTabMenu(event, sessionId));
  tabButton.addEventListener("dblclick", () => startRename(sessionId));

  const tabStatus = document.createElement("span");
  tabStatus.className = "tab-status";
  tabStatus.setAttribute("aria-hidden", "true");

  const newOutputIndicator = document.createElement("span");
  newOutputIndicator.className = "tab-new-output";
  newOutputIndicator.setAttribute("role", "img");
  newOutputIndicator.setAttribute("aria-label", "새 출력 있음");
  newOutputIndicator.setAttribute("aria-hidden", "true");

  const tabLabel = document.createElement("span");
  tabLabel.className = "tab-label";
  tabButton.append(tabStatus, tabLabel, newOutputIndicator);

  const closeButton = document.createElement("button");
  closeButton.className = "tab-close";
  closeButton.type = "button";
  closeButton.setAttribute("aria-label", `${payload.name} 탭 닫기`);
  closeButton.title = "탭 닫기";
  closeButton.textContent = "×";
  closeButton.addEventListener("click", () => {
    focusTerminalOnNextActivation = true;
    postSession("close-session", sessionId);
  });

  tabItem.append(tabButton, closeButton);

  const terminal = createTerminal(sessionId);
  const fitAddon = new FitAddon();
  const searchAddon = new SearchAddon();
  const webLinksAddon = new WebLinksAddon(
    (event, targetUrl) => requestUrlOpen(event, sessionId, targetUrl),
  );
  terminal.loadAddon(fitAddon);
  terminal.loadAddon(searchAddon);
  terminal.loadAddon(webLinksAddon);
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
      event.code === "KeyF"
    ) {
      showSearch(sessionId);
      return false;
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
    sessionGeneration: payload.sessionGeneration,
    startingDirectory: payload.startingDirectory,
    homeDirectory: payload.homeDirectory,
    terminal,
    fitAddon,
    searchAddon,
    pane,
    mount,
    status,
    statusMessage,
    restartButton,
    changeDirectoryButton,
    homeDirectoryButton,
    tabItem,
    tabButton,
    tabLabel,
    tabStatus,
    newOutputIndicator,
    closeButton,
    hasNewOutput: false,
    lastColumns: 0,
    lastRows: 0,
    fitErrorReported: false,
  };

  searchAddon.onDidChangeResults((result) => {
    if (searchOverlay?.sessionId === sessionId) {
      updateSearchStatus(result.resultIndex, result.resultCount);
    }
  });

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
  if (event.key === "F10" && event.shiftKey === true) {
    event.preventDefault();
    const tabButton = sessions.get(sessionId)?.tabButton;
    if (tabButton !== undefined) {
      showTabMenuAt(tabButton.getBoundingClientRect(), sessionId);
    }
    return;
  }

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

function startRename(sessionId: string): void {
  const entry = sessions.get(sessionId);
  if (entry === undefined) {
    return;
  }

  dismissTabMenu();
  const dialog = document.createElement("dialog");
  dialog.className = "tab-name-dialog";
  dialog.setAttribute("aria-labelledby", `tab-name-title-${sessionId}`);

  const title = document.createElement("h2");
  title.id = `tab-name-title-${sessionId}`;
  title.textContent = "탭 이름 변경";

  const label = document.createElement("label");
  label.textContent = "이름";
  const input = document.createElement("input");
  input.type = "text";
  input.value = entry.name;
  input.maxLength = 128;
  input.spellcheck = false;
  input.autocomplete = "off";
  label.append(input);

  const actions = document.createElement("div");
  actions.className = "tab-name-actions";
  const cancelButton = document.createElement("button");
  cancelButton.type = "button";
  cancelButton.textContent = "취소";
  cancelButton.addEventListener("click", () => dialog.close());
  const saveButton = document.createElement("button");
  saveButton.type = "button";
  saveButton.textContent = "저장";
  const save = (): void => {
    postSession("rename-session", sessionId, { name: input.value });
    dialog.close();
  };
  saveButton.addEventListener("click", save);

  let isComposing = false;
  input.addEventListener("compositionstart", () => {
    isComposing = true;
  });
  input.addEventListener("compositionend", () => {
    isComposing = false;
  });
  input.addEventListener("keydown", (event) => {
    if (event.isComposing === true || isComposing === true) {
      return;
    }

    if (event.key === "Enter") {
      event.preventDefault();
      save();
    } else if (event.key === "Escape") {
      event.preventDefault();
      dialog.close();
    }
  });

  actions.append(cancelButton, saveButton);
  dialog.append(title, label, actions);
  dialog.addEventListener("close", () => {
    dialog.remove();
    sessions.get(sessionId)?.tabButton.focus();
  });
  document.body.append(dialog);
  dialog.showModal();
  input.focus();
  input.select();
}

function createRequestId(): string {
  return crypto.randomUUID().replaceAll("-", "");
}

function showStartingDirectoryEditor(sessionId: string): void {
  const entry = sessions.get(sessionId);
  if (entry === undefined) {
    return;
  }

  dismissTabMenu();
  const dialog = document.createElement("dialog");
  dialog.className = "starting-directory-dialog";
  dialog.setAttribute("aria-labelledby", `starting-directory-title-${sessionId}`);

  const title = document.createElement("h2");
  title.id = `starting-directory-title-${sessionId}`;
  title.textContent = `${entry.name} 시작 폴더`;

  const label = document.createElement("label");
  label.textContent = "로컬 절대 경로";
  const input = document.createElement("input");
  input.type = "text";
  input.value = entry.startingDirectory;
  input.maxLength = 32_767;
  input.spellcheck = false;
  input.autocomplete = "off";
  label.append(input);

  const guidance = document.createElement("p");
  guidance.textContent = "다음 셸 시작부터 적용됩니다. 실행 중인 셸은 유지됩니다.";
  const validation = document.createElement("p");
  validation.className = "starting-directory-validation";
  validation.setAttribute("role", "alert");

  const actions = document.createElement("div");
  actions.className = "starting-directory-actions";
  const browseButton = document.createElement("button");
  browseButton.type = "button";
  browseButton.textContent = "폴더 선택…";
  browseButton.addEventListener("click", () => {
    postSession("set-starting-directory", sessionId, { startingDirectory: "" });
    dialog.close();
  });
  const cancelButton = document.createElement("button");
  cancelButton.type = "button";
  cancelButton.textContent = "취소";
  cancelButton.addEventListener("click", () => dialog.close());
  const saveButton = document.createElement("button");
  saveButton.type = "button";
  saveButton.textContent = "저장";
  const save = (): void => {
    const startingDirectory = input.value.trim();
    if (startingDirectory.length === 0) {
      validation.textContent = "시작 폴더 경로를 입력해 주세요.";
      input.focus();
      return;
    }

    postSession("set-starting-directory", sessionId, { startingDirectory });
    dialog.close();
  };
  saveButton.addEventListener("click", save);

  let isComposing = false;
  input.addEventListener("compositionstart", () => {
    isComposing = true;
  });
  input.addEventListener("compositionend", () => {
    isComposing = false;
  });
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && event.isComposing === false && isComposing === false) {
      event.preventDefault();
      save();
    } else if (event.key === "Escape") {
      event.preventDefault();
      dialog.close();
    }
  });
  actions.append(browseButton, cancelButton, saveButton);
  dialog.append(title, label, guidance, validation, actions);
  dialog.addEventListener("close", () => dialog.remove());
  document.body.append(dialog);
  dialog.showModal();
  input.focus();
  input.select();
}

function showTabMenu(event: MouseEvent, sessionId: string): void {
  event.preventDefault();
  showTabMenuAt({ left: event.clientX, bottom: event.clientY }, sessionId);
}

function showTabMenuAt(anchor: Pick<DOMRect, "left" | "bottom">, sessionId: string): void {
  dismissTabMenu();
  const entry = sessions.get(sessionId);
  const index = orderedSessions().findIndex((item) => item.id === sessionId);
  if (entry === undefined || index < 0) {
    return;
  }

  const menu = document.createElement("div");
  menu.className = "tab-context-menu";
  menu.setAttribute("role", "menu");
  menu.setAttribute("aria-label", `${entry.name} 탭 메뉴`);
  menu.style.left = `${anchor.left}px`;
  menu.style.top = `${anchor.bottom}px`;
  const addItem = (label: string, action: () => void, disabled = false): void => {
    const button = document.createElement("button");
    button.type = "button";
    button.setAttribute("role", "menuitem");
    button.textContent = label;
    button.disabled = disabled;
    button.addEventListener("click", () => {
      dismissTabMenu();
      action();
    });
    menu.append(button);
  };

  addItem("이름 변경", () => startRename(sessionId));
  addItem("왼쪽으로 이동", () => postSession("move-session", sessionId, { direction: "left" }), index === 0);
  addItem("오른쪽으로 이동", () => postSession("move-session", sessionId, { direction: "right" }), index === orderedSessions().length - 1);
  addItem("시작 폴더 설정", () => showStartingDirectoryEditor(sessionId));
  const duplicateLimitReached = canAddSession === false || orderedSessions().length >= MaximumTabs;
  addItem("이 탭 구성 복제", () => duplicateTab(sessionId), duplicateLimitReached);
  if (duplicateLimitReached === true) {
    const limit = document.createElement("p");
    limit.id = "tab-context-menu-duplicate-limit";
    limit.className = "tab-context-menu-limit";
    limit.setAttribute("role", "status");
    limit.textContent = `실행 탭 한도(${MaximumTabs}개)에 도달했습니다.`;
    menu.append(limit);
    const duplicateItem = menu.querySelector<HTMLButtonElement>("button:last-of-type");
    duplicateItem?.setAttribute("aria-label", `${duplicateItem.textContent} (실행 탭 한도에 도달하여 사용할 수 없음)`);
    duplicateItem?.setAttribute("aria-describedby", limit.id);
  }
  const savedTabLimitReached = savedTabsSnapshot !== undefined &&
    savedTabsSnapshot.tabs.length >= savedTabsSnapshot.maximumSavedTabs;
  addItem("저장한 탭에 추가…", () => {
    showSavedTabsDialog({
      name: entry.name,
      startingDirectory: entry.startingDirectory,
      shellKind: entry.shellKind,
    }, sessionId);
  }, savedTabLimitReached);
  if (savedTabLimitReached === true) {
    const limit = document.createElement("p");
    limit.id = "tab-context-menu-limit";
    limit.className = "tab-context-menu-limit";
    limit.setAttribute("role", "status");
    limit.textContent = `저장한 탭 한도(${savedTabsSnapshot?.maximumSavedTabs ?? 20}개)에 도달했습니다.`;
    menu.append(limit);
    const saveItem = menu.querySelector<HTMLButtonElement>("button:last-of-type");
    saveItem?.setAttribute("aria-label", `${saveItem.textContent} (저장한 탭 한도에 도달하여 사용할 수 없음)`);
    saveItem?.setAttribute("aria-describedby", limit.id);
  }
  document.body.append(menu);
  contextMenu = menu;
  contextMenuFocusReturnSessionId = sessionId;
  menu.querySelector<HTMLButtonElement>("button:not(:disabled)")?.focus();
}

function dismissTabMenu(): void {
  if (contextMenu === undefined) {
    return;
  }

  contextMenu.remove();
  contextMenu = undefined;
  contextMenuFocusReturnSessionId = undefined;
}

function shellLabel(shellKind: SavedTab["shellKind"]): string {
  return shellKind === "automatic" ? "자동" : shellKind;
}

function savedTabStatusLabel(status: string, failureMessage: string | null): string {
  if (failureMessage !== null && failureMessage.trim().length > 0) {
    return failureMessage;
  }
  const labels: Record<string, string> = {
    "saved-tab-limit-reached": "저장 탭 한도(20개)에 도달했습니다.",
    "running-tab-limit-reached": "실행 탭 한도(8개)에 도달했습니다.",
    "starting-directory-unavailable": "시작 폴더를 사용할 수 없습니다.",
    "shell-unavailable": "선택한 셸을 사용할 수 없습니다.",
    "saved-tab-not-found": "저장한 탭을 찾을 수 없습니다.",
    "invalid-name": "탭 이름을 확인해 주세요.",
    "invalid-starting-directory": "시작 폴더를 확인해 주세요.",
    "unsupported-shell": "지원하지 않는 셸입니다.",
    failed: "저장한 탭 작업에 실패했습니다.",
  };
  return labels[status] ?? "저장한 탭 작업에 실패했습니다.";
}

function dismissSavedTabsMenu(): void {
  if (savedTabsMenu !== undefined) {
    savedTabsMenu.remove();
    savedTabsMenu = undefined;
  }
  savedTabsButton.setAttribute("aria-expanded", "false");
}

function profileLaunchKey(sessionId: string, profileId: string): string {
  return `${sessionId}:${profileId}`;
}

function launchProfile(profile: LaunchProfile): void {
  const source = activeSessionId === undefined ? undefined : sessions.get(activeSessionId);
  if (source === undefined || canAddSession === false) {
    return;
  }

  const key = profileLaunchKey(source.id, profile.profileId);
  if (pendingProfileLaunches.has(key) === true) {
    return;
  }

  pendingProfileLaunches.add(key);
  focusTerminalOnNextActivation = true;
  postSession("new-tab", source.id, {
    requestId: createRequestId(),
    sessionGeneration: source.sessionGeneration,
    profileId: profile.profileId,
  });
  dismissSavedTabsMenu();
}

function launchDefaultProfile(): void {
  const source = activeSessionId === undefined ? undefined : sessions.get(activeSessionId);
  if (source === undefined) {
    return;
  }

  const profile = launchProfiles?.profiles.find((candidate) =>
    candidate.kind === "built-in-shell" && candidate.shellKind === source.shellKind,
  ) ?? launchProfiles?.profiles.find((candidate) => candidate.kind === "built-in-shell");
  if (profile !== undefined) {
    launchProfile(profile);
  }
}

function duplicateTab(sessionId: string): void {
  const source = sessions.get(sessionId);
  if (source === undefined || canAddSession === false) {
    return;
  }

  const key = `${source.id}:${source.sessionGeneration}`;
  if (pendingTabDuplicates.has(key) === true) {
    return;
  }

  pendingTabDuplicates.add(key);
  focusTerminalOnNextActivation = true;
  postSession("duplicate-tab", source.id, {
    requestId: createRequestId(),
    sessionGeneration: source.sessionGeneration,
  });
}

function appendLaunchProfilesMenu(menu: HTMLElement): void {
  const heading = document.createElement("p");
  heading.className = "saved-tabs-section-heading";
  heading.textContent = "새 탭";
  menu.append(heading);

  const tabLimitReached = canAddSession === false || sessions.size >= MaximumTabs;
  let limitId: string | undefined;
  if (tabLimitReached === true) {
    const limit = document.createElement("p");
    limitId = "launch-profile-tab-limit";
    limit.id = limitId;
    limit.className = "saved-tabs-limit";
    limit.setAttribute("role", "status");
    limit.textContent = `실행 탭 한도(${MaximumTabs}개)에 도달했습니다.`;
    menu.append(limit);
  }

  for (const profile of launchProfiles?.profiles ?? []) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "launch-profile";
    button.setAttribute("role", "menuitem");
    button.textContent = profile.displayName;
    button.disabled = tabLimitReached;
    if (limitId !== undefined) {
      button.setAttribute("aria-label", `${profile.displayName} (실행 탭 한도에 도달하여 사용할 수 없음)`);
      button.setAttribute("aria-describedby", limitId);
    }
    button.addEventListener("click", () => launchProfile(profile));
    menu.append(button);
  }

  if (launchProfiles?.status === "wsl-discovery-failed") {
    const failure = document.createElement("p");
    failure.className = "saved-tabs-feedback is-error";
    failure.setAttribute("role", "status");
    failure.textContent = launchProfiles.failureMessage ?? "WSL 목록을 불러오지 못했습니다.";
    const retry = document.createElement("button");
    retry.type = "button";
    retry.className = "launch-profile-retry";
    retry.setAttribute("role", "menuitem");
    retry.textContent = "WSL 목록 다시 시도";
    retry.addEventListener("click", () => postGlobal("retry-launch-profiles"));
    menu.append(failure, retry);
  }

  const separator = document.createElement("div");
  separator.className = "saved-tabs-menu-separator";
  separator.setAttribute("role", "separator");
  menu.append(separator);
}

function launchSavedTab(tab: SavedTab): void {
  if (savedTabsSnapshot === undefined || savedTabsSnapshot.runningTabCount >= savedTabsSnapshot.maximumRunningTabs) {
    savedTabsFeedback = "실행 탭 한도(8개)에 도달했습니다.";
    renderSavedTabsDialog();
    return;
  }

  const requestId = createRequestId();
  pendingSavedTabLaunchRequestId = requestId;
  savedTabsFeedback = `${tab.name} 탭을 시작하는 중입니다.`;
  postGlobal("launch-saved-tab", { requestId, savedTabId: tab.savedTabId });
  dismissSavedTabsMenu();
  renderSavedTabsDialog();
}

function showSavedTabsMenu(): void {
  dismissSavedTabsMenu();
  const menu = document.createElement("div");
  menu.className = "saved-tabs-menu";
  menu.setAttribute("role", "menu");
  menu.setAttribute("aria-label", "저장한 탭 메뉴");
  const anchor = savedTabsButton.getBoundingClientRect();
  menu.style.right = `${Math.max(8, window.innerWidth - anchor.right)}px`;
  menu.style.top = `${anchor.bottom}px`;

  appendLaunchProfilesMenu(menu);

  const snapshot = savedTabsSnapshot;
  const savedHeading = document.createElement("p");
  savedHeading.className = "saved-tabs-section-heading";
  savedHeading.textContent = "저장한 탭";
  menu.append(savedHeading);
  if (snapshot === undefined || snapshot.tabs.length === 0) {
    const empty = document.createElement("p");
    empty.className = "saved-tabs-empty";
    empty.textContent = "저장한 탭이 없습니다.";
    menu.append(empty);
  } else {
    for (const tab of snapshot.tabs) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "saved-tab-launch";
      button.setAttribute("role", "menuitem");
      button.title = `${tab.name}\n${tab.startingDirectory}`;
      button.disabled = snapshot.runningTabCount >= snapshot.maximumRunningTabs;
      const name = document.createElement("span");
      name.className = "saved-tab-launch-name";
      name.textContent = tab.name;
      const shell = document.createElement("span");
      shell.textContent = shellLabel(tab.shellKind);
      const path = document.createElement("span");
      path.className = "saved-tab-launch-path";
      path.textContent = tab.startingDirectory;
      button.append(name, shell, path);
      button.addEventListener("click", () => launchSavedTab(tab));
      menu.append(button);
    }
  }

  const limit = document.createElement("p");
  limit.className = "saved-tabs-limit";
  limit.textContent = `${snapshot?.tabs.length ?? 0}/20개 저장 · ${snapshot?.runningTabCount ?? sessions.size}/8개 실행`;
  menu.append(limit);
  const manage = document.createElement("button");
  manage.type = "button";
  manage.className = "saved-tabs-manage";
  manage.setAttribute("role", "menuitem");
  manage.textContent = "저장한 탭 관리…";
  manage.addEventListener("click", () => {
    dismissSavedTabsMenu();
    showSavedTabsDialog();
  });
  menu.append(manage);
  document.body.append(menu);
  savedTabsMenu = menu;
  savedTabsButton.setAttribute("aria-expanded", "true");
  menu.querySelector<HTMLButtonElement>("button:not(:disabled)")?.focus();
}

function appendSavedTabEditor(dialog: HTMLDialogElement, tab?: SavedTab, createDefault?: SavedTabDraft): void {
  const editor = document.createElement("form");
  editor.className = "saved-tab-editor";
  const heading = document.createElement("h3");
  heading.textContent = tab === undefined ? "새 저장 탭" : "저장 탭 편집";
  const name = document.createElement("input");
  name.type = "text";
  name.value = tab?.name ?? createDefault?.name ?? "";
  name.maxLength = 128;
  const path = document.createElement("input");
  path.type = "text";
  path.value = tab?.startingDirectory ?? createDefault?.startingDirectory ?? "";
  path.maxLength = 32_767;
  const shell = document.createElement("select");
  for (const value of ["automatic", "pwsh", "powershell", "cmd"] as const) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = shellLabel(value);
    option.selected = value === (tab?.shellKind ?? createDefault?.shellKind ?? "automatic");
    shell.append(option);
  }
  const addLabel = (text: string, control: HTMLElement): void => {
    const label = document.createElement("label");
    label.textContent = text;
    label.append(control);
    editor.append(label);
  };
  editor.append(heading);
  addLabel("이름", name);
  addLabel("시작 폴더", path);
  addLabel("셸", shell);
  if (createDefault !== undefined) {
    const guidance = document.createElement("p");
    guidance.className = "saved-tab-guidance";
    guidance.textContent = "현재 터미널에서 이동한 폴더와 다를 수 있습니다.";
    editor.append(guidance);
  }
  const actions = document.createElement("div");
  actions.className = "saved-tab-editor-actions";
  const cancel = document.createElement("button");
  cancel.type = "button";
  cancel.textContent = "취소";
  cancel.addEventListener("click", () => {
    if (savedTabsDialogFocusReturnSessionId !== undefined) {
      dialog.close();
      return;
    }

    renderSavedTabsDialog();
  });
  const save = document.createElement("button");
  save.type = "submit";
  save.className = "saved-tab-primary";
  save.textContent = tab === undefined ? "추가" : "저장";
  actions.append(cancel, save);
  editor.append(actions);
  editor.addEventListener("submit", (event) => {
    event.preventDefault();
    const requestId = createRequestId();
    const payload = { requestId, name: name.value.trim(), startingDirectory: path.value.trim(), shellKind: shell.value };
    if (tab === undefined) {
      postGlobal("create-saved-tab", payload);
    } else {
      postGlobal("update-saved-tab", { ...payload, savedTabId: tab.savedTabId });
    }
  });
  dialog.append(editor);
  name.focus();
}

function renderSavedTabsDialog(editTab?: SavedTab, create = false, createDefault?: SavedTabDraft): void {
  const dialog = savedTabsDialog;
  if (dialog === undefined) {
    return;
  }
  dialog.replaceChildren();
  const heading = document.createElement("h2");
  heading.textContent = "저장한 탭 관리";
  dialog.append(heading);
  const feedback = document.createElement("p");
  feedback.className = "saved-tabs-feedback";
  feedback.textContent = savedTabsFeedback;
  feedback.classList.toggle("is-error", savedTabsFeedback.length > 0 && pendingSavedTabLaunchRequestId === undefined);
  dialog.append(feedback);
  if (editTab !== undefined || create === true) {
    appendSavedTabEditor(dialog, editTab, createDefault);
    return;
  }
  const list = document.createElement("div");
  list.className = "saved-tabs-list";
  const snapshot = savedTabsSnapshot;
  if (snapshot === undefined || snapshot.tabs.length === 0) {
    const empty = document.createElement("p");
    empty.textContent = "저장한 탭이 없습니다. 새 탭을 추가해 보세요.";
    list.append(empty);
  } else {
    for (const tab of snapshot.tabs) {
      const item = document.createElement("div");
      item.className = "saved-tab-editor";
      const title = document.createElement("strong");
      title.textContent = tab.name;
      const details = document.createElement("p");
      details.textContent = `${tab.startingDirectory} · ${shellLabel(tab.shellKind)}`;
      const actions = document.createElement("div");
      actions.className = "saved-tab-editor-actions";
      const edit = document.createElement("button"); edit.type = "button"; edit.textContent = "편집";
      edit.addEventListener("click", () => renderSavedTabsDialog(tab));
      const remove = document.createElement("button"); remove.type = "button"; remove.textContent = "삭제";
      remove.addEventListener("click", () => postGlobal("delete-saved-tab", { requestId: createRequestId(), savedTabId: tab.savedTabId }));
      actions.append(edit, remove); item.append(title, details, actions); list.append(item);
    }
  }
  dialog.append(list);
  const actions = document.createElement("div"); actions.className = "saved-tabs-actions";
  const add = document.createElement("button"); add.type = "button"; add.className = "saved-tab-primary"; add.textContent = "새 저장 탭";
  add.disabled = snapshot !== undefined && snapshot.tabs.length >= snapshot.maximumSavedTabs;
  add.addEventListener("click", () => renderSavedTabsDialog(undefined, true));
  const close = document.createElement("button"); close.type = "button"; close.textContent = "닫기"; close.addEventListener("click", () => dialog.close());
  actions.append(add, close); dialog.append(actions);
}

function showSavedTabsDialog(createDefault?: SavedTabDraft, focusReturnSessionId?: string): void {
  if (savedTabsDialog !== undefined) { renderSavedTabsDialog(); return; }
  const dialog = document.createElement("dialog");
  dialog.className = "saved-tabs-dialog";
  dialog.addEventListener("cancel", (event) => { event.preventDefault(); dialog.close(); });
  dialog.addEventListener("close", () => {
    if (pendingSavedTabLaunchRequestId !== undefined) {
      postGlobal("cancel-saved-tab-launch", { requestId: pendingSavedTabLaunchRequestId });
      pendingSavedTabLaunchRequestId = undefined;
    }
    const returnSessionId = savedTabsDialogFocusReturnSessionId;
    savedTabsDialogFocusReturnSessionId = undefined;
    savedTabsDialog = undefined;
    dialog.remove();
    const returnTab = returnSessionId === undefined ? undefined : sessions.get(returnSessionId)?.tabButton;
    (returnTab ?? savedTabsButton).focus();
  });
  savedTabsDialogFocusReturnSessionId = focusReturnSessionId;
  savedTabsDialog = dialog;
  document.body.append(dialog);
  renderSavedTabsDialog(undefined, createDefault !== undefined, createDefault);
  dialog.showModal();
}

function isRequestIdentifier(value: unknown): value is string {
  return (
    typeof value === "string" &&
    SessionIdPattern.test(value) === true &&
    value.toLowerCase() !== EmptySessionId
  );
}

function isSessionGeneration(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) === true && value >= 1;
}

function isAllowedUrlTarget(value: unknown): value is string {
  if (typeof value !== "string" || value.length === 0 || value.length > MaximumUrlLength) {
    return false;
  }

  try {
    const parsed = new URL(value);
    return (
      (parsed.protocol === "http:" || parsed.protocol === "https:") &&
      parsed.hostname.length > 0 &&
      parsed.username.length === 0 &&
      parsed.password.length === 0
    );
  } catch {
    return false;
  }
}

function requestUrlOpen(event: MouseEvent, sessionId: string, targetUrl: string): void {
  if (
    event.button !== 0 ||
    event.ctrlKey !== true ||
    event.shiftKey === true ||
    event.altKey === true ||
    event.metaKey === true ||
    isAllowedUrlTarget(targetUrl) === false
  ) {
    return;
  }

  const entry = sessions.get(sessionId);
  if (
    entry === undefined ||
    activeSessionId !== sessionId ||
    entry.terminal.hasSelection() === true
  ) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  if (pendingConfirmation !== undefined) {
    showUrlOpenFeedback(entry, "먼저 열려 있는 확인 요청을 완료해 주세요.", true);
    return;
  }

  postSession("open-url-request", sessionId, {
    rendererInstanceId: RendererInstanceId,
    sessionGeneration: entry.sessionGeneration,
    activation: "ctrl-click",
    url: targetUrl,
  });
}

function parseConfirmationRequest(
  payload: Record<string, unknown>,
): ConfirmationRequestPayload | undefined {
  if (
    isRequestIdentifier(payload.requestId) === false ||
    isSessionGeneration(payload.sessionGeneration) === false ||
    (payload.kind !== "close" && payload.kind !== "paste" && payload.kind !== "path-drop" &&
      payload.kind !== "url-open") ||
    typeof payload.sessionName !== "string" ||
    payload.sessionName.trim().length === 0
  ) {
    return undefined;
  }

  if (
    payload.kind === "paste" &&
    (typeof payload.clipboardText !== "string" ||
      (payload.clipboardText.includes("\r") === false &&
        payload.clipboardText.includes("\n") === false))
  ) {
    return undefined;
  }

  if (
    payload.kind === "path-drop" &&
    (typeof payload.quotedInput !== "string" || payload.quotedInput.length === 0 ||
      payload.quotedInput.includes("\r") === true || payload.quotedInput.includes("\n") === true)
  ) {
    return undefined;
  }

  if (payload.kind === "url-open" && isAllowedUrlTarget(payload.targetUrl) === false) {
    return undefined;
  }

  return {
    requestId: payload.requestId.toLowerCase(),
    sessionGeneration: payload.sessionGeneration,
    kind: payload.kind,
    sessionName: payload.sessionName,
    clipboardText: payload.kind === "paste" ? payload.clipboardText : undefined,
    quotedInput: payload.kind === "path-drop" ? payload.quotedInput : undefined,
    targetUrl: payload.kind === "url-open" ? payload.targetUrl : undefined,
  };
}

function parseConfirmationCancel(
  payload: Record<string, unknown>,
): ConfirmationCancelPayload | undefined {
  if (
    isRequestIdentifier(payload.requestId) === false ||
    isSessionGeneration(payload.sessionGeneration) === false
  ) {
    return undefined;
  }

  return {
    requestId: payload.requestId.toLowerCase(),
    sessionGeneration: payload.sessionGeneration,
  };
}

function isNewOutputStatePayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & NewOutputStatePayload {
  return (
    isSessionGeneration(payload.sessionGeneration) === true &&
    typeof payload.hasNewOutput === "boolean"
  );
}

function formatPastePreview(text: string): { text: string; lineBreakCount: number } {
  let lineBreakCount = 0;
  const visibleText = text.replace(/\r\n|\r|\n/g, () => {
    lineBreakCount += 1;
    return "↵\n";
  });
  return { text: visibleText, lineBreakCount };
}

function showPathDropFeedback(entry: SessionEntry, message: string, isError: boolean): void {
  entry.pane.querySelector(".path-drop-feedback")?.remove();
  const feedback = document.createElement("div");
  feedback.className = "path-drop-feedback";
  feedback.classList.toggle("is-error", isError);
  feedback.setAttribute("role", isError === true ? "alert" : "status");
  feedback.setAttribute("aria-live", isError === true ? "assertive" : "polite");
  feedback.textContent = message;
  entry.pane.append(feedback);
  window.setTimeout(() => {
    if (feedback.isConnected === true) {
      feedback.remove();
    }
  }, 6_000);
}

function showUrlOpenFeedback(entry: SessionEntry, message: string, isError: boolean): void {
  entry.pane.querySelector(".url-open-feedback")?.remove();
  const feedback = document.createElement("div");
  feedback.className = "url-open-feedback";
  feedback.classList.toggle("is-error", isError);
  feedback.setAttribute("role", isError === true ? "alert" : "status");
  feedback.setAttribute("aria-live", isError === true ? "assertive" : "polite");
  feedback.textContent = message;
  entry.pane.append(feedback);
  window.setTimeout(() => {
    if (feedback.isConnected === true) {
      feedback.remove();
    }
  }, 6_000);
}

function restoreTerminalFocus(sessionId: string): void {
  const entry = sessions.get(sessionId);
  if (
    entry !== undefined &&
    activeSessionId === sessionId &&
    document.hasFocus() === true
  ) {
    entry.terminal.focus();
  }
}

function dismissConfirmation(restoreFocus: boolean): void {
  const pending = pendingConfirmation;
  if (pending === undefined) {
    return;
  }

  pendingConfirmation = undefined;
  if (pending.preview !== undefined) {
    pending.preview.textContent = "";
  }
  pending.dialog.close();
  pending.dialog.remove();
  if (restoreFocus === true) {
    restoreTerminalFocus(pending.sessionId);
  }
}

function completeConfirmation(
  result: "confirmed" | "cancelled",
  keyboardCode?: string,
): void {
  const pending = pendingConfirmation;
  if (pending === undefined) {
    return;
  }

  if (keyboardCode !== undefined) {
    suppressedConfirmationKeys.add(keyboardCode);
  }
  postSession("confirmation-response", pending.sessionId, {
    requestId: pending.requestId,
    sessionGeneration: pending.sessionGeneration,
    result,
  });
  dismissConfirmation(true);
}

function showConfirmation(sessionId: string, request: ConfirmationRequestPayload): void {
  if (sessions.has(sessionId) === false) {
    return;
  }

  if (pendingConfirmation !== undefined) {
    const isDuplicate =
      pendingConfirmation.sessionId === sessionId &&
      pendingConfirmation.requestId === request.requestId &&
      pendingConfirmation.sessionGeneration === request.sessionGeneration;
    if (isDuplicate === true) {
      return;
    }
    completeConfirmation("cancelled");
  }

  dismissTabMenu();
  const dialog = document.createElement("dialog");
  dialog.className = "confirmation-dialog";
  dialog.dataset.kind = request.kind;

  const title = document.createElement("h2");
  title.id = `confirmation-title-${request.requestId}`;
  title.textContent = request.kind === "close"
    ? `${request.sessionName} 탭 닫기`
    : request.kind === "paste"
      ? "여러 줄 붙여넣기"
      : request.kind === "path-drop" ? "경로 입력에 넣기" : "웹 주소 열기";
  dialog.setAttribute("aria-labelledby", title.id);

  const guidance = document.createElement("p");
  guidance.className = "confirmation-guidance";
  guidance.textContent =
    request.kind === "close"
      ? "이 탭의 터미널과 실행 중인 작업이 종료됩니다."
      : request.kind === "paste"
        ? `${request.sessionName} 탭에 여러 줄을 붙여넣으면 명령이 바로 실행될 수 있습니다. 내용을 확인하세요.`
        : request.kind === "path-drop"
          ? `${request.sessionName} 탭의 현재 입력 위치에 인용한 경로만 넣습니다. shell prompt가 맞는지 확인하세요. Enter는 추가하지 않습니다.`
          : "표시된 실제 주소를 Windows 기본 브라우저로 전달합니다. Starboard는 링크 내용을 미리 불러오지 않습니다.";

  let preview: HTMLElement | undefined;
  let previewSummary: HTMLElement | undefined;
  if (request.kind === "paste" && request.clipboardText !== undefined) {
    const formatted = formatPastePreview(request.clipboardText);
    previewSummary = document.createElement("p");
    previewSummary.className = "confirmation-preview-summary";
    previewSummary.textContent = `줄바꿈 ${formatted.lineBreakCount}개 · ${request.clipboardText.length}자`;

    preview = document.createElement("pre");
    preview.className = "confirmation-preview";
    preview.tabIndex = 0;
    preview.setAttribute("role", "textbox");
    preview.setAttribute("aria-readonly", "true");
    preview.setAttribute("aria-label", "붙여넣을 내용, 읽기 전용. 줄바꿈은 ↵로 표시됩니다.");
    preview.textContent = formatted.text;
  } else if (request.kind === "path-drop" && request.quotedInput !== undefined) {
    previewSummary = document.createElement("p");
    previewSummary.className = "confirmation-preview-summary";
    previewSummary.textContent = `${request.quotedInput.length}자 · 줄바꿈 없음 · 자동 실행 안 함`;

    preview = document.createElement("pre");
    preview.className = "confirmation-preview";
    preview.tabIndex = 0;
    preview.setAttribute("role", "textbox");
    preview.setAttribute("aria-readonly", "true");
    preview.setAttribute("aria-label", "shell 입력에 넣을 인용된 경로, 읽기 전용");
    preview.textContent = request.quotedInput;
  } else if (request.kind === "url-open" && request.targetUrl !== undefined) {
    previewSummary = document.createElement("p");
    previewSummary.className = "confirmation-preview-summary";
    previewSummary.textContent = "HTTP/HTTPS 주소 · 자동 미리보기 없음";

    preview = document.createElement("pre");
    preview.className = "confirmation-preview";
    preview.tabIndex = 0;
    preview.setAttribute("role", "textbox");
    preview.setAttribute("aria-readonly", "true");
    preview.setAttribute("aria-label", "브라우저에서 열 실제 웹 주소, 읽기 전용");
    preview.textContent = request.targetUrl;
  }

  const actions = document.createElement("div");
  actions.className = "confirmation-actions";
  const cancelButton = document.createElement("button");
  cancelButton.type = "button";
  cancelButton.autofocus = true;
  cancelButton.textContent = "취소";
  cancelButton.addEventListener("click", () => completeConfirmation("cancelled"));
  const confirmButton = document.createElement("button");
  confirmButton.type = "button";
  confirmButton.className = "confirmation-primary";
  confirmButton.textContent = request.kind === "close"
    ? "탭 닫기"
    : request.kind === "paste"
      ? "붙여넣기"
      : request.kind === "path-drop" ? "입력에 넣기" : "브라우저에서 열기";
  confirmButton.addEventListener("click", () => completeConfirmation("confirmed"));
  actions.append(cancelButton, confirmButton);

  dialog.append(title, guidance);
  if (previewSummary !== undefined && preview !== undefined) {
    dialog.append(previewSummary, preview);
  }
  dialog.append(actions);

  let isComposing = false;
  dialog.addEventListener("compositionstart", () => {
    isComposing = true;
  }, true);
  dialog.addEventListener("compositionend", () => {
    isComposing = false;
  }, true);
  dialog.addEventListener("keydown", (event) => {
    if (event.key !== "Enter" && event.key !== "Escape") {
      return;
    }

    event.preventDefault();
    event.stopImmediatePropagation();
    if (event.isComposing === true || isComposing === true || event.repeat === true) {
      return;
    }

    if (event.key === "Escape") {
      completeConfirmation("cancelled", event.code);
      return;
    }

    const result = document.activeElement === confirmButton ? "confirmed" : "cancelled";
    completeConfirmation(result, event.code);
  }, true);
  dialog.addEventListener("cancel", (event) => {
    event.preventDefault();
    completeConfirmation("cancelled", "Escape");
  });
  dialog.addEventListener("close", () => {
    if (pendingConfirmation?.dialog === dialog) {
      completeConfirmation("cancelled");
    }
  });

  pendingConfirmation = {
    ...request,
    sessionId,
    dialog,
    preview,
  };
  document.body.append(dialog);
  dialog.showModal();
  cancelButton.focus();
}

function setNewOutputState(entry: SessionEntry, hasNewOutput: boolean): void {
  entry.hasNewOutput = hasNewOutput;
  renderTab(entry);
}

function renderTab(entry: SessionEntry): void {
  const label = stateLabel(entry);
  const isActive = entry.id === activeSessionId;
  entry.tabItem.dataset.state = entry.state;
  entry.tabItem.dataset.selected = isActive.toString();
  entry.tabButton.setAttribute("aria-selected", isActive.toString());
  entry.tabButton.setAttribute(
    "aria-label",
    `${entry.name}, ${label}${entry.hasNewOutput === true ? ", 새 출력 있음" : ""}`,
  );
  entry.tabButton.setAttribute(
    "aria-busy",
    (entry.state === "starting" || entry.state === "restarting").toString(),
  );
  entry.tabButton.tabIndex = isActive ? 0 : -1;
  entry.tabButton.title = entry.name;
  entry.tabLabel.textContent = entry.name;
  entry.newOutputIndicator.dataset.visible = entry.hasNewOutput.toString();
  entry.newOutputIndicator.setAttribute("aria-hidden", (!entry.hasNewOutput).toString());
  entry.newOutputIndicator.title = entry.hasNewOutput === true ? "새 출력 있음" : "";
  entry.closeButton.setAttribute("aria-label", `${entry.name} 탭 닫기`);
  entry.restartButton.setAttribute("aria-label", `${entry.name} shell 다시 시작`);
  entry.changeDirectoryButton.setAttribute("aria-label", `${entry.name} 시작 폴더 변경`);
  entry.homeDirectoryButton.setAttribute("aria-label", `${entry.name} 홈 폴더에서 다시 시도`);
}

function renderTabs(): void {
  for (const entry of orderedSessions()) {
    renderTab(entry);
    tabList.append(entry.tabItem);
  }

  const tabLimitReached = canAddSession === false || sessions.size >= MaximumTabs;
  newTabButton.disabled = tabLimitReached;
  newTabButton.setAttribute("aria-label", tabLimitReached === true
    ? `새 terminal 탭 (실행 탭 한도 ${MaximumTabs}개에 도달하여 사용할 수 없음)`
    : "새 terminal 탭");
}

function updateSessionStatus(entry: SessionEntry): void {
  const isLoading = entry.state === "starting" || entry.state === "restarting";
  const isError =
    entry.state === "exited" || entry.state === "failed" || entry.errorMessage !== undefined;

  entry.status.classList.toggle("is-loading", isLoading);
  entry.status.classList.toggle("is-error", isError);
  entry.restartButton.hidden = isError === false;
  entry.changeDirectoryButton.hidden = isError === false;
  entry.homeDirectoryButton.hidden = isError === false;
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
  entry.sessionGeneration = payload.sessionGeneration;
  entry.startingDirectory = payload.startingDirectory;
  entry.homeDirectory = payload.homeDirectory;
  entry.shellKind = payload.shellKind;
  if (
    payload.state === "running" ||
    payload.state === "starting" ||
    payload.state === "restarting"
  ) {
    entry.errorMessage = undefined;
  }

  canAddSession = payload.canAddSession;
  entry.mount.setAttribute("aria-label", `${payload.name} terminal`);
  entry.closeButton.setAttribute("aria-label", `${payload.name} 탭 닫기`);
  updateSessionStatus(entry);
  renderTabs();
}

function showSearch(sessionId: string): void {
  const entry = sessions.get(sessionId);
  if (entry === undefined || activeSessionId !== sessionId) {
    return;
  }

  if (searchOverlay?.sessionId === sessionId) {
    searchOverlay.input.focus();
    searchOverlay.input.select();
    return;
  }

  closeSearch(false);
  const element = document.createElement("section");
  element.className = "terminal-search";
  element.setAttribute("role", "search");
  element.setAttribute("aria-label", "현재 탭 출력 검색");

  const input = document.createElement("input");
  input.type = "search";
  input.className = "terminal-search-input";
  input.placeholder = "출력 검색";
  input.setAttribute("aria-label", "현재 탭 출력 검색");
  input.autocomplete = "off";
  input.spellcheck = false;

  const previousButton = document.createElement("button");
  previousButton.type = "button";
  previousButton.className = "terminal-search-button";
  previousButton.textContent = "↑";
  previousButton.title = "이전 결과 (Shift+Enter)";
  previousButton.setAttribute("aria-label", "이전 결과");

  const nextButton = document.createElement("button");
  nextButton.type = "button";
  nextButton.className = "terminal-search-button";
  nextButton.textContent = "↓";
  nextButton.title = "다음 결과 (Enter)";
  nextButton.setAttribute("aria-label", "다음 결과");

  const status = document.createElement("span");
  status.className = "terminal-search-status";
  status.setAttribute("role", "status");
  status.setAttribute("aria-live", "polite");
  status.textContent = "검색어 입력";

  const closeButton = document.createElement("button");
  closeButton.type = "button";
  closeButton.className = "terminal-search-button";
  closeButton.textContent = "×";
  closeButton.title = "검색 닫기 (Escape)";
  closeButton.setAttribute("aria-label", "검색 닫기");

  previousButton.addEventListener("click", () => findSearchResult(false));
  nextButton.addEventListener("click", () => findSearchResult(true));
  closeButton.addEventListener("click", () => closeSearch(true));
  input.addEventListener("input", () => findSearchResult(true, true));
  input.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      closeSearch(true);
      return;
    }
    if (event.key === "Enter") {
      event.preventDefault();
      event.stopPropagation();
      findSearchResult(event.shiftKey === false);
    }
  });
  element.append(input, previousButton, nextButton, status, closeButton);
  entry.pane.append(element);
  searchOverlay = { sessionId, element, input, status };
  input.focus();
}

function findSearchResult(forward: boolean, incremental = false): void {
  if (searchOverlay === undefined) {
    return;
  }

  const entry = sessions.get(searchOverlay.sessionId);
  const term = searchOverlay.input.value;
  if (entry === undefined || term.length === 0) {
    entry?.searchAddon.clearDecorations();
    if (searchOverlay !== undefined) {
      searchOverlay.status.textContent = "검색어 입력";
    }
    return;
  }

  const searchOptions = createSearchOptions(incremental);
  const found = forward === true
    ? entry.searchAddon.findNext(term, searchOptions)
    : entry.searchAddon.findPrevious(term, searchOptions);
  if (found === false && searchOverlay !== undefined) {
    searchOverlay.status.textContent = "결과 없음";
  }
}

function createSearchOptions(incremental: boolean): ISearchOptions {
  const accent = toSearchColor(currentAppearance?.theme.accent);
  const canvas = toSearchColor(currentAppearance?.theme.canvas);
  const selection = toSearchColor(currentAppearance?.theme.selection);
  return {
    incremental,
    decorations: {
      matchBackground: selection,
      matchBorder: accent,
      matchOverviewRuler: accent,
      activeMatchBackground: accent,
      activeMatchBorder: canvas,
      activeMatchColorOverviewRuler: accent,
    },
  };
}

function toSearchColor(value: string | undefined): string {
  if (/^#[0-9a-f]{6}$/i.test(value ?? "") === true) {
    return value!;
  }

  return "#5ea8ff";
}

function updateSearchStatus(resultIndex: number, resultCount: number): void {
  if (searchOverlay === undefined) {
    return;
  }

  searchOverlay.status.textContent = resultCount === 0 || resultIndex < 0
    ? "결과 없음"
    : `${resultIndex + 1}/${resultCount}`;
}

function closeSearch(restoreTerminalFocus: boolean): void {
  if (searchOverlay === undefined) {
    return;
  }

  const closedSearch = searchOverlay;
  searchOverlay = undefined;
  const entry = sessions.get(closedSearch.sessionId);
  entry?.searchAddon.clearDecorations();
  closedSearch.element.remove();
  if (restoreTerminalFocus === true && entry !== undefined && activeSessionId === entry.id) {
    entry.terminal.focus();
  }
}

function activateSession(sessionId: string): void {
  const nextEntry = sessions.get(sessionId);
  if (nextEntry === undefined) {
    return;
  }

  if (pendingConfirmation !== undefined && pendingConfirmation.sessionId !== sessionId) {
    completeConfirmation("cancelled");
  }
  if (searchOverlay?.sessionId !== sessionId) {
    closeSearch(false);
  }

  activeSessionId = sessionId;
  setNewOutputState(nextEntry, false);
  for (const entry of sessions.values()) {
    const isActive = entry.id === sessionId;
    entry.pane.hidden = isActive === false;
    entry.tabItem.dataset.selected = isActive.toString();
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
  if (searchOverlay?.sessionId === sessionId) {
    closeSearch(false);
  }
  if (pendingConfirmation?.sessionId === sessionId) {
    dismissConfirmation(false);
  }
  if (contextMenu?.getAttribute("aria-label") === `${entry.name} 탭 메뉴`) {
    dismissTabMenu();
  }
  entry.searchAddon.dispose();
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

function isWorkspaceSaveStatusPayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & WorkspaceSaveStatusPayload {
  return (
    (payload.state === "saved" ||
      payload.state === "failed" ||
      payload.state === "deleted" ||
      payload.state === "disabled") &&
    (typeof payload.message === "string" || payload.message === null)
  );
}

function isSavedTab(value: unknown): value is SavedTab {
  return (
    isRecord(value) === true &&
    isRequestIdentifier(value.savedTabId) === true &&
    typeof value.name === "string" &&
    typeof value.startingDirectory === "string" &&
    (value.shellKind === "automatic" || value.shellKind === "pwsh" ||
      value.shellKind === "powershell" || value.shellKind === "cmd")
  );
}

function isSavedTabsSnapshotPayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & SavedTabsSnapshotPayload {
  return (
    Number.isInteger(payload.schemaVersion) === true &&
    typeof payload.maximumSavedTabs === "number" && payload.maximumSavedTabs === 20 &&
    typeof payload.runningTabCount === "number" && Number.isInteger(payload.runningTabCount) === true &&
    typeof payload.maximumRunningTabs === "number" && payload.maximumRunningTabs === 8 &&
    payload.runningTabCount >= 0 && payload.runningTabCount <= payload.maximumRunningTabs &&
    Array.isArray(payload.tabs) === true && payload.tabs.length <= payload.maximumSavedTabs &&
    payload.tabs.every(isSavedTab)
  );
}

function isSavedTabOperationResultPayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & SavedTabOperationResultPayload {
  return (
    isRequestIdentifier(payload.requestId) === true &&
    (payload.operation === "create" || payload.operation === "update" || payload.operation === "delete") &&
    typeof payload.status === "string" &&
    (typeof payload.failureMessage === "string" || payload.failureMessage === null) &&
    (payload.snapshot === null || (isRecord(payload.snapshot) === true && isSavedTabsSnapshotPayload(payload.snapshot) === true))
  );
}

function isSavedTabLaunchResultPayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & SavedTabLaunchResultPayload {
  return (
    isRequestIdentifier(payload.requestId) === true &&
    isRequestIdentifier(payload.savedTabId) === true &&
    typeof payload.status === "string" &&
    (typeof payload.sessionId === "string" || payload.sessionId === null) &&
    (typeof payload.sessionGeneration === "number" || payload.sessionGeneration === null) &&
    (typeof payload.failureMessage === "string" || payload.failureMessage === null) &&
    typeof payload.runningTabCount === "number" && Number.isInteger(payload.runningTabCount) === true &&
    typeof payload.maximumRunningTabs === "number" && payload.maximumRunningTabs === 8
  );
}

function isLaunchProfilesPayload(
  payload: Record<string, unknown>,
): payload is Record<string, unknown> & LaunchProfilesPayload {
  const statuses: LaunchProfilesPayload["status"][] = [
    "succeeded",
    "wsl-unavailable",
    "wsl-discovery-failed",
  ];
  if (
    typeof payload.status !== "string" ||
    statuses.includes(payload.status as LaunchProfilesPayload["status"]) === false ||
    Array.isArray(payload.profiles) === false ||
    (typeof payload.failureMessage !== "string" && payload.failureMessage !== null)
  ) {
    return false;
  }

  return payload.profiles.every((profile) => {
    if (isRecord(profile) === false) {
      return false;
    }

    return (
      typeof profile.profileId === "string" && profile.profileId.trim().length > 0 &&
      (profile.kind === "built-in-shell" || profile.kind === "wsl-distribution") &&
      typeof profile.displayName === "string" &&
      profile.displayName.trim().length > 0 &&
      (profile.shellKind === "pwsh" || profile.shellKind === "powershell" ||
        profile.shellKind === "cmd" || profile.shellKind === null) &&
      (typeof profile.wslDistributionName === "string" || profile.wslDistributionName === null)
    );
  });
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
    typeof payload.canAddSession === "boolean" &&
    isSessionGeneration(payload.sessionGeneration) === true &&
    typeof payload.startingDirectory === "string" &&
    typeof payload.homeDirectory === "string" &&
    (payload.shellKind === "automatic" || payload.shellKind === "pwsh" ||
      payload.shellKind === "powershell" || payload.shellKind === "cmd")
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

  if (message.type === "workspace-save-status") {
    // The status is global: it describes workspace persistence, not a live shell.
    // The tab-management UI added in W2 presents this value without changing a session.
    return;
  }

  if (message.type === "launch-profiles-result") {
    launchProfiles = message.payload;
    if (savedTabsMenu !== undefined) {
      showSavedTabsMenu();
    }
    return;
  }

  if (message.type === "saved-tabs-snapshot") {
    savedTabsSnapshot = message.payload;
    if (savedTabsDialogFocusReturnSessionId === undefined) {
      renderSavedTabsDialog();
    }
    return;
  }

  if (message.type === "saved-tab-operation-result") {
    if (message.payload.snapshot !== null) {
      savedTabsSnapshot = message.payload.snapshot;
    }
    savedTabsFeedback = message.payload.status === "succeeded" ? "저장했습니다." :
      savedTabStatusLabel(message.payload.status, message.payload.failureMessage);
    if (message.payload.operation === "create" && message.payload.status === "succeeded" &&
        savedTabsDialogFocusReturnSessionId !== undefined) {
      savedTabsDialog?.close();
      return;
    }
    if (message.payload.operation === "create" && savedTabsDialogFocusReturnSessionId !== undefined) {
      const entry = sessions.get(savedTabsDialogFocusReturnSessionId);
      if (entry !== undefined) {
        renderSavedTabsDialog(undefined, true, {
          name: entry.name,
          startingDirectory: entry.startingDirectory,
          shellKind: entry.shellKind,
        });
        return;
      }
    }
    renderSavedTabsDialog();
    return;
  }

  if (message.type === "saved-tab-launch-result") {
    if (pendingSavedTabLaunchRequestId === message.payload.requestId) {
      pendingSavedTabLaunchRequestId = undefined;
    }
    if (savedTabsSnapshot !== undefined) {
      savedTabsSnapshot = { ...savedTabsSnapshot, runningTabCount: message.payload.runningTabCount };
    }
    savedTabsFeedback = message.payload.status === "started" ? "탭을 시작했습니다." :
      savedTabStatusLabel(message.payload.status, message.payload.failureMessage);
    renderSavedTabsDialog();
    return;
  }

  const sessionId = message.sessionId;
  const payload = message.payload;

  if (message.type === "session-upsert") {
    if (isSessionPayload(payload) === true) {
      upsertSession(sessionId, payload);
      for (const key of pendingProfileLaunches) {
        if (key.startsWith(`${sessionId}:`) === false) {
          pendingProfileLaunches.delete(key);
        }
      }
      for (const key of pendingTabDuplicates) {
        if (key.startsWith(`${sessionId}:`) === false) {
          pendingTabDuplicates.delete(key);
        }
      }
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

  if (message.type === "confirmation-request") {
    const request = parseConfirmationRequest(payload);
    if (request !== undefined) {
      showConfirmation(sessionId, request);
    }
    return;
  }

  if (message.type === "confirmation-cancel") {
    const cancellation = parseConfirmationCancel(payload);
    if (
      cancellation !== undefined &&
      pendingConfirmation?.sessionId === sessionId &&
      pendingConfirmation.requestId === cancellation.requestId &&
      pendingConfirmation.sessionGeneration === cancellation.sessionGeneration
    ) {
      dismissConfirmation(true);
    }
    return;
  }

  if (message.type === "new-output-state") {
    if (isNewOutputStatePayload(payload) === true) {
      setNewOutputState(entry, activeSessionId === sessionId ? false : payload.hasNewOutput);
    }
    return;
  }

  if (message.type === "output" && typeof payload.data === "string") {
    entry.terminal.write(payload.data);
    if (payload.data.length > 0 && activeSessionId !== sessionId) {
      setNewOutputState(entry, true);
    }
    return;
  }

  if (message.type === "paste" && typeof payload.data === "string") {
    entry.terminal.paste(payload.data);
    return;
  }

  if (
    message.type === "path-drop-result" && payload.succeeded === false &&
    typeof payload.message === "string"
  ) {
    showPathDropFeedback(entry, payload.message, true);
    return;
  }

  if (
    message.type === "url-open-result" && payload.succeeded === false &&
    typeof payload.message === "string"
  ) {
    showUrlOpenFeedback(entry, payload.message, true);
    return;
  }

  if (message.type === "reset") {
    if (pendingConfirmation?.sessionId === sessionId) {
      completeConfirmation("cancelled");
    }
    entry.terminal.reset();
    if (searchOverlay?.sessionId === sessionId) {
      closeSearch(false);
    }
    setNewOutputState(entry, false);
    entry.errorMessage = undefined;
    updateSessionStatus(entry);
    return;
  }

  if (message.type === "session-error" && typeof payload.message === "string") {
    setSessionError(sessionId, payload.message);
  }
}

function handleApplicationShortcut(event: KeyboardEvent): void {
  if ((pendingConfirmation !== undefined || savedTabsDialog !== undefined) && event.ctrlKey === true) {
    event.preventDefault();
    event.stopPropagation();
    return;
  }

  if (
    event.type !== "keydown" ||
    event.ctrlKey === false ||
    event.altKey === true ||
    event.metaKey === true
  ) {
    return;
  }

  let handled = false;
  if (event.shiftKey === false && event.code === "KeyF" && activeSessionId !== undefined) {
    showSearch(activeSessionId);
    handled = true;
  } else if (event.shiftKey === true && event.code === "KeyT") {
    if (newTabButton.disabled === false) {
      launchDefaultProfile();
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
    launchDefaultProfile();
  }
});

savedTabsButton.addEventListener("click", () => {
  if (savedTabsMenu === undefined) {
    showSavedTabsMenu();
  } else {
    dismissSavedTabsMenu();
  }
});

reducedMotion.addEventListener("change", (event) => {
  for (const entry of sessions.values()) {
    entry.terminal.options.cursorBlink = event.matches === false;
  }
});

document.addEventListener("keydown", handleApplicationShortcut, true);
document.addEventListener("keydown", (event) => {
  if (suppressedConfirmationKeys.has(event.code) === true) {
    event.preventDefault();
    event.stopImmediatePropagation();
  }
}, true);
document.addEventListener("keyup", (event) => {
  suppressedConfirmationKeys.delete(event.code);
}, true);
document.addEventListener("pointerdown", (event) => {
  if (contextMenu !== undefined && contextMenu.contains(event.target as Node) === false) {
    dismissTabMenu();
  }
  if (savedTabsMenu !== undefined && savedTabsMenu.contains(event.target as Node) === false && event.target !== savedTabsButton) {
    dismissSavedTabsMenu();
  }
});
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && searchOverlay !== undefined) {
    event.preventDefault();
    event.stopImmediatePropagation();
    closeSearch(true);
    return;
  }
  if (event.key === "Escape" && contextMenu !== undefined) {
    event.preventDefault();
    const returnSessionId = contextMenuFocusReturnSessionId;
    dismissTabMenu();
    sessions.get(returnSessionId ?? "")?.tabButton.focus();
  }
  if (event.key === "Escape" && savedTabsMenu !== undefined) {
    event.preventDefault();
    dismissSavedTabsMenu();
    savedTabsButton.focus();
  }
});
window.chrome?.webview?.addEventListener("message", (event) => {
  handleHostMessage(event.data);
});

const resizeObserver = new ResizeObserver(() => fitActiveSession());
resizeObserver.observe(workspace);

window.addEventListener("error", () => postGlobal("renderer-error", { kind: "runtime" }));
window.addEventListener("unhandledrejection", () =>
  postGlobal("renderer-error", { kind: "unhandled-rejection" }),
);

document.addEventListener("dragover", (event) => {
  if (event.dataTransfer?.types.includes("Files") !== true) {
    return;
  }

  event.preventDefault();
  event.dataTransfer.dropEffect = "copy";
});
document.addEventListener("drop", (event) => {
  if (event.dataTransfer?.types.includes("Files") !== true) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  const entry = activeSessionId === undefined ? undefined : sessions.get(activeSessionId);
  if (entry === undefined) {
    return;
  }

  if (entry.state !== "running") {
    showPathDropFeedback(entry, "shell이 실행 중일 때만 경로를 넣을 수 있습니다.", true);
    return;
  }

  if (pendingConfirmation !== undefined) {
    showPathDropFeedback(entry, "먼저 열려 있는 확인 요청을 완료해 주세요.", true);
    return;
  }

  const files = Array.from(event.dataTransfer.files);
  if (files.length === 0 || postPathDrop(entry, files) === false) {
    showPathDropFeedback(entry, "로컬 파일이나 폴더 경로를 확인하지 못했습니다.", true);
  }
});

postGlobal("ready", { rendererInstanceId: RendererInstanceId });
