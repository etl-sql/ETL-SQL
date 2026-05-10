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
      <div className="flex flex-col items-center justify-center h-full opacity-50 gap-2 text-[var(--muted)]">
        <Activity size={32} strokeWidth={1.5} />
        <p className="text-[12px]">No performance data</p>
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
      <div className="px-2 py-1 border-b border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] shrink-0">
        <span className="text-[11px] font-semibold text-[var(--text)]">Performance</span>
      </div>

      {/* Top section: two columns filling full width */}
      <div className="flex flex-row border-b border-[var(--border)] shrink-0">

        {/* Left: summary metrics */}
        <div className="flex-1 border-r border-[var(--border)]">
          <div className="px-2 py-1 border-b border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
            <span className="text-[11px] font-semibold text-[var(--muted)]">Summary</span>
          </div>
          <table className="w-full border-collapse">
            <tbody>
              <StatRow label="Execution Time" value={fmtMs(metrics.executionMs)} />
              <StatRow label="Total Rows"     value={metrics.rowsProcessed.toLocaleString()} />
              <StatRow label="Rows/s"         value={rowsPerSec.toLocaleString()} />
              <StatRow label="Memory (Peak)"  value={`${metrics.memoryMb.toFixed(1)} MB`} />
            </tbody>
          </table>
        </div>

        {/* Right: statement breakdown */}
        <div className="flex-1">
          <div className="px-2 py-1 border-b border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
            <span className="text-[11px] font-semibold text-[var(--muted)]">Statements</span>
          </div>
          {metrics.statements.length === 0 ? (
            <p className="px-3 py-2 text-[var(--muted)] italic text-[11px]">No statement metrics yet.</p>
          ) : (
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--bg-darker)]/20">
                  <th className="px-3 py-1 text-left text-[11px] font-semibold text-[var(--text)]">Type</th>
                  <th className="px-3 py-1 text-right text-[11px] font-semibold text-[var(--text)]">Duration</th>
                  <th className="px-3 py-1 text-right text-[11px] font-semibold text-[var(--text)]">%</th>
                </tr>
              </thead>
              <tbody>
                {metrics.statements.map((s, i) => {
                  const pct = totalStmtMs > 0 ? ((s.totalMs / totalStmtMs) * 100).toFixed(1) : '0.0';
                  return (
                    <tr key={i} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.18))]">
                      <td className="px-3 py-1 text-[var(--text-primary)] opacity-90">{s.type}</td>
                      <td className="px-3 py-1 text-right text-[var(--vscode-debugTokenExpression-value,#89d185)] tabular-nums">{fmtMs(s.totalMs)}</td>
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
        <div className="px-2 py-1 border-b border-[var(--border)] bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))] shrink-0">
          <span className="text-[11px] font-semibold text-[var(--muted)]">Execution Profile</span>
        </div>
        <div className="flex-1 overflow-auto scrollbar-fancy">
          <table className="w-full border-collapse">
            <thead className="sticky top-0 bg-[var(--vscode-editorGroupHeader-tabsBackground,var(--bg-darker))]">
              <tr className="border-b border-[var(--border)]">
                <th className="px-3 py-1 text-left text-[11px] font-semibold text-[var(--text)] w-24">Time</th>
                <th className="px-3 py-1 text-left text-[11px] font-semibold text-[var(--text)]">Statement</th>
                <th className="px-3 py-1 text-right text-[11px] font-semibold text-[var(--text)] w-20">Rows</th>
                <th className="px-3 py-1 text-right text-[11px] font-semibold text-[var(--text)] w-20">Dur</th>
                <th className="px-3 py-1 text-right text-[11px] font-semibold text-[var(--text)] w-20">Mem</th>
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

const StatRow: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <tr className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.18))]">
    <td className="px-3 py-1 text-[var(--muted)]">{label}</td>
    <td className="px-3 py-1 font-semibold tabular-nums text-[var(--vscode-debugTokenExpression-value,#89d185)]">{value}</td>
  </tr>
);
