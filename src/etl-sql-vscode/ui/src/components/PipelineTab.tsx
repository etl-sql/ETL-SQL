import React, { useEffect, useRef, useMemo, useState, useCallback } from 'react';
import { PlayCircle, ChevronLeft, ChevronRight } from 'lucide-react';
import type { ExecutionNode, LogMessage, ProtocolMessage } from '../types';
import { extractPipelineNodes } from '../utils/pipeline_utils';

interface PipelineTabProps {
  nodes: ExecutionNode[];
  messages: ProtocolMessage[];
  isFinished?: boolean;
  status?: string;
  runHistory?: ProtocolMessage[][];
}

type NodeStatus = 'Waiting' | 'Running' | 'Completed' | 'Faulted';

function statusIcon(status: NodeStatus): string {
  switch (status) {
    case 'Completed': return '✓';
    case 'Faulted':   return '✗';
    case 'Running':   return '●';
    default:          return '·';
  }
}

function statusColorClass(status: NodeStatus): string {
  switch (status) {
    case 'Completed': return 'text-emerald-400';
    case 'Faulted':   return 'text-red-400';
    case 'Running':   return 'text-blue-400';
    default:          return 'text-slate-500';
  }
}

function msgLevelClass(level: string): string {
  switch ((level || 'info').toLowerCase()) {
    case 'err':
    case 'error':   return 'text-red-400';
    case 'warn':
    case 'warning': return 'text-yellow-400/80';
    case 'sys':     return 'text-slate-500';
    default:        return 'text-[var(--text-primary)] opacity-80';
  }
}

interface ColumnData {
  type: 'single' | 'parallel';
  nodes: ExecutionNode[];
}

function flattenDagColumns(nodes: ExecutionNode[]): ColumnData[] {
  const columns: ColumnData[] = [];
  for (const node of nodes) {
    const children = node.children || [];
    if (node.isParallelBlock && children.length) {
      columns.push({ type: 'parallel', nodes: children });
    } else if (children.length) {
      columns.push(...flattenDagColumns(children));
    } else {
      columns.push({ type: 'single', nodes: [node] });
    }
  }
  return columns;
}

