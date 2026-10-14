import type { SessionOrder } from "./workspaceSync";

type DragTab = { id: string; name: string; button: HTMLElement; item: HTMLElement };
type Insertion = { targetSessionId: string; position: "before" | "after"; x: number };
type Drag = {
  pointerId: number;
  source: DragTab;
  startOrder: SessionOrder[];
  startX: number;
  startY: number;
  x: number;
  y: number;
  started: boolean;
};

// Owns input and transient geometry only. The host remains the sole order authority.
export class TabDragReorder {
  private drag: Drag | undefined;
  private frame: number | undefined;
  private suppressUntil = 0;
  private disposed = false;
  private readonly marker = document.createElement("div");
  private readonly preview = document.createElement("div");

  constructor(private readonly list: HTMLElement,
              private readonly tabs: () => DragTab[],
              private readonly startOrder: () => SessionOrder[] | undefined,
              private readonly send: (source: SessionOrder, target: Insertion, order: SessionOrder[]) => void) {
    this.marker.className = "tab-insertion-marker";
    this.marker.hidden = true;
    this.marker.setAttribute("aria-hidden", "true");
    document.body.append(this.marker);
    this.preview.className = "tab-drag-preview";
    this.preview.hidden = true;
    this.preview.setAttribute("aria-hidden", "true");
    document.body.append(this.preview);
    list.addEventListener("pointerdown", this.pointerDown);
    document.addEventListener("click", this.suppressClick, true);
    document.addEventListener("dblclick", this.suppressClick, true);
    window.addEventListener("blur", this.cancel);
    window.addEventListener("resize", this.refresh);
    window.addEventListener("pagehide", this.dispose);
    document.addEventListener("visibilitychange", this.visibilityChanged);
  }

  observeOrder(order: readonly SessionOrder[] | undefined): void {
    if (this.drag !== undefined && (order === undefined ||
        this.drag.startOrder.length !== order.length ||
        this.drag.startOrder.some((session, index) => session.sessionId !== order[index].sessionId ||
          session.sessionGeneration !== order[index].sessionGeneration) === true)) {
      this.cancel();
    }
    if (this.drag?.started === true) {
      this.refresh();
    }
  }

  get hasPointerCapture(): boolean {
    return this.drag !== undefined && this.drag.source.button.hasPointerCapture(this.drag.pointerId) === true;
  }

  private readonly pointerDown = (event: PointerEvent): void => {
    if (this.disposed === true || this.drag !== undefined || event.button !== 0 ||
        event.pointerType !== "mouse" || event.isPrimary === false) {
      return;
    }
    const order = this.startOrder();
    const source = this.tabs().find((tab) => tab.button.contains(event.target as Node));
    if (source === undefined || order === undefined || order.length < 2) {
      return;
    }

    // Prevent button mousedown focus and native text drag, while retaining the normal click handler.
    event.preventDefault();
    this.drag = { pointerId: event.pointerId, source, startOrder: order,
      startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY, started: false };
    source.button.setPointerCapture(event.pointerId);
    source.button.addEventListener("lostpointercapture", this.pointerCancel);
    document.addEventListener("pointermove", this.pointerMove, true);
    document.addEventListener("pointerup", this.pointerUp, true);
    document.addEventListener("pointercancel", this.pointerCancel, true);
    document.addEventListener("keydown", this.keyDown, true);
    this.list.addEventListener("scroll", this.refresh);
  };

  private readonly pointerMove = (event: PointerEvent): void => {
    const drag = this.drag;
    if (drag === undefined || event.pointerId !== drag.pointerId) {
      return;
    }
    if ((event.buttons & 1) === 0) {
      this.cancel();
      return;
    }
    drag.x = event.clientX;
    drag.y = event.clientY;
    if (drag.started === false && Math.hypot(drag.x - drag.startX, drag.y - drag.startY) > 6) {
      drag.started = true;
      this.list.dataset.reordering = "true";
      drag.source.item.dataset.dragSource = "true";
    }
    if (drag.started === true) {
      event.preventDefault();
      this.refresh();
      this.scheduleScroll();
    }
  };

  private insertion(): Insertion | undefined {
    const drag = this.drag;
    if (drag === undefined || drag.started === false) {
      return undefined;
    }
    const bounds = this.list.getBoundingClientRect();
    if (drag.x < bounds.left || drag.x > bounds.right || drag.y < bounds.top || drag.y > bounds.bottom) {
      return undefined;
    }
    const tabs = this.tabs();
    const target = tabs.find((tab) => drag.x < tab.item.getBoundingClientRect().right) ?? tabs[tabs.length - 1];
    if (target === undefined || target.id === drag.source.id) {
      return undefined;
    }
    const rect = target.item.getBoundingClientRect();
    const position = drag.x < (rect.left + rect.right) / 2 ? "before" : "after";
    return { targetSessionId: target.id, position,
      x: Math.max(bounds.left + 3.5, Math.min(bounds.right - 3.5, position === "before" ? rect.left : rect.right)) };
  }

