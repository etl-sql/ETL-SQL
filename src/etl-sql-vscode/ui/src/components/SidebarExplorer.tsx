import React, { useState, useMemo, useEffect } from 'react';
import { Database, Search, RefreshCw, Layers, Variable, Hash, Plus } from 'lucide-react';
import type { ProtocolMessage, Connection, Variable as ScriptVar } from '../types';
import { extractVariables } from '../utils/variable_utils';
import { MetadataItem } from './MetadataItem';

interface SidebarExplorerProps {
  messages: ProtocolMessage[];
  postMessage: (msg: Record<string, unknown>) => void;
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
    const latestByUri = new Map<string, Connection[]>();
    scriptConnMsgs.forEach(m => {
      if (m.type === 'scriptConnections') latestByUri.set(m.uri, m.connections);
    });
    return latestByUri.get(activeUri || '') || [];
  }, [messages, activeUri]);

  useEffect(() => {
    const activeMsg = [...messages].reverse().find(m => m.type === 'activeEditorChanged');
    if (activeMsg?.type === 'activeEditorChanged') {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setActiveUri(activeMsg.uri);
    }
  }, [messages]);

  useEffect(() => {
    postMessage({ type: 'ready' });
  }, [postMessage]);

  const variables = useMemo(() => {
    const runtimeMsg = [...messages].reverse().find(m => m.type === 'variables');
    const runtimeVars = extractVariables(runtimeMsg) as ScriptVar[];
    const scriptMsg = [...messages].reverse().find(m => m.type === 'scriptVariables');
    const scriptVars = extractVariables(scriptMsg);
    const merged = new Map<string, ScriptVar>();
    scriptVars.forEach(v => merged.set(v.name.toLowerCase(), { name: v.name, typeName: v.typeName, value: v.value || '(declared)' }));
    runtimeVars.forEach(v => merged.set(v.name.toLowerCase(), v));
    return Array.from(merged.values());
  }, [messages]);

  const filteredConnections = connections.filter(c => c.name.toLowerCase().includes(searchQuery.toLowerCase()));
  const filteredVariables = variables.filter(v => v.name.toLowerCase().includes(searchQuery.toLowerCase()));

  const sectionLabelClass = "flex items-center gap-1.5 px-2 py-1 text-[11px] font-semibold text-[var(--muted)]";

  return (
    <div className="flex flex-col h-full bg-[var(--bg,var(--bg-fallback))] text-[var(--text-primary)] overflow-hidden">
      {/* Header & Search */}
      <div className="px-2 py-2 border-b border-[var(--border)] space-y-2 bg-[var(--vscode-sideBar-background,var(--bg-darker))]">
        <div className="flex items-center justify-between">
          <h2 className="text-[12px] font-semibold text-[var(--text-primary)] flex items-center gap-1.5">
            <Database size={13} className="text-[var(--muted)]" /> Metadata
          </h2>
          <div className="flex items-center gap-1">
            <button 
              onClick={() => postMessage({ type: 'openConnectionWizard' })}
              className="p-1 text-[var(--muted)] hover:text-[var(--text-primary)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] rounded"
              title="New Connection Wizard..."
            >
              <Plus size={14} />
            </button>
            <button 
              onClick={() => postMessage({ type: 'refresh' })}
              className="p-1 text-[var(--muted)] hover:text-[var(--text-primary)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] rounded"
              title="Refresh Connections"
            >
              <RefreshCw size={14} />
            </button>
          </div>
        </div>
        
        <div className="relative group">
          <Search size={13} className="absolute left-2 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
          <input 
            type="text"
            placeholder="Search schema..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full bg-[var(--vscode-input-background,var(--bg))] border border-[var(--vscode-input-border,var(--border))] py-1 pl-7 pr-2 text-[12px] text-[var(--vscode-input-foreground,var(--text-primary))] placeholder-[var(--vscode-input-placeholderForeground,var(--muted))] focus:outline-none focus:border-[var(--vscode-focusBorder,#007fd4)]"
          />
        </div>
      </div>

      {/* Explorer Tree */}
      <div className="flex-1 overflow-y-auto custom-scrollbar py-1">
        
        {/* Connections Section */}
        <section>
          <div className={sectionLabelClass}>
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
          <section className="pt-1">
            <div className={sectionLabelClass}>
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
          <section className="pt-1 border-t border-[var(--border)] mt-1">
            <div className={sectionLabelClass}>
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
             <section className="pt-1 border-t border-[var(--border)] mt-1">
                <div className={sectionLabelClass}>
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
        <div className="px-2 py-1 border-t border-[var(--border)] bg-[var(--vscode-sideBar-background,var(--bg-darker))]">
          <div className="text-[10px] font-mono text-[var(--muted)] truncate" title={activeUri}>
            Context: {activeUri.split('/').pop()}
          </div>
        </div>
      )}
    </div>
  );
};
