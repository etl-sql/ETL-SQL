import React, { useMemo } from 'react';
import type { ProtocolMessage, Variable } from '../types';
import { extractVariables } from '../utils/variable_utils';
import { Variable as VariableIcon } from 'lucide-react';

interface VariablesTabProps {
  messages: ProtocolMessage[];
}

export const VariablesTab: React.FC<VariablesTabProps> = ({ messages }) => {
  const variables = useMemo(() => {
    // 1. Get the latest runtime variables (from execution)
    const runtimeMsg = [...messages].reverse().find(m => (m as any).type === 'variables');
    const runtimeVars = extractVariables(runtimeMsg);

    // 2. Get the latest script variables (from LSP while typing)
    const scriptMsg = [...messages].reverse().find(m => (m as any).type === 'scriptVariables');
    const scriptVars = extractVariables(scriptMsg);

    // 3. Merge them: Runtime values win over static definitions
    const merged = new Map<string, Variable>();

    // Add script variables first (placeholders)
    scriptVars.forEach(v => {
      merged.set(v.name.toLowerCase(), {
        name: v.name,
        typeName: v.typeName,
        value: v.value || '(declared)',
        isScriptOnly: true
      } as any);
    });

    // Overwrite with runtime variables (actual state)
    runtimeVars.forEach(v => {
      merged.set(v.name.toLowerCase(), {
        ...v,
        isScriptOnly: false
      } as any);
    });

    return Array.from(merged.values());
  }, [messages]);

  if (variables.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-2 text-[var(--muted)]">
        <VariableIcon size={28} strokeWidth={1.5} />
        <p className="text-xs">No active variables</p>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-auto custom-scrollbar p-2 animate-fade-in">
      <table className="w-full border-collapse text-[12px]">
        <thead className="sticky top-0 z-10 bg-[var(--vscode-editor-background,var(--bg))] text-[var(--muted)]">
          <tr className="border-b border-[var(--border)]">
            <th className="px-2 py-1.5 text-left font-normal">Name</th>
            <th className="px-2 py-1.5 text-left font-normal">Type</th>
            <th className="px-2 py-1.5 text-left font-normal">Scope</th>
            <th className="px-2 py-1.5 text-left font-normal">Value</th>
          </tr>
        </thead>
        <tbody>
          {variables.map((v: any) => (
            <tr
              key={v.name}
              className="border-b border-[var(--border)]/70 hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.31))]"
            >
              <td className="px-2 py-1.5 font-mono text-[var(--text-primary)] whitespace-nowrap">
                {v.name.startsWith('@') ? v.name : `@${v.name}`}
              </td>
              <td className="px-2 py-1.5 font-mono text-[var(--muted)] whitespace-nowrap">
                {v.typeName}
              </td>
              <td className="px-2 py-1.5 text-[var(--muted)] whitespace-nowrap">
                {v.isScriptOnly ? 'Declared' : 'Runtime'}
              </td>
              <td className="px-2 py-1.5 font-mono text-[var(--text)] break-all">
                {typeof v.value === 'object' ? JSON.stringify(v.value) : String(v.value)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};
