import React, { useEffect, useRef } from 'react';
import type { LogMessage, ProtocolMessage } from '../types';
import { AlertCircle, Terminal, Settings, BellRing } from 'lucide-react';

interface ExecutionConsoleProps {
  messages: ProtocolMessage[];
}

export const ExecutionConsole: React.FC<ExecutionConsoleProps> = ({ messages }) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const logs = messages.filter(m => m.type === 'message') as LogMessage[];

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [logs]);

  if (logs.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full opacity-20 space-y-4">
        <Terminal size={48} strokeWidth={1} />
        <p className="text-sm font-display font-bold uppercase tracking-widest">No Log Messages</p>
      </div>
    );
  }

  return (
    <div 
      ref={scrollRef}
      className="flex-1 overflow-y-auto scrollbar-fancy p-6 space-y-2 font-mono text-[11px] animate-fade-in"
    >
      {logs.map((msg, i) => {
        const level = (msg.level || 'info').toLowerCase();
        const configMap: Record<string, any> = {
          info: { icon: Terminal, color: 'text-indigo-400', bg: 'bg-indigo-500/5', border: 'border-indigo-500/10' },
          warn: { icon: BellRing, color: 'text-[var(--color-warn)]', bg: 'bg-[var(--color-warn)]/5', border: 'border-[var(--color-warn)]/10' },
          warning: { icon: BellRing, color: 'text-[var(--color-warn)]', bg: 'bg-[var(--color-warn)]/5', border: 'border-[var(--color-warn)]/10' },
          err: { icon: AlertCircle, color: 'text-[var(--color-err)]', bg: 'bg-[var(--color-err)]/5', border: 'border-[var(--color-err)]/10' },
          error: { icon: AlertCircle, color: 'text-[var(--color-err)]', bg: 'bg-[var(--color-err)]/5', border: 'border-[var(--color-err)]/10' },
          sys: { icon: Settings, color: 'text-[var(--color-sys)]', bg: 'bg-[var(--color-sys)]/5', border: 'border-[var(--color-sys)]/10' }
        };
        const config = configMap[level] || configMap.info;
        const Icon = config.icon;

        return (
          <div 
            key={i} 
            className={`flex items-start gap-4 p-2 rounded border ${config.bg} ${config.border} transition-colors hover:bg-[var(--bg-darker)]/30 group`}
          >
            <span className="opacity-20 flex-shrink-0 font-bold tracking-tighter select-none">
              {new Date().toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}
            </span>
            
            <div className={`mt-0.5 ${config.color} opacity-40 group-hover:opacity-100 transition-opacity`}>
              <Icon size={12} />
            </div>

            <div className={`flex-1 break-all leading-relaxed ${msg.level === 'err' ? 'text-[var(--color-err)] font-bold' : 'text-[var(--text)] opacity-80 group-hover:opacity-100'}`}>
              {msg.text}
            </div>
          </div>
        );
      })}
    </div>
  );
};
