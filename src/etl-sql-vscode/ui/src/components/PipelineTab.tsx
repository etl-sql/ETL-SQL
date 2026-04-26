import React, { useEffect, useRef, useMemo } from 'react';
import { PlayCircle } from 'lucide-react';
import type { ExecutionNode, LogMessage, ProtocolMessage } from '../types';

interface PipelineTabProps {
  nodes: ExecutionNode[];
  messages: ProtocolMessage[];
  isFinished?: boolean;
  status?: string;
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

export const PipelineTab: React.FC<PipelineTabProps> = ({ nodes, messages, isFinished, status }) => {
  const logRef = useRef<HTMLDivElement>(null);
  const logs = useMemo(() => messages.filter(m => m.type === 'message') as LogMessage[], [messages]);

  useEffect(() => {
    if (logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight;
    }
  }, [logs.length]);

  const normalizedNodes = useMemo(() => {
    if (!isFinished) return nodes;
    const fix = (n: ExecutionNode): ExecutionNode => ({
      ...n,
      status: (n.status === 'Running' || n.status === 'Waiting') ? 'Completed' : n.status,
      children: n.children?.map(fix),
    });
    return nodes.map(fix);
  }, [nodes, isFinished]);

  const lines = useMemo(() => renderTree(normalizedNodes), [normalizedNodes]);

  const isEmpty = nodes.length === 0 && logs.length === 0;

  if (isEmpty) {
    if (status === 'running') {
      return (
        <div className="flex flex-col items-center justify-center h-full space-y-4 font-display">
          <div className="relative">
            <PlayCircle size={48} strokeWidth={1} className="text-indigo-400 animate-pulse" />
            <div className="absolute inset-0 rounded-full bg-indigo-500/10 animate-ping" />
          </div>
          <p className="text-sm font-bold uppercase tracking-[0.2em] text-indigo-400/60">Executing</p>
          <div className="flex gap-1.5">
            {[0, 1, 2].map(i => (
              <div key={i} className="w-1.5 h-1.5 rounded-full bg-indigo-400/50 animate-bounce" style={{ animationDelay: `${i * 150}ms` }} />
            ))}
          </div>
        </div>
      );
    }
    return (
      <div className="flex flex-col items-center justify-center h-full opacity-20 space-y-4 font-display">
        <PlayCircle size={48} strokeWidth={1} />
        <p className="text-sm font-bold uppercase tracking-[0.2em]">No Active Pipeline</p>
      </div>
    );
  }

  return (
    <div className="flex flex-row h-full overflow-hidden">
      {/* Left: Execution tree (~40%) */}
      <div className="w-[40%] min-w-[160px] flex flex-col border-r border-[var(--border)] overflow-hidden">
        <div className="px-3 py-1.5 border-b border-[var(--border)] shrink-0 bg-[var(--bg-darker)]/40">
          <span className="text-[9px] font-bold uppercase tracking-[0.25em] text-cyan-400">Pipeline</span>
        </div>
        <div className="flex-1 overflow-auto scrollbar-fancy p-2 font-mono text-[12px] leading-[1.65]">
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
                  <span className="ml-3 text-[var(--muted)] text-[11px] shrink-0">{line.stats}</span>
                )}
                {line.error && (
                  <span className="ml-2 text-red-400/70 text-[10px] italic truncate" title={line.error}>
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
        <div className="px-3 py-1.5 border-b border-[var(--border)] shrink-0 bg-[var(--bg-darker)]/40">
          <span className="text-[9px] font-bold uppercase tracking-[0.25em] text-yellow-400/70">Messages</span>
        </div>
        <div className="flex-1 overflow-auto scrollbar-fancy p-2 font-mono text-[11px] leading-[1.7]">
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
  );
};
