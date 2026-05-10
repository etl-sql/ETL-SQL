import React, { useEffect, useRef } from 'react';
import type { LogMessage, ProtocolMessage } from '../types';
import { Terminal } from 'lucide-react';

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
      <div className="flex flex-col items-center justify-center h-full gap-2 text-[var(--muted)]">
        <Terminal size={28} strokeWidth={1.5} />
        <p className="text-xs">No log messages</p>
      </div>
    );
  }

  return (
    <div 
      ref={scrollRef}
      className="flex-1 overflow-y-auto scrollbar-fancy p-2 font-mono text-[11px] animate-fade-in"
    >
      {logs.map((msg, i) => {
        const level = (msg.level || 'info').toLowerCase();
        const levelColor: Record<string, string> = {
          info: 'text-[var(--muted)]',
          warn: 'text-[var(--color-warn)]',
          warning: 'text-[var(--color-warn)]',
          err: 'text-[var(--color-err)]',
          error: 'text-[var(--color-err)]',
          sys: 'text-[var(--color-sys)]'
        };
        const textColor = levelColor[level] || levelColor.info;

        return (
          <div 
            key={i} 
            className="flex items-start gap-3 border-b border-[var(--border)]/70 py-1 px-1 hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.31))]"
          >
            <span className="w-[64px] flex-shrink-0 text-[var(--muted)] select-none">
              {new Date().toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}
            </span>
            <span className={`w-[52px] flex-shrink-0 uppercase ${textColor}`}>
              {level}
            </span>
            <div className={`flex-1 break-all leading-relaxed ${level === 'err' || level === 'error' ? 'text-[var(--color-err)]' : 'text-[var(--text-primary)]'}`}>
              {msg.text}
            </div>
          </div>
        );
      })}
    </div>
  );
};
