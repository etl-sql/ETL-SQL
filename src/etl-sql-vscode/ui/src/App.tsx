import { useState, useMemo, useEffect, useRef } from 'react';
import { useVsCodeApi } from './hooks/useVsCodeApi';
import { PipelineTab } from './components/PipelineTab';
import { ResultGrid } from './components/ResultGrid';
import { PerformanceTab } from './components/PerformanceTab';
import { ErrorBoundary } from './components/ErrorBoundary';
import { ReportTab } from './components/ReportTab';
import type { ResultsMessage, PerformanceMessage, ReportManifest } from './types';
import {
  RefreshCw,
  BarChart3,
  Database,
  Layout,
  ChevronLeft,
  ChevronRight,
  LayoutList,
  Square,
  ListTree,
  Table2,
  Gauge,
  type LucideIcon
} from 'lucide-react';
import { extractPipelineNodes } from './utils/pipeline_utils';
import { SidebarExplorer } from './components/SidebarExplorer';

declare global {
  interface Window {
    VIEW_TYPE?: 'sidebar' | 'results' | 'report';
  }
}

type TabId = 'pipeline' | 'results' | 'performance' | 'report';

function App() {
  const { messages, runHistory, status, postMessage } = useVsCodeApi();
  const [activeTab, setActiveTab] = useState<TabId>('pipeline');
  const [selectedResultIndex, setSelectedResultIndex] = useState(0);
  const [isCompareMode, setIsCompareMode] = useState(false);
  const prevStatusRef = useRef(status);

  useEffect(() => {
    if (status === 'error' && prevStatusRef.current !== 'error') {
      setActiveTab('pipeline');
    }
    prevStatusRef.current = status;
  }, [status]);

  // Allow switching view mode via query param in browser mode
  const currentView = useMemo(() => {
    if (window.VIEW_TYPE) return window.VIEW_TYPE;
    const params = new URLSearchParams(window.location.search);
    return params.get('view') as 'sidebar' | 'results' | 'report' | null;
  }, []);

  const reportManifest = useMemo(() => {
    const reportMsg = [...messages].reverse().find(m => m.type === 'reportManifest') as ReportManifest | undefined;
    return reportMsg;
  }, [messages]);

  // These must be declared before any early returns to satisfy rules-of-hooks
  const pipeline = useMemo(() => extractPipelineNodes(messages), [messages]);

  const results = useMemo(() => {
    return messages.filter(m => m.type === 'results') as ResultsMessage[];
  }, [messages]);

  // Auto-advance to latest result set; reset compare mode when results are cleared
  useEffect(() => {
    if (results.length > 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedResultIndex(results.length - 1);
    } else {
      setSelectedResultIndex(0);
      setIsCompareMode(false);
    }
  }, [results.length]);

  const perf = useMemo(() => {
    const perfMessages = messages.filter(m => m.type === 'performance') as PerformanceMessage[];
    return perfMessages.length > 0 ? perfMessages[perfMessages.length - 1].metrics : null;
  }, [messages]);

  if (currentView === 'sidebar') {
    return (
      <ErrorBoundary>
        <SidebarExplorer messages={messages} postMessage={postMessage} />
      </ErrorBoundary>
    );
  }

  if (currentView === 'report') {
    return (
        <ErrorBoundary>
            {reportManifest ? (
                <ReportTab 
                    manifest={reportManifest} 
                    onRefresh={(params) => postMessage({ type: 'refreshReport', parameters: params })} 
                    onExport={(format) => postMessage({ type: 'exportReport', format })}
                />
            ) : (
                <div className="flex-1 flex items-center justify-center bg-[var(--bg)] h-full">
                    <EmptyState icon={Layout} message="No Report Data Loaded" />
                </div>
            )}
        </ErrorBoundary>
    );
  }

  const currentResult = results[selectedResultIndex] || results[results.length - 1];

  const statusConfig = {
    ready: { label: 'Ready', color: 'bg-[var(--vscode-charts-blue,#3794ff)]' },
    running: { label: 'Executing', color: 'bg-[var(--vscode-progressBar-background,#0e70c0)] animate-pulse' },
    finished: { label: 'Completed', color: 'bg-[var(--vscode-testing-iconPassed,#73c991)]' },
    error: { label: 'Failed', color: 'bg-[var(--vscode-errorForeground,#f85149)]' }
  };

  const tabs: { id: TabId, label: string, icon: LucideIcon, badge?: number }[] = [
    { id: 'pipeline', label: 'Pipeline', icon: ListTree },
    { id: 'results', label: 'Results', icon: Table2, badge: results.length > 1 ? results.length : currentResult?.rows.length },
    { id: 'performance', label: 'Performance', icon: Gauge },
  ];

  const formatDuration = (ms: number) => {
    const totalSeconds = Math.floor(ms / 1000);
    const hrs = Math.floor(totalSeconds / 3600);
    const mins = Math.floor((totalSeconds % 3600) / 60);
    const secs = totalSeconds % 60;
    return `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  };

  return (
    <ErrorBoundary>
      <div className="flex-1 flex flex-row h-full w-full overflow-hidden">
        {/* Sidebar Vertical Navigation */}
        <nav className="sidebar-nav flex flex-col items-center w-11 shrink-0 bg-[var(--vscode-sideBar-background,var(--bg-darker))] border-r border-[var(--border)] z-10">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`
                relative flex items-center justify-center w-full h-10 transition-colors
                ${activeTab === tab.id ? 'text-[var(--vscode-list-activeSelectionForeground,var(--text))] bg-[var(--vscode-list-activeSelectionBackground,rgba(90,93,94,0.31))]' : 'text-[var(--muted)] hover:text-[var(--text)] hover:bg-[var(--vscode-list-hoverBackground,rgba(90,93,94,0.18))]'}
              `}
              title={tab.label}
              aria-label={tab.label}
            >
              <div className="relative flex h-7 w-7 items-center justify-center">
                <tab.icon size={16} strokeWidth={1.8} className={
                  status === 'error' && tab.id === 'pipeline' ? 'text-[var(--vscode-errorForeground,#f85149)]' : ''
                } />
                {status === 'error' && tab.id === 'pipeline' && (
                  <span className="absolute right-0 top-0 w-1.5 h-1.5 rounded-full bg-[var(--vscode-errorForeground,#f85149)]" />
                )}
                {tab.badge !== undefined && tab.badge > 0 && !(status === 'error' && tab.id === 'pipeline') && (
                  <span className="absolute right-0 top-0 min-w-3 h-3 px-0.5 rounded-full bg-[var(--vscode-badge-background,#4d4d4d)] text-[var(--vscode-badge-foreground,#fff)] text-[8px] leading-3 text-center">
                    {tab.badge > 99 ? '99+' : tab.badge}
                  </span>
                )}
              </div>
              
              {activeTab === tab.id && (
                <div className="absolute left-0 top-0 bottom-0 w-0.5 bg-[var(--vscode-focusBorder,#007fd4)]" />
              )}
            </button>
          ))}
        </nav>

        {/* Main Content Area - Forced Visibility */}
        <main className="flex-1 min-h-0 overflow-hidden relative flex flex-col">
          {status === 'running' && (
            <div className="absolute top-0 left-0 right-0 h-0.5 z-50 overflow-hidden bg-[var(--vscode-progressBar-background,#0e70c0)]/20">
              <div className="h-full w-2/5 bg-[var(--vscode-progressBar-background,#0e70c0)] animate-loading-bar" />
            </div>
          )}
          {activeTab === 'pipeline' && <PipelineTab nodes={pipeline} messages={messages} isFinished={status === 'finished'} status={status} runHistory={runHistory} />}
          {activeTab === 'results' && (
             <div className="flex-1 min-h-0 p-2 flex flex-col overflow-hidden">
                {isCompareMode ? (
                  <div className="flex-1 overflow-auto space-y-4 scrollbar-fancy pb-12">
                    {results.map((res, idx) => (
                      <div key={idx} className="flex flex-col gap-2">
                        <div className="flex items-center gap-2 px-1 py-1 border-b border-[var(--border)]">
                          <span className="text-[11px] font-semibold text-[var(--text)]">Result Set {idx + 1}</span>
                          <span className="text-[11px] text-[var(--muted)]">{res.rows.length.toLocaleString()} rows</span>
                        </div>
                        <div>
                          <ResultGrid rows={res.rows} columns={res.columns} />
                        </div>
                      </div>
                    ))}
                    
                    {/* Floating Toggle to exit Compare Mode */}
                    <div className="fixed bottom-8 left-1/2 -translate-x-1/2 z-50">
                       <button 
                          onClick={() => setIsCompareMode(false)}
                          className="flex items-center gap-1.5 bg-[var(--vscode-button-background,#0e639c)] text-[var(--vscode-button-foreground,#fff)] px-3 py-1.5 border border-[var(--vscode-button-border,transparent)] hover:bg-[var(--vscode-button-hoverBackground,#1177bb)] text-[11px]"
                        >
                          <LayoutList size={14} />
                          Exit Compare
                        </button>
                    </div>
                  </div>
                ) : (
                  currentResult ? (
                    <>
                      <ResultGrid rows={currentResult.rows} columns={currentResult.columns} />
                      
                      {/* Result Set Navigation & Compare Toggle */}
                      {results.length > 1 && (
                        <div className="mt-2 flex items-center justify-center gap-2 bg-[var(--vscode-panel-background,var(--bg-darker))] px-2 py-1 border border-[var(--border)] self-center">
                          <button 
                            onClick={() => setSelectedResultIndex(Math.max(0, selectedResultIndex - 1))}
                            disabled={selectedResultIndex === 0}
                            className="p-0.5 text-[var(--muted)] hover:text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] disabled:opacity-30 disabled:cursor-not-allowed"
                            title="Previous result set"
                          >
                            <ChevronLeft size={16} />
                          </button>
                          
                          <span className="text-[11px] text-[var(--muted)] min-w-[96px] text-center">
                            Result {selectedResultIndex + 1} of {results.length}
                          </span>

                          <button 
                            onClick={() => setSelectedResultIndex(Math.min(results.length - 1, selectedResultIndex + 1))}
                            disabled={selectedResultIndex === results.length - 1}
                            className="p-0.5 text-[var(--muted)] hover:text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))] disabled:opacity-30 disabled:cursor-not-allowed"
                            title="Next result set"
                          >
                            <ChevronRight size={16} />
                          </button>

                          <div className="w-px h-4 bg-[var(--border)]" />

                          <button 
                            onClick={() => setIsCompareMode(true)}
                            className="flex items-center gap-1.5 px-1.5 py-0.5 text-[11px] text-[var(--muted)] hover:text-[var(--text)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
                            title="Compare All Results"
                          >
                            <LayoutList size={14} />
                            <span>Compare</span>
                          </button>
                        </div>
                      )}
                    </>
                  ) : (
                    <EmptyState icon={BarChart3} message="No Result Set Available" />
                  )
                )}
             </div>
          )}
{activeTab === 'performance' && <PerformanceTab metrics={perf} />}
        </main>
      </div>

      {/* Modern Status Footer */}
      <footer className="flex items-center justify-between px-2 h-[24px] border-t border-[var(--border)] bg-[var(--vscode-statusBar-background,var(--bg-darker))] text-[var(--vscode-statusBar-foreground,var(--text))] z-20 shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 group shrink-0">
            <div className={`w-2 h-2 rounded-full ${statusConfig[status].color}`} />
            <span className="text-[11px]">
              {statusConfig[status].label}
            </span>
            {status === 'running' && (
              <button 
                onClick={() => postMessage({ type: 'cancel' })}
                className="flex items-center gap-1 px-1.5 py-0.5 text-[11px] text-[var(--vscode-errorForeground,#f85149)] hover:bg-[var(--vscode-toolbar-hoverBackground,rgba(90,93,94,0.31))]"
                title="Stop Execution"
              >
                <Square size={10} fill="currentColor" /> Stop
              </button>
            )}
          </div>
        </div>

        <div className="flex items-center gap-4 text-[11px] font-mono">
           <span className="flex items-center gap-1.5"><Database size={11} />{perf?.rowsProcessed.toLocaleString() || '0'} rows</span>
           <span className="flex items-center gap-1.5"><RefreshCw size={11} />{formatDuration(perf?.executionMs || 0)}</span>
        </div>
      </footer>
    </ErrorBoundary>
  );
}

const EmptyState = ({ icon: Icon, message }: { icon: LucideIcon, message: string }) => (
  <div className="flex-1 flex flex-col items-center justify-center opacity-50 space-y-2 text-[var(--muted)]">
    <Icon size={32} strokeWidth={1.5} />
    <p className="text-[12px]">{message}</p>
  </div>
);

export default App;
