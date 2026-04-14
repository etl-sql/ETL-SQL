import * as vscode from 'vscode';

export class ResultsPanel implements vscode.WebviewViewProvider {
    public static readonly viewType = 'etlsql-results-view';
    public static currentPanel: ResultsPanel | undefined;

    private _view?: vscode.WebviewView;
    private _extensionUri: vscode.Uri;
    private _isReady: boolean = false;
    private _messageQueue: any[] = [];
    private _onMessageReceived?: (message: any) => void;

    private constructor(extensionUri: vscode.Uri) {
        this._extensionUri = extensionUri;
    }

    public static register(context: vscode.ExtensionContext): ResultsPanel {
        const provider = new ResultsPanel(context.extensionUri);
        context.subscriptions.push(
            vscode.window.registerWebviewViewProvider(ResultsPanel.viewType, provider)
        );
        ResultsPanel.currentPanel = provider;
        return provider;
    }

    public static setOnMessageReceived(handler: (message: any) => void) {
        if (ResultsPanel.currentPanel) {
            ResultsPanel.currentPanel._onMessageReceived = handler;
        }
    }

    public resolveWebviewView(
        webviewView: vscode.WebviewView,
        context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ) {
        this._view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [
                this._extensionUri
            ]
        };

        webviewView.webview.html = this._getHtmlForWebview(webviewView.webview);

        webviewView.webview.onDidReceiveMessage(message => {
            if (message.type === 'ready') {
                this._isReady = true;
                this._flushQueue();
            }
            if (this._onMessageReceived) {
                this._onMessageReceived(message);
            }
        });

