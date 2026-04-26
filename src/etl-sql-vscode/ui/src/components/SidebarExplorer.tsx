import React, { useState, useMemo, useEffect } from 'react';
import { Database, Search, RefreshCw, Layers, Variable, Hash } from 'lucide-react';
import type { ProtocolMessage, Connection, Variable as ScriptVar } from '../types';
import { extractVariables } from '../utils/variable_utils';
import { MetadataItem } from './MetadataItem';

interface SidebarExplorerProps {
  messages: ProtocolMessage[];
  postMessage: (msg: any) => void;
}

export const SidebarExplorer: React.FC<SidebarExplorerProps> = ({ messages, postMessage }) => {
  const [searchQuery, setSearchQuery] = useState('');
  const [activeUri, setActiveUri] = useState<string | null>(null);

  // Derive state from messages
  const connections = useMemo(() => {
    const connMsg = [...messages].reverse().find(m => m.type === 'connections');
    return (connMsg?.type === 'connections' ? connMsg.connections : []) as Connection[];
  }, [messages]);

  const scriptConnections = useMemo(() => {
    const scriptConnMsgs = messages.filter(m => m.type === 'scriptConnections');
    const latestByUri = new Map<string, any[]>();
    scriptConnMsgs.forEach(m => {
      if (m.type === 'scriptConnections') latestByUri.set(m.uri, m.connections);
    });
    return latestByUri.get(activeUri || '') || [];
  }, [messages, activeUri]);

useEffect(() => {
    const activeMsg = [...messages].reverse().find(m => (m as any).type === 'activeEditorChanged');
    if (activeMsg?.type === 'activeEditorChanged') {
      setActiveUri(activeMsg.uri);
    }
  }, [messages]);

  useEffect(() => {
    // Signal ready to extension to receive initial data
    postMessage({ type: 'ready' });
  }, []);

  const variables = useMemo(() => {
    const runtimeMsg = [...messages].reverse().find(m => (m as any).type === 'variables');
    const runtimeVars = extractVariables(runtimeMsg) as ScriptVar[];
    const scriptMsg = [...messages].reverse().find(m => (m as any).type === 'scriptVariables');
    const scriptVars = extractVariables(scriptMsg) as any[];
    const merged = new Map<string, ScriptVar>();
    scriptVars.forEach((v: any) => merged.set(v.name.toLowerCase(), { name: v.name, typeName: v.typeName, value: v.value || '(declared)' }));
    runtimeVars.forEach(v => merged.set(v.name.toLowerCase(), v));
    return Array.from(merged.values());
  }, [messages]);

  const filteredConnections = connections.filter(c => c.name.toLowerCase().includes(searchQuery.toLowerCase()));
  const filteredVariables = variables.filter(v => v.name.toLowerCase().includes(searchQuery.toLowerCase()));

  return (
    <div className="flex flex-col h-full bg-[var(--bg,var(--bg-fallback))] text-[var(--text-primary)] overflow-hidden">
      {/* Header & Search */}
      <div className="p-3 border-b border-[var(--border)] space-y-3 bg-[var(--bg-darker)]/10">
        <div className="flex items-center justify-between">
          <h2 className="text-[11px] font-bold uppercase tracking-[0.15em] text-[var(--primary)] flex items-center gap-2">
            <RefreshCw size={12} className="text-[var(--primary)]" /> Metadata Explorer
          </h2>
          <button 
            onClick={() => postMessage({ type: 'refresh' })}
            className="p-1 hover:bg-white/10 rounded transition-colors text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
            title="Refresh Connections"
          >
            <RefreshCw size={14} />
          </button>
        </div>
        
        <div className="relative group">
          <Search size={14} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--primary)]/40 group-focus-within:text-[var(--primary)] transition-colors" />
          <input 
            type="text"
            placeholder="Search schema..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full bg-black/5 border border-[var(--border)] rounded-md py-1.5 pl-8 pr-3 text-xs text-[var(--text-primary)] placeholder-[var(--text-secondary)]/30 focus:outline-none focus:border-[var(--primary)]/50 transition-all shadow-inner"
          />
        </div>
      </div>

      {/* Explorer Tree */}
      <div className="flex-1 overflow-y-auto custom-scrollbar p-2 space-y-1">
        
        {/* Connections Section */}
        <section>
          <div className="flex items-center gap-2 px-1 py-1 mb-1 text-[9px] font-bold uppercase tracking-widest text-[var(--text-secondary)]">
            <Database size={10} /> Global Connections
          </div>
          {filteredConnections.map(conn => (
            <MetadataItem 
              key={conn.name}
              label={conn.name}
              type="connection"
              icon={Database}
              detail={conn.type}
              connectionName={conn.name}
              messages={messages}
              postMessage={postMessage}
              uri={activeUri || undefined}
            />
          ))}
        </section>

        {/* Script Section */}
        {scriptConnections.length > 0 && (
          <section className="pt-2">
            <div className="flex items-center gap-2 px-1 py-1 mb-1 text-[9px] font-bold uppercase tracking-widest text-[var(--text-secondary)]">
              <Layers size={10} /> Script Connections
            </div>
            {scriptConnections.map(conn => (
              <MetadataItem 
                key={`${conn.name}-script`}
                label={conn.name}
                type="connection"
                icon={Layers}
                detail={conn.type}
                connectionName={conn.name}
                messages={messages}
                postMessage={postMessage}
                uri={activeUri || undefined}
                isScript={true}
              />
            ))}
          </section>
        )}

        {/* Variables Section */}
        {filteredVariables.length > 0 && (
          <section className="pt-2 border-t border-[var(--border)]/50 mt-2">
            <div className="flex items-center gap-2 px-1 py-1 mb-1 text-[9px] font-bold uppercase tracking-widest text-[var(--text-secondary)]">
              <Variable size={10} /> Script Variables
            </div>
            {filteredVariables.map(v => (
              <MetadataItem
                key={v.name}
                label={v.name}
                type="variable"
                icon={Variable}
                detail={v.typeName}
                value={typeof v.value === 'object' ? JSON.stringify(v.value) : String(v.value)}
                messages={messages}
                postMessage={postMessage}
              />
            ))}
          </section>
        )}

        {/* Temp Tables Section */}
        {activeUri && (
             <section className="pt-2 border-t border-[var(--border)]/50 mt-2">
                <div className="flex items-center gap-2 px-1 py-1 mb-1 text-[9px] font-bold uppercase tracking-widest text-[var(--text-secondary)]">
                    <Hash size={10} /> Temporary Tables
                </div>
                <MetadataItem 
                    label="Active Session Tables"
                    type="temp-root"
                    icon={Hash}
                    messages={messages}
                    postMessage={postMessage}
                    uri={activeUri}
                />
            </section>
        )}
      </div>

      {/* Footer / Context */}
      {activeUri && (
        <div className="p-2 border-t border-[var(--border)] bg-black/10">
          <div className="text-[8px] font-mono opacity-30 truncate" title={activeUri}>
            Context: {activeUri.split('/').pop()}
          </div>
        </div>
      )}
    </div>
  );
};
