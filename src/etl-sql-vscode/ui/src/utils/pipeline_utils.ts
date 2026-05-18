import type { ProgressMessage, ProtocolMessage, ExecutionNode } from '../types';

export function extractPipelineNodes(messages: ProtocolMessage[]): ExecutionNode[] {
  const progressMessages = messages.filter(m => m.type === 'progress') as ProgressMessage[];
  if (progressMessages.length === 0) return [];

  const last = progressMessages[progressMessages.length - 1];
  if (!last.data) return [];

  let roots: ExecutionNode[];

  // Real engine sends data as a direct array (ToSnapshot returns List<object>)
  if (Array.isArray(last.data)) {
    roots = last.data as ExecutionNode[];
  } else {
    // Fallback: search all keys for an array (legacy/mock formats)
    const firstArray = Object.values(last.data as Record<string, unknown>).find(val => Array.isArray(val));
    roots = firstArray ? (firstArray as ExecutionNode[]) : [];
  }

  if (roots.length === 0) return [];

  // The REPL engine accumulates all script executions across the session in a
  // single snapshot. Return only the last root node (the current/most-recent run).
  return [roots[roots.length - 1]];
}
