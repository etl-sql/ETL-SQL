import React from 'react';
import { Activity } from 'lucide-react';
import type { PerformanceMetrics } from '../types';

interface PerformanceTabProps {
  metrics: PerformanceMetrics | null;
}

function fmtMs(ms: number): string {
  if (ms >= 60_000) return `${(ms / 60_000).toFixed(1)}m`;
  if (ms >= 1_000)  return `${(ms / 1_000).toFixed(1)}s`;
  return `${Math.round(ms)}ms`;
}

export const PerformanceTab: React.FC<PerformanceTabProps> = ({ metrics }) => {
  if (!metrics) {
    return (
      <div className="flex flex-col items-center justify-center h-full opacity-20 space-y-4 font-display">
        <Activity size={48} strokeWidth={1} />
        <p className="text-sm font-bold uppercase tracking-widest">No Performance Data</p>
      </div>
    );
  }

  const rowsPerSec = metrics.executionMs > 0
    ? Math.round((metrics.rowsProcessed / metrics.executionMs) * 1000)
    : 0;

  const totalStmtMs = metrics.statements.reduce((s, x) => s + x.totalMs, 0);

  return (
    <div className="h-full flex flex-col overflow-hidden font-mono text-[12px]">

      {/* Header */}
      <div className="px-3 py-1.5 border-b border-[var(--border)] bg-[var(--bg-darker)]/40 shrink-0">
        <span className="text-[9px] font-bold uppercase tracking-[0.25em] text-yellow-400/80">Performance Dashboard</span>
      </div>

      {/* Top section: two columns filling full width */}
      <div className="flex flex-row border-b border-[var(--border)] shrink-0">

        {/* Left: summary metrics */}
        <div className="flex-1 border-r border-[var(--border)]">
          <div className="px-3 py-1 border-b border-[var(--border)] bg-[var(--bg-darker)]/20">
            <span className="text-[9px] font-bold uppercase tracking-widest text-[var(--muted)]">Main</span>
          </div>
          <table className="w-full border-collapse">
            <tbody>
              <StatRow label="Execution Time" value={fmtMs(metrics.executionMs)}                color="text-green-400" />
              <StatRow label="Total Rows"     value={metrics.rowsProcessed.toLocaleString()}    color="text-cyan-400" />
              <StatRow label="Rows/s"         value={rowsPerSec.toLocaleString()}               color="text-green-400" />
              <StatRow label="Memory (Peak)"  value={`${metrics.memoryMb.toFixed(1)} MB`}       color="text-blue-400" />
            </tbody>
          </table>
        </div>

        {/* Right: statement breakdown */}
        <div className="flex-1">
          <div className="px-3 py-1 border-b border-[var(--border)] bg-[var(--bg-darker)]/20">
            <span className="text-[9px] font-bold uppercase tracking-widest text-[var(--muted)]">Details</span>
          </div>
          {metrics.statements.length === 0 ? (
            <p className="px-3 py-2 text-[var(--muted)] italic text-[11px]">No statement metrics yet.</p>
          ) : (
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--bg-darker)]/20">
                  <th className="px-3 py-1 text-left   text-[9px] font-bold uppercase tracking-widest text-indigo-400">Type</th>
                  <th className="px-3 py-1 text-right  text-[9px] font-bold uppercase tracking-widest text-indigo-400">Duration</th>
                  <th className="px-3 py-1 text-right  text-[9px] font-bold uppercase tracking-widest text-indigo-400">%</th>
                </tr>
              </thead>
              <tbody>
                {metrics.statements.map((s, i) => {
                  const pct = totalStmtMs > 0 ? ((s.totalMs / totalStmtMs) * 100).toFixed(1) : '0.0';
                  return (
                    <tr key={i} className="border-b border-[var(--border)] last:border-0 hover:bg-white/[0.02]">
                      <td className="px-3 py-1 text-[var(--text-primary)] opacity-90">{s.type}</td>
                      <td className="px-3 py-1 text-right text-green-400 tabular-nums">{fmtMs(s.totalMs)}</td>
                      <td className="px-3 py-1 text-right text-[var(--muted)] tabular-nums">{pct}%</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {/* Bottom: detailed execution profile — full width, fills remaining height */}
      <div className="flex flex-col flex-1 min-h-0">
        <div className="px-3 py-1 border-b border-[var(--border)] bg-[var(--bg-darker)]/20 shrink-0">
          <span className="text-[9px] font-bold uppercase tracking-widest text-[var(--muted)]">Detailed Execution Profile</span>
        </div>
        <div className="flex-1 overflow-auto scrollbar-fancy">
          <table className="w-full border-collapse">
            <thead className="sticky top-0 bg-[var(--bg-darker)]/90">
              <tr className="border-b border-[var(--border)]">
                <th className="px-3 py-1 text-left  text-[9px] font-bold uppercase tracking-widest text-indigo-400 w-24">Time</th>
                <th className="px-3 py-1 text-left  text-[9px] font-bold uppercase tracking-widest text-indigo-400">Statement</th>
                <th className="px-3 py-1 text-right text-[9px] font-bold uppercase tracking-widest text-indigo-400 w-20">Rows</th>
                <th className="px-3 py-1 text-right text-[9px] font-bold uppercase tracking-widest text-indigo-400 w-20">Dur</th>
                <th className="px-3 py-1 text-right text-[9px] font-bold uppercase tracking-widest text-indigo-400 w-20">Mem</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td colSpan={5} className="px-3 py-2 text-[var(--muted)] italic text-[11px]">
                  Run <span className="text-[var(--text-primary)] not-italic">SET PROFILING ON;</span> before your script to capture per-statement metrics.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

    </div>
  );
};

const StatRow: React.FC<{ label: string; value: string; color: string }> = ({ label, value, color }) => (
  <tr className="border-b border-[var(--border)] last:border-0 hover:bg-white/[0.02]">
    <td className="px-3 py-1 text-[var(--muted)]">{label}</td>
    <td className={`px-3 py-1 font-bold tabular-nums ${color}`}>{value}</td>
  </tr>
);
