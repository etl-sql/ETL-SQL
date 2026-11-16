import type { Variable, ProtocolMessage } from '../types';

/**
 * Extracts the variable list from a message. The engine's --json stream names it 'data' and the
 * extension's own messages name it 'variables'.
 */
export function extractVariables(message: ProtocolMessage | undefined): Variable[] {
  if (!message) return [];
  if (message.type === 'variables') {
    return message.variables || message.data || [];
  }
  if (message.type === 'scriptVariables') {
    return message.variables;
  }
  return [];
}
