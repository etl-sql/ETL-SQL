import type { Variable } from '../types';

/**
 * Safely extracts variable list from a message, handling both 'variables' and 'data' keys.
 * Handles protocol inconsistencies between engine versions and LSP.
 */
export function extractVariables(message: any): Variable[] {
  if (!message || (message.type !== 'variables' && message.type !== 'scriptVariables')) {
    return [];
  }
  return (message.variables || message.data || []) as Variable[];
}