export const VisualDagCanvas: React.FC<{ nodes: ExecutionNode[] }> = ({ nodes }) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const [svgPaths, setSvgPaths] = useState<string[]>([]);
  const columns = useMemo(() => flattenDagColumns(nodes), [nodes]);

  const updatePaths = useCallback(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    const containerRect = container.getBoundingClientRect();
    const columnEls = Array.from(container.querySelectorAll<HTMLElement>('[data-dag-column]'));

    const paths: string[] = [];
    for (let i = 0; i < columnEls.length - 1; i++) {
      const col1 = columnEls[i];
      const col2 = columnEls[i + 1];
      const caps1 = Array.from(col1.querySelectorAll<HTMLElement>('[data-dag-capsule]'));
      const caps2 = Array.from(col2.querySelectorAll<HTMLElement>('[data-dag-capsule]'));

      caps1.forEach(c1 => {
        const r1 = c1.getBoundingClientRect();
        const x1 = r1.right - containerRect.left + container.scrollLeft;
        const y1 = (r1.top + r1.bottom) / 2 - containerRect.top + container.scrollTop;

        caps2.forEach(c2 => {
          const r2 = c2.getBoundingClientRect();
          const x2 = r2.left - containerRect.left + container.scrollLeft;
          const y2 = (r2.top + r2.bottom) / 2 - containerRect.top + container.scrollTop;

          const dx = x2 - x1;
          const cp1x = x1 + dx / 3;
          const cp2x = x1 + (2 * dx) / 3;
          paths.push(`M ${x1} ${y1} C ${cp1x} ${y1}, ${cp2x} ${y2}, ${x2} ${y2}`);
        });
      });
    }
    setSvgPaths(paths);
  }, []);

  useEffect(() => {
    updatePaths();
    const timer = setTimeout(updatePaths, 100);
    window.addEventListener('resize', updatePaths);
    return () => {
      clearTimeout(timer);
      window.removeEventListener('resize', updatePaths);
    };
  }, [columns, updatePaths]);

  if (columns.length === 0) {
    return <p className="text-[var(--muted)] text-[11px] italic p-2">No pipeline data.</p>;
  }

  return (
    <div ref={containerRef} onScroll={updatePaths} className="relative flex-1 overflow-auto scrollbar-fancy p-3">
      <svg className="absolute inset-0 w-full h-full pointer-events-none z-0">
        {svgPaths.map((d, i) => (
          <path key={i} d={d} stroke="var(--vscode-panel-border, rgba(128,128,128,0.35))" strokeWidth="2" fill="none" />
        ))}
      </svg>
      <div className="relative z-10 flex items-center gap-10 min-h-full">
        {columns.map((col, colIdx) => (
          <div key={colIdx} data-dag-column={colIdx} className="flex flex-col gap-3 justify-center">
            {col.nodes.map((node, rowIdx) => {
              const status = node.status as NodeStatus;
              const icon = statusIcon(status);
              const colorClass = statusColorClass(status);
              const durationStr = node.durationMs != null ? `${Math.round(node.durationMs)}ms` : '';
              const rowsStr = node.rowsProcessed != null ? `${node.rowsProcessed.toLocaleString()}r` : '';

              return (
                <div
                  key={node.id || rowIdx}
                  data-dag-capsule={`${colIdx}-${rowIdx}`}
                  className={`flex flex-col gap-1 px-3 py-2 rounded-md border border-[var(--vscode-panel-border,rgba(128,128,128,0.35))] bg-[var(--vscode-sideBar-background,rgba(30,30,30,0.6))] min-w-[140px] max-w-[220px] shadow-sm ${node.status === 'Running' ? 'ring-1 ring-[var(--vscode-progressBar-background,#0e70c0)] animate-pulse' : ''}`}
                >
                  <div className="flex items-center justify-between gap-1 font-semibold text-[11px] min-w-0">
                    <span className={`truncate ${colorClass}`}>{icon} {node.name}</span>
                  </div>
                  <div className="flex items-center justify-between text-[10px] text-[var(--muted)] font-mono">
                    <span>{rowsStr}</span>
                    <span>{durationStr}</span>
                  </div>
                  {node.error && (
                    <div className="text-[10px] text-red-400/80 italic break-words mt-0.5">
                      {node.error}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>
    </div>
  );
};

export const PipelineTab: React.FC<PipelineTabProps> = ({ nodes, messages, isFinished, status, runHistory }) => {
  const logRef = useRef<HTMLDivElement>(null);
  // 'current' = live run; numeric index = into runHistory array
  const [selectedRun, setSelectedRun] = useState<'current' | number>('current');

  // Jump back to current whenever a new run starts
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (status === 'running') setSelectedRun('current');
  }, [status]);

  // Filter out blank history entries (pre-execute snapshots with no renderable content).
  const visibleHistory = useMemo(() =>
    (runHistory ?? []).filter(msgs =>
      msgs.some(m => m.type === 'progress' || m.type === 'message' || m.type === 'results')
    ), [runHistory]);

  const historyLen = visibleHistory.length;
  const isBrowsingHistory = selectedRun !== 'current';

  const displayMessages = isBrowsingHistory
    ? visibleHistory[selectedRun as number]
    : messages;

  const displayNodes = isBrowsingHistory
    ? extractPipelineNodes(visibleHistory[selectedRun as number])
    : nodes;

  const displayIsFinished = isBrowsingHistory ? true : isFinished;

  const logs = useMemo(() => displayMessages.filter(m => m.type === 'message') as LogMessage[], [displayMessages]);

  useEffect(() => {
    if (logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight;
    }
  }, [logs.length]);

  const normalizedNodes = useMemo(() => {
    if (!displayIsFinished) return displayNodes;
    const fix = (n: ExecutionNode): ExecutionNode => ({
      ...n,
      status: (n.status === 'Running' || n.status === 'Waiting') ? 'Completed' : n.status,
      children: n.children?.map(fix),
    });
    return displayNodes.map(fix);
  }, [displayNodes, displayIsFinished]);

  const isEmpty = displayNodes.length === 0 && logs.length === 0;

  if (isEmpty) {
    if (status === 'running') {
      return (
        <div className="flex flex-col items-center justify-center h-full gap-2 text-[var(--muted)]">
          <PlayCircle size={32} strokeWidth={1.5} className="text-[var(--vscode-progressBar-background,#0e70c0)] animate-pulse" />
          <p className="text-[12px]">Executing script...</p>
        </div>
      );
    }
    return (
      <div className="flex flex-col items-center justify-center h-full opacity-50 gap-2 text-[var(--muted)]">
        <PlayCircle size={32} strokeWidth={1.5} />
        <p className="text-[12px]">No active pipeline</p>
      </div>
    );
  }

  const runLabel = selectedRun === 'current'
    ? `Current${historyLen > 0 ? ` (Run ${historyLen + 1})` : ''}`
    : `Run ${(selectedRun as number) + 1} of ${historyLen}`;

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex flex-row flex-1 overflow-hidden">
        {/* Left: Execution Visual DAG (~55%) */}
        <div className="w-[55%] min-w-[240px] flex flex-col border-r border-[var(--border)] overflow-hidden">
          <div className="px-2 py-1 border-b border-[var(--border)] shrink-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
            <span className="text-[11px] font-semibold text-[var(--text)]">Pipeline DAG</span>
          </div>
          <VisualDagCanvas nodes={normalizedNodes} />
        </div>

        {/* Right: Message log (~45%) */}
        <div ref={logRef} className="flex-1 flex flex-col overflow-hidden">
          <div className="px-2 py-1 border-b border-[var(--border)] shrink-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
            <span className="text-[11px] font-semibold text-[var(--text)]">Messages</span>
          </div>
          <div className="flex-1 overflow-auto scrollbar-fancy p-2 font-mono text-[11px] leading-[1.55]">
            {logs.length === 0 ? (
              <p className="text-[var(--muted)] italic px-1">No messages.</p>
            ) : (
              logs.map((msg, i) => (
                <div key={i} className={`whitespace-pre-wrap ${msgLevelClass(msg.level)}`}>
                  {msg.text}
                </div>
              ))
            )}
          </div>
        </div>
      </div>

      {/* Run history navigation — only visible when there is history */}
      {historyLen > 0 && (
        <div className="flex items-center justify-center gap-2 px-2 py-0.5 border-t border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] shrink-0">
          <button
            onClick={() => setSelectedRun(r => r === 'current' ? historyLen - 1 : Math.max(0, (r as number) - 1))}
            disabled={selectedRun !== 'current' && (selectedRun as number) === 0}
            className="p-0.5 text-[var(--muted)] hover:text-[var(--text)] disabled:opacity-30 disabled:cursor-not-allowed"
            title="Older run"
          >
            <ChevronLeft size={14} />
          </button>
          <span className="text-[11px] text-[var(--muted)] min-w-[120px] text-center select-none">
            {runLabel}
          </span>
          <button
            onClick={() => setSelectedRun(r => {
              if (r === 'current') return 'current';
              const next = (r as number) + 1;
              return next >= historyLen ? 'current' : next;
            })}
            disabled={selectedRun === 'current'}
            className="p-0.5 text-[var(--muted)] hover:text-[var(--text)] disabled:opacity-30 disabled:cursor-not-allowed"
            title="Newer run"
          >
            <ChevronRight size={14} />
          </button>
        </div>
      )}
    </div>
  );
};
