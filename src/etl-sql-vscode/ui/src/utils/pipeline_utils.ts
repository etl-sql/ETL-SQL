import type { ProgressMessage } from '../types';

export function extractPipelineNodes(messages: any[]): any[] {
  const progressMessages = messages.filter(m => m.type === 'progress') as ProgressMessage[];
  if (progressMessages.length === 0) return [];

  const last = progressMessages[progressMessages.length - 1];
  if (!last.data) return [];

  // Real engine sends data as a direct array (ToSnapshot returns List<object>)
  if (Array.isArray(last.data)) return last.data;

  // Fallback: search all keys for an array (legacy/mock formats)
  const firstArray = Object.values(last.data as Record<string, unknown>).find(val => Array.isArray(val));
  if (firstArray) return firstArray as any[];

  return [];
}
