import { FitAddon } from "@xterm/addon-fit";
import { Terminal, type ITheme } from "@xterm/xterm";
import "@xterm/xterm/css/xterm.css";
import "./styles.css";

declare global {
  interface Window {
    chrome?: {
      webview?: {
        addEventListener: (
          type: "message",
          listener: (event: MessageEvent<HostMessage>) => void,
        ) => void;
        postMessage: (message: HostMessage) => void;
      };
    };
  }
}

type HostMessage = {
  version: number;
  type: string;
  payload?: unknown;
};

type InitializePayload = {
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

type OutputPayload = {
  data: string;
};

const terminalElement = document.querySelector<HTMLElement>("#terminal");
if (terminalElement === null) {
  throw new Error("The terminal mount point is missing.");
}

const rootStyle = getComputedStyle(document.documentElement);
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const terminal = new Terminal({
  allowProposedApi: false,
  convertEol: false,
  cursorBlink: reducedMotion.matches === false,
  cursorStyle: "block",
  fontFamily: rootStyle.getPropertyValue("--font-mono").trim(),
  fontSize: Number.parseFloat(rootStyle.getPropertyValue("--font-size-terminal")),
  lineHeight: Number.parseFloat(rootStyle.getPropertyValue("--line-height-terminal")),
  scrollback: 10_000,
});
const fitAddon = new FitAddon();
terminal.loadAddon(fitAddon);
terminal.open(terminalElement);
reducedMotion.addEventListener("change", (event) => {
  terminal.options.cursorBlink = event.matches === false;
});

function post(type: string, payload: unknown = {}): void {
  window.chrome?.webview?.postMessage({ version: 1, type, payload });
}

function fitAndReport(): void {
  fitAddon.fit();
  post("resize", { columns: terminal.cols, rows: terminal.rows });
}

function applyOptions(payload: InitializePayload): void {
  const palette = payload.theme.ansiPalette;
  const theme: ITheme = {
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

  terminal.options.fontFamily = payload.fontFamily;
  terminal.options.fontSize = payload.fontSize;
  terminal.options.theme = theme;
  document.documentElement.style.setProperty("--color-canvas", payload.theme.canvas);
  document.documentElement.style.setProperty("--color-ink", payload.theme.foreground);
  document.documentElement.style.setProperty("--color-neutral", payload.theme.muted);
  document.documentElement.style.setProperty("--color-accent", payload.theme.accent);
  fitAndReport();
}

terminal.onData((data) => post("input", { data }));
terminal.attachCustomKeyEventHandler((event) => {
  if (event.type !== "keydown") {
    return true;
  }

  if (event.ctrlKey && event.code === "KeyC" && terminal.hasSelection()) {
    post("copy", { data: terminal.getSelection() });
    return false;
  }

  if (event.ctrlKey && event.code === "KeyV") {
    post("paste-request");
    return false;
  }

  return true;
});

terminalElement.addEventListener("pointerdown", () => terminal.focus());

window.chrome?.webview?.addEventListener("message", (event) => {
  const message = event.data;
  if (message.version !== 1) {
    return;
  }

  if (message.type === "initialize") {
    applyOptions(message.payload as InitializePayload);
    return;
  }

  if (message.type === "output") {
    const payload = message.payload as OutputPayload;
    terminal.write(payload.data);
    return;
  }

  if (message.type === "paste") {
    const payload = message.payload as OutputPayload;
    terminal.paste(payload.data);
    return;
  }

  if (message.type === "reset") {
    terminal.reset();
  }
});

const resizeObserver = new ResizeObserver(() => fitAndReport());
resizeObserver.observe(terminalElement);

window.addEventListener("error", () => post("renderer-error", { kind: "runtime" }));
window.addEventListener("unhandledrejection", () =>
  post("renderer-error", { kind: "unhandled-rejection" }),
);

fitAndReport();
post("ready");
