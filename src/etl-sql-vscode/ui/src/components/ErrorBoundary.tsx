import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertTriangle, RefreshCcw } from 'lucide-react';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  public state: State = {
    hasError: false,
    error: null
  };

  public static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Uncaught error:', error, errorInfo);
  }

  public render() {
    if (this.state.hasError) {
      return (
        <div className="flex-1 flex flex-col items-center justify-center bg-[var(--bg)] p-8 text-center">
          <div className="w-16 h-16 rounded-full bg-red-500/10 flex items-center justify-center mb-6">
            <AlertTriangle className="text-red-500" size={32} />
          </div>
          <h1 className="text-xl font-display font-bold text-white mb-2 uppercase tracking-widest">UI Rendering Crash</h1>
          <p className="text-[var(--muted)] text-sm max-w-md mb-8">
            The application encountered an unexpected data format and crashed. This is likely due to an unhandled message from the ETL-SQL engine.
          </p>
          
          <div className="w-full max-w-2xl bg-black/40 rounded-lg border border-red-500/20 p-4 mb-8 overflow-hidden">
            <div className="flex items-center gap-2 mb-2 border-b border-white/5 pb-2">
              <div className="w-2 h-2 rounded-full bg-red-500" />
              <span className="text-[10px] font-bold text-red-400 uppercase tracking-tighter">Error Trace</span>
            </div>
            <pre className="text-left text-xs font-mono text-red-500/80 overflow-auto max-h-40 leading-relaxed">
              {this.state.error?.stack || this.state.error?.message}
            </pre>
          </div>

          <button
            onClick={() => window.location.reload()}
            className="flex items-center gap-2 px-6 py-3 bg-indigo-600 hover:bg-indigo-500 text-white rounded-md font-bold text-[10px] uppercase tracking-widest transition-all active:scale-95"
          >
            <RefreshCcw size={14} />
            Reload UI
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}
