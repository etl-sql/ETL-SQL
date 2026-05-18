import React, { useState, useEffect } from 'react';
import { ChevronRight, ChevronDown, Table as TableIcon, RefreshCw, Box, type LucideIcon } from 'lucide-react';
import type { ProtocolMessage } from '../types';

interface ChildItem {
  label: string;
  type: 'table' | 'column';
}

interface MetadataItemProps {
  label: string;
  type: 'connection' | 'table' | 'column' | 'variable' | 'temp-root';
  icon: LucideIcon;
  detail?: string;
  value?: string;
  isScript?: boolean;
  connectionName?: string;
  uri?: string;
  messages: ProtocolMessage[];
  postMessage: (msg: Record<string, unknown>) => void;
}

export const MetadataItem: React.FC<MetadataItemProps> = ({
  label, type, icon: Icon, detail, value, isScript, connectionName, uri, messages, postMessage
}) => {
  const [isExpanded, setIsExpanded] = useState(false);
  const [children, setChildren] = useState<ChildItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [requestId] = useState(() => Math.random().toString(36).substring(7));

  const hasChildren = type !== 'column' && type !== 'variable';

  useEffect(() => {
    if (!isExpanded) return;

    if (type === 'connection') {
        // eslint-disable-next-line react-hooks/set-state-in-effect
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
    if (!latest || !('requestId' in latest) || (latest as { requestId: string }).requestId !== requestId) return;

    if (latest.type === 'tablesResponse') {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setChildren(latest.tables.map(t => ({ label: t, type: 'table' as const })));
        setLoading(false);
    } else if (latest.type === 'columnsResponse') {
        setChildren(latest.columns.map(c => ({ label: c, type: 'column' as const })));
        setLoading(false);
    } else if (latest.type === 'tempTablesResponse') {
        setChildren(latest.tables.map(t => ({ label: t, type: 'table' as const })));
        setLoading(false);
    }
  }, [messages, requestId]);

  const toggleExpand = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (hasChildren) setIsExpanded(!isExpanded);
  };

  const handleDragStart = (e: React.DragEvent) => {
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
          group flex items-center h-6 px-2 cursor-default
          hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.18))]
          ${isExpanded ? 'bg-[var(--vscode-list-inactiveSelectionBackground,rgba(90,93,94,0.22))]' : ''}
        `}
        onClick={toggleExpand}
      >
        <div className="flex items-center gap-1.5 min-w-0 flex-1">
          {hasChildren && (
            <div className="text-[var(--muted)]">
              {isExpanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
            </div>
          )}
          {!hasChildren && <div className="w-3" />}

          <Icon size={13} className={`shrink-0 ${isScript ? 'text-[var(--vscode-gitDecoration-modifiedResourceForeground,#e2c08d)]' : 'text-[var(--muted)]'}`} />

          <span className="truncate text-[12px] text-[var(--text-primary)]">
            {label}
          </span>

          {detail && (
            <span className="text-[10px] text-[var(--text-secondary)] font-mono truncate ml-1">
              {detail}
            </span>
          )}

          {value && (
             <span className="text-[10px] text-[var(--vscode-debugTokenExpression-value,#89d185)] font-mono truncate ml-auto pl-2">
                {value}
             </span>
          )}
        </div>

        {loading && (
            <RefreshCw size={10} className="animate-spin opacity-40 ml-2" />
        )}
      </div>

      {isExpanded && children.length > 0 && (
        <div className="ml-4 border-l border-[var(--border)]">
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
