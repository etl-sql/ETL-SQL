import type { ProgressMessage } from '../types';

/**
 * Greedy search for pipeline nodes in a progress message.
 * Scans the entire data object for the first available array.
 */
export function extractPipelineNodes(messages: any[]): any[] {
  const progressMessages = messages.filter(m => m.type === 'progress') as ProgressMessage[];
  if (progressMessages.length === 0) return [];
  
  const last = progressMessages[progressMessages.length - 1];
  if (!last.data) return [];

  // 1. If data itself is an array
  if (Array.isArray(last.data)) return last.data;
  
  // 2. Check known keys for performance
  const known = (last.data as any).roots || (last.data as any).Roots;
  if (Array.isArray(known)) return known;

  // 3. Search all top-level keys for an array
  const firstArray = Object.values(last.data).find(val => Array.isArray(val));
  if (firstArray) return firstArray as any[];

  return [];
}
