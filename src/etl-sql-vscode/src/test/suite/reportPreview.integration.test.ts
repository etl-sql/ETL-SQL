import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';
import { ReportPreviewPanel } from '../../reportPreviewPanel';

suite('Report preview module loading', () => {
    test('renders through the production webview CSP with sibling modules', async () => {
        const extension = vscode.extensions.getExtension('etl-sql.etl-sql-vscode');
        assert.ok(extension);
        const panel = vscode.window.createWebviewPanel('etlsql.runtimeLoadTest',
            'Runtime module verification', vscode.ViewColumn.One, {
                enableScripts: true,
                localResourceRoots: [vscode.Uri.joinPath(extension.extensionUri, 'media')],
            });
        // Use the production HTML renderer with a fixed manifest, avoiding a CLI/server dependency.
        const preview = Object.assign(Object.create(ReportPreviewPanel.prototype), {
            _panel: panel, _extensionUri: extension.extensionUri,
        }) as { _getReportHtml(manifest: Record<string, unknown>): string };
        const html = preview._getReportHtml({
            title: 'Module graph preview', parameters: {}, pages: [],
            visuals: [{ name: 'Revenue', visualType: 'CARD', columns: ['Value'], rows: [['120']] }],
        });
        const nonce = /<script nonce="([^"]+)"/.exec(html)?.[1];
        assert.ok(nonce);
        let listener: vscode.Disposable | undefined;
        let timer: ReturnType<typeof setTimeout> | undefined;
        try {
            const result = new Promise<{ text: string; runtime: boolean; violations: string[] }>((resolve, reject) => {
                timer = setTimeout(() => reject(new Error('Preview did not finish loading its module graph.')), 15_000);
                listener = panel.webview.onDidReceiveMessage(message => {
                    if (message.type === 'moduleVerification') { resolve(message); }
                    if (message.type === 'log' && message.level === 'error') { reject(new Error(message.message)); }
                });
            });
            panel.webview.html = html.replace('</body>', `<script nonce="${nonce}">
                const violations = [];
                document.addEventListener('securitypolicyviolation', e => violations.push(e.violatedDirective));
                window.addEventListener('load', () => {
                    acquireVsCodeApi().postMessage({
                        type: 'moduleVerification',
                        text: document.getElementById('root').textContent,
                        runtime: !!window.__reportRuntime__, violations,
                    });
                });
            </script></body>`);
            const loaded = await result;
            assert.equal(loaded.runtime, true);
            assert.match(loaded.text, /Revenue/);
            assert.match(loaded.text, /120/);
            assert.deepEqual(loaded.violations, []);
        } finally {
            clearTimeout(timer);
            listener?.dispose();
            panel.dispose();
        }
    });
});
