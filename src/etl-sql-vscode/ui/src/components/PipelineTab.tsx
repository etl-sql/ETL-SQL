import React, { useEffect, useRef, useMemo, useState } from 'react';
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

interface RenderLine {
  indent: string;
  connector: string;
  label: string;
  stats: string;
  status: NodeStatus;
  isSummary: boolean;
  isRunning: boolean;
  error?: string;
}

const COLLAPSE_THRESHOLD = 5;

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

function formatMs(ms: number): string {
  if (ms >= 60_000) return `${(ms / 60_000).toFixed(1)}m`;
  if (ms >= 1_000)  return `${(ms / 1_000).toFixed(1)}s`;
  return `${Math.round(ms)}ms`;
}

function formatRows(r: number): string {
  if (r >= 1_000_000) return `${(r / 1_000_000).toFixed(1)}M`;
  if (r >= 1_000)     return `${(r / 1_000).toFixed(1)}k`;
  return `${r}r`;
}

function formatStats(node: ExecutionNode): string {
  if (node.status === 'Waiting') return '';
  if (node.status === 'Running') {
    return node.durationMs > 0 ? formatMs(node.durationMs) + '…' : '…';
  }
  const t = formatMs(node.durationMs);
  return node.rowsProcessed > 0 ? `${t}  ${formatRows(node.rowsProcessed)}` : t;
}

function nodeLabel(node: ExecutionNode): string {
  const childCount = (node.children || []).length;
  return node.isParallelBlock ? `PARALLEL (${childCount})` : node.name;
}

function buildSummary(count: number, nodes: ExecutionNode[]): string {
  const running   = nodes.filter(n => n.status === 'Running').length;
  const faulted   = nodes.filter(n => n.status === 'Faulted').length;
  const completed = nodes.filter(n => n.status === 'Completed').length;
  const waiting   = nodes.filter(n => n.status === 'Waiting').length;
  const parts: string[] = [];
  if (running   > 0) parts.push(`${running} ●`);
  if (faulted   > 0) parts.push(`${faulted} ✗`);
  if (completed > 0) parts.push(`${completed} ✓`);
  if (waiting   > 0) parts.push(`${waiting} ·`);
  const desc = parts.length > 0 ? parts.join(', ') : 'all pending';
  return `... ${count} more  (${desc})`;
}

function appendChildNode(node: ExecutionNode, indent: string, connector: string, childCont: string, lines: RenderLine[]) {
  lines.push({
    indent, connector,
    label: nodeLabel(node),
    stats: formatStats(node),
    status: node.status as NodeStatus,
    isSummary: false,
    isRunning: node.status === 'Running',
    error: node.error,
  });
  appendChildren(node, childCont, lines);
}

function appendChildren(node: ExecutionNode, continuation: string, lines: RenderLine[]) {
  const children = node.children || [];
  if (children.length === 0) return;
  const collapse = node.isParallelBlock && children.length > COLLAPSE_THRESHOLD;
  if (collapse) {
    const showFirst = Math.min(2, children.length - 1);
    for (let i = 0; i < showFirst; i++) {
      appendChildNode(children[i], continuation, '├─ ', continuation + '│  ', lines);
    }
    const hiddenCount = children.length - showFirst - 1;
    if (hiddenCount > 0) {
      const hidden = children.slice(showFirst, showFirst + hiddenCount);
      lines.push({ indent: continuation, connector: '┊  ', label: buildSummary(hiddenCount, hidden), stats: '', status: 'Waiting', isSummary: true, isRunning: false });
    }
    appendChildNode(children[children.length - 1], continuation, '└─ ', continuation + '   ', lines);
  } else {
    for (let i = 0; i < children.length; i++) {
      const isLast = i === children.length - 1;
      appendChildNode(children[i], continuation, isLast ? '└─ ' : '├─ ', continuation + (isLast ? '   ' : '│  '), lines);
    }
  }
}

function renderTree(nodes: ExecutionNode[]): RenderLine[] {
  const lines: RenderLine[] = [];
  for (const root of nodes) {
    lines.push({
      indent: '', connector: '',
      label: nodeLabel(root),
      stats: formatStats(root),
      status: root.status as NodeStatus,
      isSummary: false,
      isRunning: root.status === 'Running',
      error: root.error,
    });
    appendChildren(root, '', lines);
  }
  return lines;
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

  const lines = useMemo(() => renderTree(normalizedNodes), [normalizedNodes]);

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
      {/* Left: Execution tree (~40%) */}
      <div className="w-[40%] min-w-[180px] flex flex-col border-r border-[var(--border)] overflow-hidden">
        <div className="px-2 py-1 border-b border-[var(--border)] shrink-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
          <span className="text-[11px] font-semibold text-[var(--text)]">Pipeline</span>
        </div>
        <div className="flex-1 overflow-auto scrollbar-fancy p-2 font-mono text-[12px] leading-[1.55]">
          {lines.length === 0 ? (
            <p className="text-[var(--muted)] text-[11px] italic px-1">No pipeline data.</p>
          ) : (
            lines.map((line, idx) => (
              <div key={idx} className="flex items-baseline whitespace-pre min-w-0">
                <span className="text-[var(--muted)] select-none">{line.indent}</span>
                <span className="text-[var(--muted)] select-none">{line.connector}</span>
                {!line.isSummary && (
                  <span className={`mr-1.5 shrink-0 ${statusColorClass(line.status)} ${line.isRunning ? 'animate-pulse' : ''}`}>
                    {statusIcon(line.status)}
                  </span>
                )}
                <span className={
                  line.isSummary ? 'text-[var(--muted)] italic text-[11px]' :
                  line.status === 'Faulted' ? 'text-red-300' : 'text-[var(--text-primary)]'
                }>
                  {line.label}
                </span>
                {line.stats && (
                  <span className="ml-2 text-[var(--muted)] text-[11px] shrink-0">{line.stats}</span>
                )}
                {line.error && (
                  <span className="ml-2 text-red-400/70 text-[10px] italic break-all select-text">
                    — {line.error}
                  </span>
                )}
              </div>
            ))
          )}
        </div>
      </div>

      {/* Right: Message log */}
      <div ref={logRef} className="flex-1 flex flex-col overflow-hidden">
        <div className="px-2 py-1 border-b border-[var(--border)] shrink-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
          <span className="text-[11px] font-semibold text-[var(--text)]">Messages</span>
        </div>
        <div className="flex-1 overflow-auto scrollbar-fancy p-2 font-mono text-[11px] leading-[1.55]">
          {logs.length === 0 ? (
            <p className="text-[var(--muted)] italic px-1">No messages.</p>
          ) : (
            logs.map((msg, i) => (
              <div key={i} className={msgLevelClass(msg.level)}>
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
