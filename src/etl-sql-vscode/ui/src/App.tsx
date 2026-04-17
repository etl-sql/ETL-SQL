import { useState, useMemo } from 'react';
import { useVsCodeApi } from './hooks/useVsCodeApi';
import { PipelineTab } from './components/PipelineTab';
import { ExecutionConsole } from './components/ExecutionConsole';
import { ResultGrid } from './components/ResultGrid';
import { PerformanceTab } from './components/PerformanceTab';
import { ErrorBoundary } from './components/ErrorBoundary';
import type { ResultsMessage, PerformanceMessage } from './types';
import { RefreshCw, BarChart3, Database, Terminal, Activity, GitBranch } from 'lucide-react';
import { extractPipelineNodes } from './utils/pipeline_utils';

type TabId = 'pipeline' | 'results' | 'messages' | 'performance';

function App() {
  const { messages, status } = useVsCodeApi();
  const [activeTab, setActiveTab] = useState<TabId>('pipeline');

  // Extract relevant state from messages with defensive checks
  const pipeline = useMemo(() => extractPipelineNodes(messages), [messages]);

  const latestResult = useMemo(() => {
    const resultMessages = messages.filter(m => m.type === 'results') as ResultsMessage[];
    return resultMessages.length > 0 ? resultMessages[resultMessages.length - 1] : null;
  }, [messages]);

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
    { id: 'results', label: 'Results', icon: Database, badge: latestResult?.rows.length },
    { id: 'messages', label: 'Messages', icon: Terminal },
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
        <nav className="sidebar-nav flex flex-col items-center w-14 shrink-0 bg-[var(--bg-darker)]/40 border-r border-[var(--border)] z-10">
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
                <tab.icon size={18} className={activeTab === tab.id ? 'text-indigo-400' : ''} />
                {tab.badge !== undefined && tab.badge > 0 && (
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
          {activeTab === 'pipeline' && <PipelineTab nodes={pipeline} isFinished={status === 'finished'} />}
          {activeTab === 'results' && (
             <div className="flex-1 min-h-0 p-6 flex flex-col overflow-hidden">
                {latestResult ? (
                  <ResultGrid rows={latestResult.rows} columns={latestResult.columns} />
                ) : (
                  <EmptyState icon={BarChart3} message="No Result Set Available" />
                )}
             </div>
          )}
          {activeTab === 'messages' && <ExecutionConsole messages={messages} />}
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