  private readonly refresh = (): void => {
    const target = this.insertion();
    this.marker.hidden = target === undefined;
    this.preview.hidden = target === undefined;
    if (target !== undefined) {
      const bounds = this.list.getBoundingClientRect();
      this.marker.style.left = `${target.x - 1.5}px`;
      this.marker.style.top = `${bounds.top}px`;
      this.marker.style.height = `${this.list.clientHeight}px`;
      const drag = this.drag!;
      this.preview.textContent = this.tabs().find((tab) => tab.id === drag.source.id)?.name ?? drag.source.name;
      // Fixed-position overlay coordinates and viewport dimensions are all CSS pixels.
      const width = document.documentElement.clientWidth;
      const height = document.documentElement.clientHeight;
      this.preview.style.maxWidth = `${Math.max(0, Math.min(220, width - 8))}px`;
      this.preview.style.maxHeight = `${Math.max(0, height - 8)}px`;
      const rect = this.preview.getBoundingClientRect();
      this.preview.style.left = `${Math.max(4, Math.min(width - rect.width - 4, drag.x + 12))}px`;
      this.preview.style.top = `${Math.max(4, Math.min(height - rect.height - 4, bounds.bottom + 6))}px`;
    }
  };

  private scrollStep(): number {
    const drag = this.drag;
    const bounds = this.list.getBoundingClientRect();
    if (drag?.started !== true || drag.x < bounds.left || drag.x > bounds.right ||
        drag.y < bounds.top || drag.y > bounds.bottom) {
      return 0;
    }
    const edge = Math.min(24, bounds.width / 4);
    if (drag.x < bounds.left + edge && this.list.scrollLeft > 0) {
      return -Math.ceil(8 * (bounds.left + edge - drag.x) / edge);
    }
    if (drag.x > bounds.right - edge && this.list.scrollLeft < this.list.scrollWidth - this.list.clientWidth) {
      return Math.ceil(8 * (drag.x - bounds.right + edge) / edge);
    }
    return 0;
  }

  private scheduleScroll(): void {
    if (this.frame !== undefined || this.scrollStep() === 0) {
      return;
    }
    this.frame = requestAnimationFrame(() => {
      this.frame = undefined;
      const step = this.scrollStep();
      if (step !== 0) {
        this.list.scrollLeft = Math.max(0, Math.min(this.list.scrollWidth - this.list.clientWidth,
          this.list.scrollLeft + step));
        this.refresh();
        this.scheduleScroll();
      }
    });
  }

  private readonly pointerUp = (event: PointerEvent): void => {
    const drag = this.drag;
    if (drag === undefined || event.pointerId !== drag.pointerId) {
      return;
    }
    drag.x = event.clientX;
    drag.y = event.clientY;
    const target = this.insertion();
    const source = drag.startOrder.find((session) => session.sessionId === drag.source.id);
    this.cancel();
    if (target !== undefined && source !== undefined) {
      this.send(source, target, drag.startOrder);
    }
  };

  private readonly pointerCancel = (event: PointerEvent): void => {
    if (event.pointerId === this.drag?.pointerId) {
      this.cancel();
    }
  };

  private readonly keyDown = (event: KeyboardEvent): void => {
    if (event.key === "Escape" && this.drag !== undefined) {
      event.preventDefault();
      event.stopImmediatePropagation();
      this.cancel();
    }
  };

  private readonly suppressClick = (event: MouseEvent): void => {
    const isTabClick = event.target === this.list ||
      this.tabs().some((tab) => tab.button.contains(event.target as Node));
    if (event.detail !== 0 && isTabClick === true &&
        Date.now() < this.suppressUntil) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };

  private readonly visibilityChanged = (): void => {
    if (document.hidden === true) {
      this.cancel();
    }
  };

  private readonly cancel = (): void => {
    const drag = this.drag;
    this.drag = undefined;
    if (this.frame !== undefined) {
      cancelAnimationFrame(this.frame);
      this.frame = undefined;
    }
    this.marker.hidden = true;
    this.preview.hidden = true;
    this.preview.textContent = "";
    delete this.list.dataset.reordering;
    document.removeEventListener("pointermove", this.pointerMove, true);
    document.removeEventListener("pointerup", this.pointerUp, true);
    document.removeEventListener("pointercancel", this.pointerCancel, true);
    document.removeEventListener("keydown", this.keyDown, true);
    this.list.removeEventListener("scroll", this.refresh);
    if (drag !== undefined) {
      delete drag.source.item.dataset.dragSource;
      if (drag.started === true) {
        // Chromium emits click/dblclick after a captured drag, including a cancelled one.
        this.suppressUntil = Date.now() + 500;
      }
      drag.source.button.removeEventListener("lostpointercapture", this.pointerCancel);
      if (drag.source.button.hasPointerCapture(drag.pointerId) === true) {
        drag.source.button.releasePointerCapture(drag.pointerId);
      }
    }
  };

  readonly dispose = (): void => {
    this.cancel();
    this.disposed = true;
    this.list.removeEventListener("pointerdown", this.pointerDown);
    document.removeEventListener("click", this.suppressClick, true);
    document.removeEventListener("dblclick", this.suppressClick, true);
    document.removeEventListener("visibilitychange", this.visibilityChanged);
    window.removeEventListener("blur", this.cancel);
    window.removeEventListener("resize", this.refresh);
    window.removeEventListener("pagehide", this.dispose);
    this.marker.remove();
    this.preview.remove();
  };
}
