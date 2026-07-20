import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import { ResultsPanel } from '../resultsPanel';

// Mock fs to intercept reading of the React UI index.html
vi.mock('fs', () => ({
    readFileSync: vi.fn().mockImplementation((path: string) => {
        if (path.endsWith('index.html')) {
            return '<html><head></head><body><div id="root"></div></body></html>';
        }
        throw new Error('File not found');
    }),
    promises: {
        readFile: vi.fn().mockImplementation((path: string) => {
            if (path.endsWith('index.html') || path.endsWith('welcome.html')) {
                return Promise.resolve('<html><head></head><body><div id="root"></div></body></html>');
            }
            return Promise.reject(new Error('File not found'));
        })
    }
}));

describe('ResultsPanel Webview Provider', () => {
    let mockContext: vscode.ExtensionContext;
    let provider: ResultsPanel;

    beforeEach(() => {
        vi.restoreAllMocks();
        ResultsPanel.currentPanel = undefined;

        mockContext = {
            subscriptions: [],
            extensionUri: vscode.Uri.file('C:/mock/extension')
        } as any;

        // Register the provider
        provider = ResultsPanel.register(mockContext);
    });

    const createMockWebviewView = () => {
        let messageHandler: ((msg: any) => void) | undefined;
        let disposeHandler: (() => void) | undefined;

        const webview = {
            options: {},
            html: '',
            cspSource: 'vscode-resource:',
            onDidReceiveMessage: vi.fn().mockImplementation((cb) => {
                messageHandler = cb;
                return { dispose: () => {} };
            }),
            postMessage: vi.fn().mockResolvedValue(true)
        };

        const webviewView = {
            webview,
            onDidDispose: vi.fn().mockImplementation((cb) => {
                disposeHandler = cb;
                return { dispose: () => {} };
            }),
            show: vi.fn()
        };

        return {
            webviewView,
            triggerMessage: (msg: any) => messageHandler?.(msg),
            triggerDispose: () => disposeHandler?.()
        };
    };

    it('should register successfully and populate static reference', () => {
        expect(ResultsPanel.currentPanel).toBe(provider);
        expect(mockContext.subscriptions).toHaveLength(1);
        expect(vscode.window.registerWebviewViewProvider).toHaveBeenCalledWith(
            'etlsql-results-view',
            provider,
            {
                webviewOptions: {
                    retainContextWhenHidden: true
                }
            }
        );
    });

    it('should generate HTML with nonce, CSP, and window variables', async () => {
        const { webviewView } = createMockWebviewView();

        await provider.resolveWebviewView(webviewView as any);

        const html = webviewView.webview.html;
        expect(html).toContain('<meta http-equiv="Content-Security-Policy"');
        expect(html).toContain("window.VIEW_TYPE = 'results'");
        expect(html).toContain('nonce=');
        expect(html).toContain('style-src vscode-resource:');
    });

    it('should queue postMessage when not ready and flush once ready', async () => {
        const { webviewView, triggerMessage } = createMockWebviewView();

        // 1. Post a message while not resolved/ready
        ResultsPanel.postMessage({ type: 'query_success', data: [1, 2, 3] });

        // Verify it triggers showing/focusing panel command
        expect(vscode.commands.executeCommand).toHaveBeenCalledWith(
            'workbench.view.extension.etlsql-panel'
        );

        // 2. Resolve the webview
        await provider.resolveWebviewView(webviewView as any);
        expect(webviewView.webview.postMessage).not.toHaveBeenCalled();

        // 3. Send the 'ready' signal from the webview
        triggerMessage({ type: 'ready' });

        // Verify queued message is flushed
        expect(webviewView.webview.postMessage).toHaveBeenCalledWith({
            type: 'query_success',
            data: [1, 2, 3]
        });
    });

    it('should deliver postMessage immediately if ready', async () => {
        const { webviewView, triggerMessage } = createMockWebviewView();

        await provider.resolveWebviewView(webviewView as any);
        triggerMessage({ type: 'ready' });

        ResultsPanel.postMessage({ type: 'clear' });

        expect(webviewView.webview.postMessage).toHaveBeenCalledWith({ type: 'clear' });
    });

    it('should trigger message received handlers', async () => {
        const { webviewView, triggerMessage } = createMockWebviewView();
        const handler = vi.fn();

        ResultsPanel.setOnMessageReceived(handler);
        await provider.resolveWebviewView(webviewView as any);

        const testMsg = { type: 'custom', payload: 'hello' };
        triggerMessage(testMsg);

        expect(handler).toHaveBeenCalledWith(testMsg);
    });
});
