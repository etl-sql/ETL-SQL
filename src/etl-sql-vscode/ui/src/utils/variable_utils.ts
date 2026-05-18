import type { Variable, ProtocolMessage } from '../types';

/**
 * Safely extracts variable list from a message, handling both 'variables' and 'data' keys.
 * Handles protocol inconsistencies between engine versions and LSP.
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
