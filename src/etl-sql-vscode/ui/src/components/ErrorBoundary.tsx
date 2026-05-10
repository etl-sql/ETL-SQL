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
        <div className="flex-1 bg-[var(--bg)] p-4 text-[var(--text)]">
          <div className="max-w-3xl border border-[var(--vscode-inputValidation-errorBorder,#be1100)] bg-[var(--vscode-inputValidation-errorBackground,transparent)]">
            <div className="flex items-center gap-2 border-b border-[var(--border)] px-3 py-2">
              <AlertTriangle className="text-[var(--color-err)]" size={16} />
              <h1 className="text-[13px] font-semibold">UI rendering failed</h1>
            </div>

            <div className="p-3">
              <p className="text-[12px] text-[var(--muted)] mb-3">
                The application encountered an unexpected data format from the ETL-SQL engine.
              </p>

              <pre className="text-left text-[11px] font-mono text-[var(--color-err)] overflow-auto max-h-48 leading-relaxed border border-[var(--border)] bg-[var(--vscode-editor-background,var(--bg-darker))] p-2 mb-3">
                {this.state.error?.stack || this.state.error?.message}
              </pre>

              <button
                onClick={() => window.location.reload()}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 bg-[var(--vscode-button-background,#0e639c)] hover:bg-[var(--vscode-button-hoverBackground,#1177bb)] text-[var(--vscode-button-foreground,#fff)] text-[12px]"
              >
                <RefreshCcw size={13} />
                Reload UI
              </button>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
