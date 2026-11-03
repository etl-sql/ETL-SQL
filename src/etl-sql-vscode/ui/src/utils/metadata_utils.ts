import type { ProtocolMessage, TablesResponse, ColumnsResponse, TempTablesResponse, Variable } from '../types';

export type MetadataResponse = TablesResponse | ColumnsResponse | TempTablesResponse;

function isMetadataResponse(message: ProtocolMessage): message is MetadataResponse {
  return message.type === 'tablesResponse' || message.type === 'columnsResponse' || message.type === 'tempTablesResponse';
}

/**
 * Finds the newest response to `requestId` among the messages that arrived after `lastSeen`.
 *
 * Several messages can land between two renders, so looking only at the last one missed any
 * response that was followed by an unrelated message, and the tree item stayed on "loading".
 * `lastSeen` is the last message already handled; when it is no longer in the list (the list was
 * cleared), every message is new.
 */
export function newestMetadataResponse(
  messages: readonly ProtocolMessage[],
  lastSeen: ProtocolMessage | null,
  requestId: string
): MetadataResponse | undefined {
  const start = lastSeen ? messages.lastIndexOf(lastSeen) + 1 : 0;
  for (let i = messages.length - 1; i >= start; i--) {
    const message = messages[i];
    if (isMetadataResponse(message) && message.requestId === requestId) {
      return message;
    }
  }
  return undefined;
}

/**
 * The script variables for the active editor. Each open script reports its own, so taking the
 * newest message from any script showed another file's variables after switching editors.
 */
export function scriptVariablesFor(messages: readonly ProtocolMessage[], activeUri: string | null): Variable[] {
  for (let i = messages.length - 1; i >= 0; i--) {
    const message = messages[i];
    if (message.type === 'scriptVariables' && message.uri === (activeUri || '')) {
      return message.variables || [];
    }
  }
  return [];
}
