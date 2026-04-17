import React, { useMemo } from 'react';
import type { ProtocolMessage, Variable, VariablesMessage } from '../types';
import { extractVariables } from '../utils/variable_utils';
import { Variable as VariableIcon, Box, Code2 } from 'lucide-react';

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
      <div className="flex flex-col items-center justify-center h-full opacity-20 space-y-4">
        <VariableIcon size={48} strokeWidth={1} />
        <p className="text-sm font-display font-bold uppercase tracking-widest">No Active Variables</p>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto p-6 animate-fade-in custom-scrollbar">
       <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {variables.map((v: any) => (
            <div 
              key={v.name}
              className={`
                glass-card p-4 rounded-lg flex flex-col gap-2 group transition-all duration-300
                border ${v.isScriptOnly ? 'border-dashed border-[var(--border)] opacity-80' : 'border-[var(--border)] hover:border-[var(--primary)]/50'}
              `}
            >
              <div className="flex items-center justify-between border-b border-[var(--border)] pb-2 mb-1">
                 <div className="flex items-center gap-2">
                    <div className={`p-1.5 rounded ${v.isScriptOnly ? 'bg-slate-500/10 text-slate-400' : 'bg-indigo-500/10 text-indigo-400'}`}>
                        {v.isScriptOnly ? <Code2 size={14} /> : <Box size={14} />}
                    </div>
                    <span className="font-mono text-sm font-bold text-[var(--text-primary)]">
                        {v.name.startsWith('@') ? v.name : `@${v.name}`}
                    </span>
                 </div>
                 <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-tighter ${v.isScriptOnly ? 'bg-slate-500/10 text-slate-400' : 'bg-indigo-500/10 text-indigo-400'}`}>
                    {v.typeName}
                 </span>
              </div>
              
              <div className="flex flex-col gap-1">
                 <span className="text-[10px] uppercase font-bold text-[var(--text-secondary)] tracking-widest opacity-60">
                    {v.isScriptOnly ? 'Initial/Declared Value' : 'Current Value'}
                 </span>
                 <div className={`
                    rounded p-2 font-mono text-xs break-all border border-white/5
                    ${v.isScriptOnly ? 'bg-slate-500/5 text-slate-400' : 'bg-emerald-500/10 text-emerald-400 font-bold'}
                 `}>
                    {typeof v.value === 'object' ? JSON.stringify(v.value, null, 2) : String(v.value)}
                 </div>
              </div>
            </div>
          ))}
       </div>
    </div>
  );
};