        webviewView.onDidDispose(() => {
            this._isReady = false;
        });
    }

    public static postMessage(message: any) {
        if (ResultsPanel.currentPanel) {
            if (ResultsPanel.currentPanel._isReady && ResultsPanel.currentPanel._view) {
                ResultsPanel.currentPanel._view.show?.(true); 
                ResultsPanel.currentPanel._view.webview.postMessage(message);
            } else {
                ResultsPanel.currentPanel._messageQueue.push(message);
                vscode.commands.executeCommand('workbench.view.extension.etlsql-panel');
            }
        }
    }

    private _flushQueue() {
        if (!this._view) return;
        while (this._messageQueue.length > 0) {
            const msg = this._messageQueue.shift();
            this._view.webview.postMessage(msg);
        }
    }

    private _getHtmlForWebview(webview: vscode.Webview) {
        const scriptPath = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'tabulator.min.js'));
        const stylePath = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'tabulator.min.css'));
        const chartPath = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'chart.min.js'));
        const xlsxPath = webview.asWebviewUri(vscode.Uri.joinPath(this._extensionUri, 'media', 'xlsx.full.min.js'));

        const nonce = getNonce();
        
        return `<!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:;">
                <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
                <link href="${stylePath}" rel="stylesheet">
                <style>
                    :root {
                        --glass: rgba(255, 255, 255, 0.03);
                        --glass-border: rgba(255, 255, 255, 0.08);
                        --primary: #6366f1;
                        --primary-glow: rgba(99, 102, 241, 0.4);
                        --accent: #3b82f6;
                        --bg: var(--vscode-sideBar-background);
                        --text: var(--vscode-editor-foreground);
                        --muted: var(--vscode-descriptionForeground);
                        --text-dim: #888;
                    }

                    body { 
                        padding: 0; 
                        margin: 0;
                        color: var(--text); 
                        font-family: 'Inter', var(--vscode-font-family), sans-serif; 
                        background: transparent;
                        height: 100vh;
                        width: 100vw;
                        overflow: hidden;
                    }

                    .main-layout {
                        display: flex;
                        height: 100vh;
                    }

                    #side-nav {
                        width: 140px;
                        background: rgba(0,0,0,0.15);
                        border-right: 1px solid var(--glass-border);
                        display: flex;
                        flex-direction: column;
                        padding: 8px 0;
                    }

                    .nav-item {
                        padding: 6px 12px;
                        cursor: pointer;
                        font-size: 11px;
                        font-weight: 500;
                        color: var(--muted);
                        display: flex;
                        align-items: center;
                        gap: 8px;
                        transition: all 0.2s;
                    }

                    .nav-item:hover { color: var(--text); background: rgba(255,255,255,0.02); }
                    .nav-item.active { 
                        color: var(--primary); 
                        background: rgba(99, 102, 241, 0.05);
                        border-right: 2px solid var(--primary);
                    }

                    .content-pane { 
                        flex: 1; 
                        position: relative; 
                        overflow: hidden; 
                        display: flex;
                        flex-direction: column;
                    }

                    .section { 
                        display: none; 
                        flex: 1;
                        flex-direction: column;
                        padding: 12px;
                        box-sizing: border-box;
                    }
                    .section.active { display: flex; animation: slideUp 0.3s cubic-bezier(0.4, 0, 0.2, 1); }

                    @keyframes slideUp { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: translateY(0); } }

                    /* Pipeline Tree Styles (ULTRA DENSE) */
                    #pipeline-view { gap: 4px; overflow-y: auto; display: flex; flex-direction: column; flex: 1; }
                    .node-card {
                        background: var(--glass);
                        border: 1px solid var(--glass-border);
                        border-radius: 4px;
                        padding: 4px 8px;
                        backdrop-filter: blur(20px);
                        position: relative;
                        overflow: hidden;
                        margin-bottom: 2px;
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        font-size: 10px;
                    }
                    .node-card.Running::before {
                        content: '';
                        position: absolute;
                        top: 0; left: 0; right: 0; height: 1.5px;
                        background: linear-gradient(90deg, transparent, var(--primary), transparent);
                        animation: flow 2s infinite linear;
                    }
                    @keyframes flow { from { transform: translateX(-100%); } to { transform: translateX(100%); } }

                    .stat-val-inline { font-weight: 700; color: var(--text); margin-left: 2px; }
                    .stat-label-inline { color: var(--muted); margin-left: 8px; }

                    /* Results Grid */
                    #results-view { padding: 0; }
                    .results-toolbar {
                        padding: 8px 12px;
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        background: rgba(0,0,0,0.1);
                        border-bottom: 1px solid var(--glass-border);
                    }
                    #results-grid-container { flex: 1; overflow: hidden; }
                    .tabulator { border: none !important; font-size: 11px !important; }

                    /* Performance Dashboard */
                    .perf-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 8px; margin-bottom: 16px; }
                    .stat-box { background: var(--glass); padding: 12px; border-radius: 8px; border: 1px solid var(--glass-border); text-align: center; }
                    .stat-val { font-size: 20px; font-weight: 700; color: var(--primary); }
                    .stat-label { font-size: 9px; color: var(--muted); text-transform: uppercase; }

                    #message-stream, #trace-stream {
                        background: rgba(0,0,0,0.1);
                        border-radius: 4px;
                        padding: 12px;
                        font-family: var(--vscode-editor-font-family), monospace;
                        font-size: 11px;
                        color: #bbb;
                        flex: 1;
                        overflow-y: auto;
                    }
                    .msg-info { color: #818cf8; }
                    .msg-warn { color: #fbbf24; }
                    .msg-err { color: #f87171; }
                    .msg-sys { color: #2dd4bf; opacity: 0.8; font-style: italic; }

                    .trace-line { border-bottom: 1px solid rgba(255,255,255,0.02); padding: 2px 0; white-space: pre-wrap; word-break: break-all; opacity: 0.8; }
                </style>
            </head>
            <body data-build-id="DIAGNOSTIC-2026-04-10-02-00">
                <div class="main-layout">
                    <div id="side-nav">
                        <div id="nav-pipeline" class="nav-item active" data-target="pipeline"><span class="icon">🌿</span> Pipeline</div>
                        <div id="nav-results" class="nav-item" data-target="results"><span class="icon">📊</span> Results</div>
                        <div id="nav-performance" class="nav-item" data-target="performance"><span class="icon">⚡</span> Performance</div>
                        <div id="nav-messages" class="nav-item" data-target="messages"><span class="icon">💬</span> Messages</div>
                        <div id="nav-trace" class="nav-item" data-target="trace"><span class="icon">🔍</span> Trace</div>
                        <div style="flex: 1"></div>
                        <div id="build-id" style="font-size: 10px; color: var(--text-dim); padding: 10px; opacity: 0.5;">Build: DIAGNOSTIC-2026-04-10-02-00</div>
                    </div>

                    <div class="content-pane">
                        <div id="pipeline-section" class="section active">
                            <div id="pipeline-view">
                                <div style="text-align: center; margin-top: 50px; opacity: 0.3;">
                                    <div style="font-size: 30px;">🛸</div>
                                    <div style="font-size: 11px;">Waiting for engine...</div>
                                </div>
                            </div>
                        </div>

                        <div id="results-section" class="section">
                            <div class="results-toolbar">
                                <div id="results-count" style="font-size: 10px; opacity: 0.6;">0 rows returned</div>
                                <div style="display: flex; gap: 8px; align-items: center;">
                                    <select id="dataset-selector" style="display: none; background: #222; color: #ccc; border: 1px solid #444; font-size: 10px;"></select>
                                    <button id="export-csv" style="background: var(--primary); color: white; border: none; border-radius: 3px; padding: 2px 8px; font-size: 10px; cursor: pointer;">Export CSV</button>
                                </div>
                            </div>
                            <div id="results-grid-container"></div>
                        </div>

                        <div id="performance-section" class="section">
                            <div class="perf-grid" id="perf-summary"></div>
                            <div style="flex: 1; min-height: 200px; background: rgba(0,0,0,0.1); border-radius: 8px; padding: 12px;">
                                <canvas id="perfChart"></canvas>
                            </div>
                        </div>

                        <div id="messages-section" class="section">
                            <div id="message-stream"></div>
                        </div>

                        <div id="trace-section" class="section">
                            <div id="trace-stream"></div>
                        </div>
                    </div>
                </div>

                <script nonce="${nonce}" src="${scriptPath}"></script>
                <script nonce="${nonce}" src="${chartPath}"></script>
                <script nonce="${nonce}" src="${xlsxPath}"></script>
                <script nonce="${nonce}">
                    const vscode = acquireVsCodeApi();
                    let grid;
                    let perfChart;
                    let resultsHistory = [];
                    let currentResultIndex = -1;

                    function showSection(target) {
                        document.querySelectorAll('.nav-item').forEach(t => t.classList.remove('active'));
                        document.querySelectorAll('.section').forEach(s => s.classList.remove('active'));
                        
                        document.getElementById('nav-' + target).classList.add('active');
                        document.getElementById(target + '-section').classList.add('active');
                        
                        if (target === 'results' && grid) setTimeout(() => grid.redraw(true), 10);
                    }

                    document.querySelectorAll('.nav-item').forEach(item => {
                        item.addEventListener('click', () => {
                            showSection(item.getAttribute('data-target'));
                        });
                    });

                    document.getElementById('dataset-selector').addEventListener('change', (e) => loadResult(parseInt(e.target.value)));

                    document.getElementById('export-csv').addEventListener('click', () => {
                        if (grid) {
                            grid.download("csv", \`results_\${new Date().getTime()}.csv\`);
                        }
                    });

                    vscode.postMessage({ type: 'ready' });

                    window.addEventListener('message', event => {
                        const message = event.data;
                        
                        // LOG TO TRACE
                        const trace = document.getElementById('trace-stream');
                        if (trace) {
                            const line = document.createElement('div');
                            line.className = 'trace-line';
                            line.textContent = \`[\${new Date().toLocaleTimeString()}] \${JSON.stringify(message)}\`;
                            trace.appendChild(line);
                            if (trace.childElementCount > 100) trace.removeChild(trace.firstChild);
                            trace.scrollTop = trace.scrollHeight;
                        }

                        switch (message.type) {
                            case 'clear':
                                resultsHistory = [];
                                currentResultIndex = -1;
                                document.getElementById('message-stream').innerHTML = '';
                                document.getElementById('pipeline-view').innerHTML = '';
                                document.getElementById('perf-summary').innerHTML = '';
                                document.getElementById('results-count').textContent = 'Cleaning up...';
                                if (grid) grid.setData([]);
                                break;
                            case 'status':
                                addLog(\`System Ready: \${message.buildId || 'v1.0'}\`, 'sys');
                                break;
                            case 'message':
                                addLog(message.text, message.level);
                                break;
                            case 'progress':
                                updatePipeline(message.data);
                                break;
                            case 'results':
                                resultsHistory.push(message);
                                updateSelector();
                                if (currentResultIndex === -1) loadResult(0);
                                break;
                            case 'performance':
                                drawPerformance(message.metrics || message.data);
                                break;
                        }
                    });

                    function addLog(text, level) {
                        const stream = document.getElementById('message-stream');
                        if (!stream) return;
                        const row = document.createElement('div');
                        row.className = 'msg-' + (level || 'info');
                        row.textContent = \`[\${new Date().toLocaleTimeString()}] \${text}\`;
                        stream.appendChild(row);
                        stream.scrollTop = stream.scrollHeight;
                    }

                    function updateSelector() {
                        const sel = document.getElementById('dataset-selector');
                        if (resultsHistory.length <= 1) { sel.style.display = 'none'; return; }
                        sel.style.display = 'block';
                        sel.innerHTML = '';
                        resultsHistory.forEach((r, i) => {
                            const opt = document.createElement('option');
                            opt.value = i;
                            opt.textContent = \`Result Set \${i+1}\`;
                            sel.appendChild(opt);
                        });
                        sel.value = currentResultIndex;
                    }

                    function loadResult(idx) {
                        currentResultIndex = idx;
                        const data = resultsHistory[idx];
                        if (!grid) {
                            grid = new Tabulator("#results-grid-container", {
                                data: data.rows,
                                autoColumns: true,
                                layout: "fitColumns",
                                pagination: "local",
                                paginationSize: 50,
                                maxHeight: "100%",
                            });
                        } else {
                            grid.setData(data.rows);
                        }
                        document.getElementById('results-count').textContent = \`\${data.rows.length} rows returned\`;
                    }

                    function updatePipeline(snap) {
                        const view = document.getElementById('pipeline-view');
                        if (!snap || !view) return;
                        
                        const roots = Array.isArray(snap) ? snap : (snap.roots || []);
                        if (roots.length === 0) return;

                        view.innerHTML = '';
                        const walk = (node) => {
                            const card = document.createElement('div');
                            card.className = 'node-card ' + (node.status || 'Pending');
                            
                            const rows = node.rows !== undefined ? node.rows : (node.rowsProcessed || 0);
                            const duration = node.durationMs !== undefined ? node.durationMs : (node.executionTimeMs || 0);

                            card.innerHTML = \`
                                <span style="font-weight:600; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">\${node.name}</span>
                                <span class="stat-label-inline">Rows:</span><span class="stat-val-inline">\${rows.toLocaleString()}</span>
                                <span class="stat-label-inline">Time:</span><span class="stat-val-inline">\${duration}ms</span>
                                <span style="margin-left: 12px; font-size: 9px; opacity: 0.5; width: 60px; text-align: right;">\${(node.status || 'Pending').toUpperCase()}</span>
                            \`;
                            view.appendChild(card);
                            if (node.children) node.children.forEach(walk);
                        };
                        roots.forEach(walk);
                    }

                    function drawPerformance(perf) {
                        if (!perf) return;
                        const summary = document.getElementById('perf-summary');
                        if (!summary) return;
                        summary.innerHTML = \`
                            <div class="stat-box">
                                <div class="stat-val">\${perf.executionMs || 0}ms</div>
                                <div class="stat-label">Exec</div>
                            </div>
                            <div class="stat-box">
                                <div class="stat-val">\${(perf.rowsProcessed || 0).toLocaleString()}</div>
                                <div class="stat-label">Total Rows</div>
                            </div>
                            <div class="stat-box">
                                <div class="stat-val">\${perf.memoryMb || 0}MB</div>
                                <div class="stat-label">Memory Usage</div>
                            </div>
                        \`;

                        const ctx = document.getElementById('perfChart').getContext('2d');
                        if (perfChart) perfChart.destroy();
                        
                        const statements = perf.statements || [];
                        perfChart = new Chart(ctx, {
                            type: 'bar',
                            data: {
                                labels: statements.map(s => s.type || s.statementType || 'Statement'),
                                datasets: [{
                                    label: 'Duration (ms)',
                                    data: statements.map(s => s.totalMs || s.durationMs || 0),
                                    backgroundColor: '#6366f1cc',
                                    borderRadius: 8
                                }]
                            },
                            options: { 
                                responsive: true, 
                                maintainAspectRatio: false,
                                plugins: { legend: { display: false } },
                                scales: { 
                                    x: { grid: { display: false }, ticks: { color: '#888', font: { size: 10 } } },
                                    y: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#888', font: { size: 10 } } }
                                }
                            }
                        });
                    }
                </script>
            </body>
            </html>`;
    }
}

function getNonce() {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}
