export type DirectoryRequestState = {
  requestId: string;
  sessionId: string;
  sessionGeneration: number;
  edited: boolean;
  dialogIsCurrent: boolean;
};

export type DirectoryResponseState = {
  requestId: unknown;
  sessionId: string;
  sessionGeneration: unknown;
  currentDirectory: unknown;
};

export function shouldApplyCurrentDirectory(
  request: DirectoryRequestState,
  response: DirectoryResponseState,
  currentSessionGeneration: number,
): response is DirectoryResponseState & { currentDirectory: string } {
  return request.requestId === response.requestId &&
    request.sessionId === response.sessionId &&
    request.sessionGeneration === response.sessionGeneration &&
    currentSessionGeneration === request.sessionGeneration &&
    request.edited === false && request.dialogIsCurrent === true &&
    typeof response.currentDirectory === "string";
}
