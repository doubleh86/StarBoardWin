export type SessionOrder = { sessionId: string; sessionGeneration: number };
export type WorkspaceSyncPayload = {
  order: SessionOrder[];
  activeSessionId: string | null;
};

export type WorkspaceSyncMessage = {
  version: number;
  type: "workspace-sync-begin" | "workspace-sync-complete";
  payload: WorkspaceSyncPayload;
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && Array.isArray(value) === false;
}

function isSessionId(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{32}$/i.test(value) === true &&
    value !== "00000000000000000000000000000000";
}

export function parseWorkspaceSyncMessage(value: unknown): WorkspaceSyncMessage | undefined {
  if (isRecord(value) === false || value.version !== 2 || value.sessionId !== undefined ||
      (value.type !== "workspace-sync-begin" && value.type !== "workspace-sync-complete") ||
      isRecord(value.payload) === false) {
    return undefined;
  }

  const { order, activeSessionId } = value.payload;
  if (Array.isArray(order) === false || order.length > 8) {
    return undefined;
  }

  const result: SessionOrder[] = [];
  const ids = new Set<string>();
  for (const session of order) {
    if (isRecord(session) === false || isSessionId(session.sessionId) === false ||
        typeof session.sessionGeneration !== "number" ||
        Number.isSafeInteger(session.sessionGeneration) === false || session.sessionGeneration <= 0) {
      return undefined;
    }
    const sessionId = session.sessionId.toLowerCase();
    if (ids.has(sessionId) === true) {
      return undefined;
    }
    ids.add(sessionId);
    result.push({ sessionId, sessionGeneration: session.sessionGeneration });
  }

  if ((order.length === 0 && activeSessionId !== null) ||
      (order.length > 0 && (isSessionId(activeSessionId) === false ||
        ids.has(activeSessionId.toLowerCase()) === false))) {
    return undefined;
  }

  return {
    version: 2,
    type: value.type,
    payload: { order: result, activeSessionId: typeof activeSessionId === "string"
      ? activeSessionId.toLowerCase() : null },
  };
}

function sameOrder(left: readonly SessionOrder[], right: readonly SessionOrder[]): boolean {
  return left.length === right.length && left.every((session, index) =>
    session.sessionId === right[index].sessionId &&
    session.sessionGeneration === right[index].sessionGeneration);
}

// Only a complete host snapshot is eligible as a drag's starting order.
export class WorkspaceSync {
  private pending: WorkspaceSyncPayload | undefined;
  private confirmed: SessionOrder[] | undefined;

  get isPending(): boolean {
    return this.pending !== undefined;
  }

  begin(payload: WorkspaceSyncPayload): void {
    this.pending = { order: payload.order.map((session) => ({ ...session })),
      activeSessionId: payload.activeSessionId };
    this.confirmed = undefined;
  }

  complete(payload: WorkspaceSyncPayload, liveOrder: readonly SessionOrder[] | undefined): boolean {
    if (this.pending === undefined || this.pending.activeSessionId !== payload.activeSessionId ||
        liveOrder === undefined || sameOrder(this.pending.order, payload.order) === false ||
        sameOrder(payload.order, liveOrder) === false) {
      this.confirmed = undefined;
      return false;
    }

    this.pending = undefined;
    this.confirmed = payload.order.map((session) => ({ ...session }));
    return true;
  }

  observe(liveOrder: readonly SessionOrder[] | undefined): void {
    if (this.confirmed !== undefined && (liveOrder === undefined || sameOrder(this.confirmed, liveOrder) === false)) {
      this.confirmed = undefined;
    }
  }

  getDragStartOrder(): SessionOrder[] | undefined {
    return this.confirmed?.map((session) => ({ ...session }));
  }
}
