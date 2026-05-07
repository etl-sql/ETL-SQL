import { useState, useMemo, useEffect, useRef } from 'react';
import { useVsCodeApi } from './hooks/useVsCodeApi';
import { PipelineTab } from './components/PipelineTab';
import { ResultGrid } from './components/ResultGrid';
import { PerformanceTab } from './components/PerformanceTab';
import { ErrorBoundary } from './components/ErrorBoundary';
import { ReportTab } from './components/ReportTab';
import type { ResultsMessage, PerformanceMessage, ReportManifest } from './types';
import { RefreshCw, BarChart3, Database, Activity, GitBranch, Layout, ChevronLeft, ChevronRight, LayoutList, Square } from 'lucide-react';
import { extractPipelineNodes } from './utils/pipeline_utils';
import { SidebarExplorer } from './components/SidebarExplorer';

declare global {
  interface Window {
    VIEW_TYPE?: 'sidebar' | 'results' | 'report';
  }
}

type TabId = 'pipeline' | 'results' | 'performance' | 'report';

function App() {
  const { messages, status, postMessage } = useVsCodeApi();
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
                <div className="flex-1 flex items-center justify-center bg-[var(--bg-dark)] h-full">
                    <EmptyState icon={Layout} message="No Report Data Loaded" />
                </div>
            )}
        </ErrorBoundary>
    );
  }

  // Rest of Results Panel code...

  // Extract relevant state from messages with defensive checks
  const pipeline = useMemo(() => extractPipelineNodes(messages), [messages]);

  const results = useMemo(() => {
    return messages.filter(m => m.type === 'results') as ResultsMessage[];
  }, [messages]);

  // Auto-advance to latest result set; reset compare mode when results are cleared
  useEffect(() => {
    if (results.length > 0) {
      setSelectedResultIndex(results.length - 1);
    } else {
      setSelectedResultIndex(0);
      setIsCompareMode(false);
    }
  }, [results.length]);

  const currentResult = results[selectedResultIndex] || results[results.length - 1];

  const perf = useMemo(() => {
    const perfMessages = messages.filter(m => m.type === 'performance') as PerformanceMessage[];
    return perfMessages.length > 0 ? perfMessages[perfMessages.length - 1].metrics : null;
  }, [messages]);

  const statusConfig = {
    ready: { label: 'Ready', color: 'bg-indigo-500', shadow: 'shadow-indigo-500/20' },
    running: { label: 'Executing', color: 'bg-blue-500 animate-pulse', shadow: 'shadow-blue-500/40' },
    finished: { label: 'Completed', color: 'bg-emerald-500', shadow: 'shadow-emerald-500/40' },
    error: { label: 'Failed', color: 'bg-red-500', shadow: 'shadow-red-500/40' }
  };

  const tabs: { id: TabId, label: string, icon: any, badge?: number }[] = [
    { id: 'pipeline', label: 'Pipeline', icon: GitBranch },
    { id: 'results', label: 'Results', icon: Database, badge: results.length > 1 ? results.length : currentResult?.rows.length },
    { id: 'performance', label: 'Performance', icon: Activity },
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
        <nav className="sidebar-nav flex flex-col items-center w-14 pt-4 shrink-0 bg-[var(--bg-darker)]/40 border-r border-[var(--border)] z-10">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`
                relative flex flex-col items-center justify-center py-3 transition-all duration-300 group
                ${activeTab === tab.id ? 'text-[var(--primary)]' : 'text-[var(--muted)] hover:text-[var(--text)]'}
              `}
              title={tab.label}
            >
              <div className="relative">
                <tab.icon size={18} className={
                  status === 'error' && tab.id === 'pipeline' ? 'text-red-400' :
                  activeTab === tab.id ? 'text-indigo-400' : ''
                } />
                {status === 'error' && tab.id === 'pipeline' && (
                  <span className="absolute -top-1 -right-1 w-2 h-2 rounded-full bg-red-500 shadow-[0_0_6px_rgba(239,68,68,0.8)]" />
                )}
                {tab.badge !== undefined && tab.badge > 0 && !(status === 'error' && tab.id === 'pipeline') && (
                  <span className="absolute -top-2 -right-2 px-1 py-0.5 rounded-full bg-indigo-500 text-white text-[7px] font-bold shadow-lg">
                    {tab.badge}
                  </span>
                )}
              </div>
              
              {activeTab === tab.id && (
                <div className="absolute left-0 top-2 bottom-2 w-0.5 bg-gradient-to-b from-indigo-500/20 via-indigo-500 to-indigo-500/20 shadow-[2px_0_10px_rgba(99,102,241,0.5)]" />
              )}
            </button>
          ))}
        </nav>

        {/* Main Content Area - Forced Visibility */}
        <main className="flex-1 min-h-0 overflow-hidden relative flex flex-col">
          {status === 'running' && (
            <div className="absolute top-0 left-0 right-0 h-0.5 z-50 overflow-hidden bg-indigo-500/10">
              <div className="h-full w-2/5 bg-gradient-to-r from-transparent via-indigo-400 to-transparent animate-loading-bar" />
            </div>
          )}
          {activeTab === 'pipeline' && <PipelineTab nodes={pipeline} messages={messages} isFinished={status === 'finished'} status={status} />}
          {activeTab === 'results' && (
             <div className="flex-1 min-h-0 p-6 flex flex-col overflow-hidden">
                {isCompareMode ? (
                  <div className="flex-1 overflow-auto space-y-12 scrollbar-fancy pb-20">
                    {results.map((res, idx) => (
                      <div key={idx} className="flex flex-col gap-4 animate-fade-in" style={{ animationDelay: `${idx * 100}ms` }}>
                        <div className="flex items-center gap-4 px-2">
                          <div className="h-px flex-1 bg-gradient-to-r from-transparent via-[var(--border)] to-transparent" />
                          <div className="flex items-center gap-2">
                            <span className="text-[10px] font-bold font-display uppercase tracking-[0.3em] text-indigo-400/60">Result Set</span>
                            <span className="text-[14px] font-bold font-display text-indigo-400">{idx + 1}</span>
                          </div>
                          <div className="h-px flex-1 bg-gradient-to-r from-transparent via-[var(--border)] to-transparent" />
                        </div>
                        <div className="px-1">
                          <ResultGrid rows={res.rows} columns={res.columns} />
                        </div>
                      </div>
                    ))}
                    
                    {/* Floating Toggle to exit Compare Mode */}
                    <div className="fixed bottom-10 left-1/2 -translate-x-1/2 z-50">
                       <button 
                          onClick={() => setIsCompareMode(false)}
                          className="flex items-center gap-2 bg-indigo-500 text-white px-4 py-2 rounded-full shadow-lg shadow-indigo-500/40 hover:bg-indigo-600 transition-all font-display font-bold uppercase tracking-wider text-[10px]"
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
                        <div className="mt-4 flex items-center justify-center gap-4 bg-[var(--bg-darker)]/80 backdrop-blur-md rounded-full px-4 py-2 border border-[var(--border)] self-center animate-slide-up shadow-xl">
                          <button 
                            onClick={() => setSelectedResultIndex(Math.max(0, selectedResultIndex - 1))}
                            disabled={selectedResultIndex === 0}
                            className="text-[var(--muted)] hover:text-indigo-400 disabled:opacity-20 disabled:cursor-not-allowed transition-colors"
                          >
                            <ChevronLeft size={18} />
                          </button>
                          
                          <span className="text-[10px] font-bold font-display uppercase tracking-widest text-[var(--muted)] min-w-[120px] text-center">
                            Result <span className="text-indigo-400">{selectedResultIndex + 1}</span> of <span className="text-[var(--text-primary)]">{results.length}</span>
                          </span>

                          <button 
                            onClick={() => setSelectedResultIndex(Math.min(results.length - 1, selectedResultIndex + 1))}
                            disabled={selectedResultIndex === results.length - 1}
                            className="text-[var(--muted)] hover:text-indigo-400 disabled:opacity-20 disabled:cursor-not-allowed transition-colors"
                          >
                            <ChevronRight size={18} />
                          </button>

                          <div className="w-px h-4 bg-[var(--border)] mx-1" />

                          <button 
                            onClick={() => setIsCompareMode(true)}
                            className={`flex items-center gap-2 px-2 py-1 rounded transition-all hover:bg-white/5 ${isCompareMode ? 'text-indigo-400' : 'text-[var(--muted)]'}`}
                            title="Compare All Results"
                          >
                            <LayoutList size={14} />
                            <span className="text-[9px] font-bold uppercase tracking-tighter">Compare</span>
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
      <footer className="flex items-center justify-between px-3 h-[24px] border-t border-[var(--border)] bg-[var(--bg-darker)]/60 backdrop-blur-xl z-20 shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 group shrink-0">
            <div className={`w-2 h-2 rounded-full ${statusConfig[status].color} ${statusConfig[status].shadow} transition-all duration-500`} />
            <span className="text-[9px] font-bold font-display uppercase tracking-widest text-[var(--text)] opacity-60">
              {statusConfig[status].label}
            </span>
            {status === 'running' && (
              <button 
                onClick={() => postMessage({ type: 'cancel' })}
                className="flex items-center gap-1.5 px-2 py-0.5 rounded bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 transition-all text-[8px] font-bold uppercase tracking-tighter"
                title="Stop Execution"
              >
                <Square size={10} fill="currentColor" /> Stop
              </button>
            )}
          </div>
        </div>

        <div className="flex items-center gap-4 text-[13px] font-mono font-bold text-indigo-400">
           <span className="flex items-center gap-1.5"><Database size={11} className="text-slate-500" />{perf?.rowsProcessed.toLocaleString() || '0'}</span>
           <span className="flex items-center gap-1.5"><RefreshCw size={11} className="text-slate-500" />{formatDuration(perf?.executionMs || 0)}</span>
        </div>
      </footer>
    </ErrorBoundary>
  );
}

const EmptyState = ({ icon: Icon, message }: { icon: any, message: string }) => (
  <div className="flex-1 flex flex-col items-center justify-center opacity-20 space-y-4">
    <Icon size={48} strokeWidth={1} />
    <p className="text-sm font-display font-bold uppercase tracking-widest">{message}</p>
  </div>
);

export default App;
