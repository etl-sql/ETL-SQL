import React, { useState, useEffect } from 'react';
import { ChevronRight, ChevronDown, Table as TableIcon, RefreshCw, Box } from 'lucide-react';
import type { ProtocolMessage } from '../types';

interface MetadataItemProps {
  label: string;
  type: 'connection' | 'table' | 'column' | 'variable' | 'temp-root';
  icon: any;
  detail?: string;
  value?: string;
  isScript?: boolean;
  connectionName?: string;
  uri?: string;
  messages: ProtocolMessage[];
  postMessage: (msg: any) => void;
}

export const MetadataItem: React.FC<MetadataItemProps> = ({ 
  label, type, icon: Icon, detail, value, isScript, connectionName, uri, messages, postMessage 
}) => {
  const [isExpanded, setIsExpanded] = useState(false);
  const [children, setChildren] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [requestId] = useState(() => Math.random().toString(36).substring(7));

  const hasChildren = type !== 'column' && type !== 'variable';

  useEffect(() => {
    if (!isExpanded) return;

    if (type === 'connection') {
        setLoading(true);
        postMessage({ type: 'getTables', connectionName: label, uri, requestId });
    } else if (type === 'table') {
        setLoading(true);
        postMessage({ type: 'getColumns', connectionName: connectionName || label, tableName: label, uri, requestId });
    } else if (type === 'temp-root') {
        setLoading(true);
        postMessage({ type: 'getTempTables', uri, requestId });
    }
  }, [isExpanded, label, type, uri, postMessage, requestId, connectionName]);

  // Handle responses from the extension
  useEffect(() => {
    const latest = messages[messages.length - 1];
    if (!latest || (latest as any).requestId !== requestId) return;

    if (latest.type === 'tablesResponse') {
        setChildren(latest.tables.map(t => ({ label: t, type: 'table' })));
        setLoading(false);
    } else if (latest.type === 'columnsResponse') {
        setChildren(latest.columns.map(c => ({ label: c, type: 'column' })));
        setLoading(false);
    } else if (latest.type === 'tempTablesResponse') {
        setChildren(latest.tables.map(t => ({ label: t, type: 'table' })));
        setLoading(false);
    }
  }, [messages, requestId]);

  const toggleExpand = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (hasChildren) setIsExpanded(!isExpanded);
  };

  const handleDragStart = (e: React.DragEvent) => {
    // Basic drag-and-drop: drag the name
    let text = label;
    if (type === 'variable' && !text.startsWith('@')) text = '@' + text;
    e.dataTransfer.setData('text/plain', text);
    e.dataTransfer.dropEffect = 'copy';
  };

  return (
    <div className="select-none">
      <div 
        draggable={type === 'table' || type === 'column' || type === 'variable'}
        onDragStart={handleDragStart}
        className={`
          group flex items-center py-1 px-1.5 rounded cursor-default transition-all duration-200
          hover:bg-indigo-500/10 active:bg-indigo-500/20
          ${isExpanded ? 'bg-indigo-500/5' : ''}
        `}
        onClick={toggleExpand}
      >
        <div className="flex items-center gap-1.5 min-w-0 flex-1">
          {hasChildren && (
            <div className="text-indigo-400 group-hover:text-indigo-300 transition-colors">
              {isExpanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
            </div>
          )}
          {!hasChildren && <div className="w-3" />}
          
          <Icon size={14} className={`shrink-0 ${isScript ? 'text-amber-400' : 'text-indigo-400'}`} />
          
          <span className="truncate text-[11px] font-bold text-[var(--text-primary)] tracking-tight">
            {label}
          </span>

          {detail && (
            <span className="text-[10px] text-[var(--text-secondary)] font-mono truncate ml-1 px-1 rounded bg-white/5 border border-white/5">
              {detail}
            </span>
          )}

          {value && (
             <span className="text-[10px] text-emerald-400 font-bold truncate ml-auto pl-2">
                {value}
             </span>
          )}
        </div>

        {loading && (
            <RefreshCw size={10} className="animate-spin opacity-40 ml-2" />
        )}
      </div>

      {isExpanded && children.length > 0 && (
        <div className="ml-4 border-l border-[var(--border)]/30 mt-0.5">
          {children.map((child, idx) => (
            <MetadataItem 
              key={`${child.label}-${idx}`}
              label={child.label}
              type={child.type}
              icon={child.type === 'table' ? TableIcon : Box}
              connectionName={type === 'connection' ? label : connectionName}
              messages={messages}
              postMessage={postMessage}
              uri={uri}
            />
          ))}
        </div>
      )}
    </div>
  );
};
