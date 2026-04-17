import { useState, useEffect, useCallback, useMemo } from 'react';
import type { ProtocolMessage } from '../types';
import { mockTrace } from '../mock_protocol';

/**
 * Hook to handle communication with VS Code webview or use mock data in dev mode.
 */
export function useVsCodeApi() {
    const [messages, setMessages] = useState<ProtocolMessage[]>([]);
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
            setStatus('running');
            let index = 0;
            const interval = setInterval(() => {
                if (index < mockTrace.length) {
                    const msg = mockTrace[index];
                    setMessages(prev => [...prev, msg]);
                    index++;
                } else {
                    setStatus('finished');
                    clearInterval(interval);
                }
            }, 1000);
            return () => clearInterval(interval);
        } else {
            const handler = (event: MessageEvent) => {
                const message = event.data as ProtocolMessage;
                if (message.type === 'clear') {
                    setMessages([]);
                    setStatus('running');
                } else if (message.type === 'done') {
                    setStatus(message.exitCode === 0 ? 'finished' : 'error');
                } else {
                    setMessages(prev => [...prev, message]);
                }
            };
            window.addEventListener('message', handler);
            return () => window.removeEventListener('message', handler);
        }
    }, [isDev]);

    const rerun = useCallback(() => {
        if (isDev) {
            setMessages([]);
            setStatus('running');
        } else {
            setMessages([]);
            setStatus('running');
            postMessage({ type: 'ready' });
        }
    }, [isDev, postMessage]);

    return { messages, status, postMessage, isDev, rerun };
}

// VS Code API type definition
declare function acquireVsCodeApi(): {
    postMessage(message: any): void;
    getState(): any;
    setState(state: any): void;
};
