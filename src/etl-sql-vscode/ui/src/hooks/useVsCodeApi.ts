import { useEffect, useCallback, useMemo, useReducer, useState } from 'react';
import type { ProtocolMessage } from '../types';
import { mockTrace } from '../mock_protocol';
declare global {
    interface Window {
        __INITIAL_STATE__?: {
            messages?: ProtocolMessage[];
        };
    }
}

type MsgState = { messages: ProtocolMessage[]; runHistory: ProtocolMessage[][]; scriptUri?: string };
type MsgAction =
    | { type: 'append'; message: ProtocolMessage }
    | { type: 'clear'; resetHistory?: boolean; scriptUri?: string }
    | { type: 'reset' };

function msgReducer(state: MsgState, action: MsgAction): MsgState {
    switch (action.type) {
        case 'append':
            return { ...state, messages: [...state.messages, action.message] };
        case 'clear': {
            if (action.resetHistory) {
                return { messages: [], runHistory: [], scriptUri: state.scriptUri };
            }
            // If the executing script changed, discard history from the old script.
            const scriptChanged = action.scriptUri !== undefined && action.scriptUri !== state.scriptUri;
            return {
                messages: [],
                scriptUri: action.scriptUri ?? state.scriptUri,
                runHistory: (!scriptChanged && state.messages.length > 0)
                    ? [...state.runHistory, state.messages]
                    : [],
            };
        }
        case 'reset':
            return { messages: [], runHistory: state.runHistory, scriptUri: state.scriptUri };
    }
}

/**
 * Hook to handle communication with VS Code webview or use mock data in dev mode.
 */
export function useVsCodeApi() {
    const [{ messages, runHistory }, dispatch] = useReducer(msgReducer, {
        messages: (() => {
            if (typeof window !== 'undefined' && window.__INITIAL_STATE__?.messages) {
                return window.__INITIAL_STATE__.messages;
            }
            return [];
        })(),
        runHistory: [],
    });
    const [status, setStatus] = useState<'ready' | 'running' | 'finished' | 'error'>('ready');
    const [isDev] = useState(import.meta.env.DEV);

    // acquireVsCodeApi can only be called once
    const vscode = useMemo(() => {
        if (typeof acquireVsCodeApi !== 'undefined') {
            return acquireVsCodeApi();
        }
        return null;
    }, []);

    const postMessage = useCallback((message: any) => {
        if (vscode) {
            vscode.postMessage(message);
        } else {
            console.log('[Mock PostMessage]', message);
        }
    }, [vscode]);

    useEffect(() => {
        if (!isDev) {
            postMessage({ type: 'ready' });
        }
    }, [isDev, postMessage]);

    useEffect(() => {
        if (isDev) {
            // Simulator for development
            const params = new URLSearchParams(window.location.search);
            const view = params.get('view');

            if (view === 'sidebar') {
                // For sidebar, send metadata immediately
                const metaMessages = mockTrace.filter(m =>
                    ['connections', 'scriptConnections', 'variables', 'activeEditorChanged'].includes(m.type)
                );
                metaMessages.forEach(m => dispatch({ type: 'append', message: m }));
                setStatus('ready');
                return;
            }

            setStatus('running');
            let index = 0;
            const interval = setInterval(() => {
                if (index < mockTrace.length) {
                    dispatch({ type: 'append', message: mockTrace[index] });
                    index++;
                } else {
                    setStatus('finished');
                    clearInterval(interval);
                }
            }, 400);
            return () => clearInterval(interval);
        } else {
            const handler = (event: MessageEvent) => {
                const message = event.data as ProtocolMessage;
                if (message.type === 'clear') {
                    dispatch({ type: 'clear', resetHistory: message.resetHistory, scriptUri: message.scriptUri });
                    setStatus('ready');
                } else if (message.type === 'status') {
                    setStatus(message.status);
                } else if (message.type === 'done') {
                    setStatus(message.exitCode === 0 ? 'finished' : 'error');
                } else {
                    dispatch({ type: 'append', message });
                }
            };
            window.addEventListener('message', handler);
            return () => window.removeEventListener('message', handler);
        }
    }, [isDev]);

    const rerun = useCallback(() => {
        dispatch({ type: 'reset' });
        setStatus('running');
        if (!isDev) {
            postMessage({ type: 'ready' });
        }
    }, [isDev, postMessage]);

    return { messages, runHistory, status, postMessage, isDev, rerun };
}

// VS Code API type definition
declare function acquireVsCodeApi(): {
    postMessage(message: any): void;
    getState(): any;
    setState(state: any): void;
};
