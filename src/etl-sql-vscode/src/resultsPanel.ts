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
        
        // @TEMPLATE-START
        const html = `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src \${webview.cspSource} 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; script-src 'nonce-\${nonce}'; img-src \${webview.cspSource} data:;">
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
    <link href="\${stylePath}" rel="stylesheet">
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
            flex-direction: column;
        }

        #side-nav {
            background: rgba(15, 15, 20, 0.95);
            border-bottom: 1px solid var(--glass-border);
            display: flex;
            flex-direction: row;
            padding: 0 8px;
            height: 38px;
            flex-shrink: 0;
            z-index: 1000;
            position: relative;
            box-shadow: 0 2px 8px rgba(0,0,0,0.3);
            pointer-events: auto !important;
        }

        .nav-item {
            padding: 0 12px;
            cursor: pointer;
            font-size: 11px;
            font-weight: 500;
            color: var(--muted);
            display: flex;
            align-items: center;
            gap: 8px;
            transition: all 0.2s;
            height: 100%;
            user-select: none;
            pointer-events: auto !important;
        }

        .nav-item:hover { color: var(--text); background: rgba(255,255,255,0.05); }
        .nav-item.active {
            color: var(--primary);
            border-bottom: 2px solid var(--primary);
            background: rgba(99, 102, 241, 0.08);
        }

        .content-area {
            flex: 1;
            position: relative;
            overflow: hidden;
            display: flex;
            flex-direction: column;
            min-height: 0;
            z-index: 1;
        }

        .section {
            display: none;
            flex-direction: column;
            height: 100%;
            width: 100%;
            padding: 12px;
            box-sizing: border-box;
            overflow: hidden;
            min-height: 0;
        }
        .section.active { display: flex; }

        .icon { font-size: 14px; }` +
`       /* Performance Dashboard */
        .perf-grid {
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: 12px;
            margin-bottom: 16px;
        }
        .stat-box {
            background: var(--glass);
            border: 1px solid var(--glass-border);
            padding: 12px;
            border-radius: 8px;
            text-align: center;
        }
        .stat-val { font-size: 18px; font-weight: 700; color: var(--primary); }
        .stat-label { font-size: 10px; color: var(--muted); text-transform: uppercase; margin-top: 4px; }

        .chart-container {
            background: var(--glass);
            border: 1px solid var(--glass-border);
            border-radius: 8px;
            padding: 16px;
            flex: 1;
            margin-bottom: 12px;
            min-height: 200px;
        }

        #pipeline-view { gap: 4px;
            flex: 1;
            overflow-y: auto;
            overflow-x: hidden;
            margin-top: 8px;
            background: rgba(0,0,0,0.1);
            border-radius: 4px;
            padding: 8px;
            font-family: var(--vscode-editor-font-family), monospace;
            font-size: 12px;
            min-height: 0;
            display: flex;
            flex-direction: column;
        }

        #message-stream, #trace-stream {
            flex: 1;
            overflow-y: auto;
            overflow-x: hidden;
            margin-top: 8px;
            background: rgba(0,0,0,0.1);
            border-radius: 4px;
            padding: 8px;
            font-family: var(--vscode-editor-font-family), monospace;
            font-size: 12px;
            min-height: 0;
        }

        .msg-info { color: #888; margin-bottom: 4px; }
        .msg-err { color: #f87171; background: rgba(248, 113, 113, 0.1); padding: 4px; border-radius: 2px; }
        .msg-warn { color: #fbbf24; }
        .msg-sys { color: #6366f1; font-weight: bold; }

        .node-card {
            background: var(--glass);
            border: 1px solid var(--glass-border);
            border-left: 3px solid #888;
            padding: 8px 12px;
            margin-bottom: 2px;
            border-radius: 4px;
            display: flex;
            align-items: center;
        }
        .node-card.Running { border-left-color: #3b82f6; background: rgba(59, 130, 246, 0.05); }
        .node-card.Completed { border-left-color: #10b981; }
        .node-card.Error { border-left-color: #ef4444; }

        .stat-label-inline { font-size: 9px; color: var(--muted); margin-left: 12px; margin-right: 4px; text-transform: uppercase; }
        .stat-val-inline { font-size: 11px; font-weight: 500; }

        #results-grid-container { flex: 1; width: 100%; min-height: 0; }
        .trace-line { font-size: 10px; color: #555; border-bottom: 1px solid #222; padding: 2px 0; }
    </style>
</head>` +
` <body data-build-id="DIAGNOSTIC-2026-04-10-02-00">
    <div class="main-layout">
        <div id="side-nav">
            <div id="nav-pipeline" class="nav-item active" data-target="pipeline"><span class="icon">🌿</span> Pipeline</div>
            <div id="nav-results" class="nav-item" data-target="results"><span class="icon">📊</span> Results</div>
            <div id="nav-messages" class="nav-item" data-target="messages"><span class="icon">💬</span> Messages</div>
            <div id="nav-performance" class="nav-item" data-target="performance"><span class="icon">⚡</span> Performance</div>
            <div id="nav-trace" class="nav-item" data-target="trace"><span class="icon">🔍</span> Trace</div>
            <div id="build-id" style="display: none;">Build: DIAGNOSTIC-2026-04-10-02-00</div>
        </div>

        <div class="content-area">
            <div id="pipeline-section" class="section active">
                <div style="display: flex; justify-content: space-between; align-items: center;">
                    <h3 style="margin: 0; font-size: 12px; color: var(--muted);">Live Pipeline</h3>
                </div>
                <div id="pipeline-view"></div>
            </div>

            <div id="results-section" class="section">
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                    <select id="dataset-selector" style="background: var(--vscode-dropdown-background); color: var(--vscode-dropdown-foreground); border: 1px solid var(--glass-border); font-size: 11px; padding: 2px; display: none;"></select>
                    <span id="results-count" style="font-size: 11px; color: var(--muted);">0 rows</span>
                    <button id="export-csv" style="background: var(--primary); color: white; border: none; padding: 4px 8px; border-radius: 4px; font-size: 10px; cursor: pointer;">Export CSV</button>
                </div>
                <div id="results-grid-container"></div>
            </div>

            <div id="messages-section" class="section">
                <h3 style="margin: 0; font-size: 12px; color: var(--muted); margin-bottom: 8px;">Execution Logs</h3>
                <div id="message-stream"></div>
            </div>

            <div id="performance-section" class="section">
                <div id="perf-summary" class="perf-grid"></div>
                <div style="display: flex; flex-direction: column; gap: 12px; flex: 1; min-height: 0;">
                    <div class="chart-container">
                        <div style="font-size: 10px; color: var(--muted); margin-bottom: 8px; text-transform: uppercase; font-weight: 600;">Resource Distribution</div>
                        <div style="flex: 1; position: relative; height: 180px;"><canvas id="perfChart"></canvas></div>
                    </div>
                    <div class="chart-container" style="flex: 0 0 120px; min-height: 120px;">
                        <div style="font-size: 10px; color: var(--muted); margin-bottom: 4px; text-transform: uppercase; font-weight: 600;">Execution Trend</div>
                        <div style="flex: 1; position: relative; height: 80px;"><canvas id="trendChart"></canvas></div>
                    </div>
                </div>
            </div>

            <div id="trace-section" class="section">
                <h3 style="margin: 0; font-size: 12px; color: var(--muted);">Internal Protocol Trace</h3>
                <div id="trace-stream"></div>
            </div>
        </div>
    </div>` +
`   <script nonce="\${nonce}" src="\${scriptPath}"></script>
    <script nonce="\${nonce}" src="\${chartPath}"></script>
    <script nonce="\${nonce}" src="\${xlsxPath}"></script>
    <script nonce="\${nonce}">
        const vscode = acquireVsCodeApi();
        let grid = null;
        let resultsHistory = [];
        let currentResultIndex = -1;
        let perfChart = null;
        let trendChart = null;

        // --- DIAGNOSTICS ---
        window.addEventListener('click', (e) => {
            const target = e.target;
            const nav = target.closest('.nav-item');
            console.log('UI Click:', target.id, 'Nav:', nav ? nav.getAttribute('data-target') : 'none');
        });

        function showSection(target) {
            if (!target) return;
            const items = document.querySelectorAll('.nav-item');
            const sections = document.querySelectorAll('.section');
            
            const nav = document.getElementById('nav-' + target);
            const sec = document.getElementById(target + '-section');
            
            if (nav && sec) {
                items.forEach(t => t.classList.remove('active'));
                sections.forEach(s => s.classList.remove('active'));
                
                nav.classList.add('active');
                sec.classList.add('active');
                
                if (target === 'results' && grid) {
                    setTimeout(() => grid.redraw(), 10);
                }
            }
        }` +
`       document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                showSection(item.getAttribute('data-target'));
            });
        });

        document.getElementById('dataset-selector').addEventListener('change', (e) => loadResult(parseInt(e.target.value)));

        document.getElementById('export-csv').addEventListener('click', () => {
            if (grid) {
                grid.download('csv', 'results_' + new Date().getTime() + '.csv');
            }
        });

        vscode.postMessage({ type: 'ready' });

        // --- MESSAGE BUFFERING LAYER (CONSUMER PATTERN) ---
        const messageQueue = [];
        const MAX_BATCH_SIZE = 10; 

        window.addEventListener('message', event => {
            messageQueue.push(event.data);
        });

        setInterval(() => {
            if (messageQueue.length === 0) return;
            
            const batch = messageQueue.splice(0, MAX_BATCH_SIZE);
            batch.forEach(message => {
                try {
                    processOneMessage(message);
                } catch (err) {
                    console.error('UI Protocol Error:', err);
                }
            });
        }, 16);` +
`       function processOneMessage(message) {
            const trace = document.getElementById('trace-stream');
            if (trace) {
                const line = document.createElement('div');
                line.className = 'trace-line';
                line.textContent = '[' + new Date().toLocaleTimeString() + '] ' + JSON.stringify(message);
                trace.appendChild(line);
                if (trace.childElementCount > 100) trace.removeChild(trace.firstChild);
                trace.scrollTop = trace.scrollHeight;
            }

            switch (message.type) {
                case 'clear':
                    // 1. Cancel any pending throttled updates
                    if (window._pipelineTimer) {
                        clearTimeout(window._pipelineTimer);
                        window._pipelineTimer = null;
                    }
                    lastPipelineUpdate = 0;
                    pendingPipelineSnap = null;

                    // 2. Clear UI history and indices
                    resultsHistory = [];
                    currentResultIndex = -1;

                    // 3. Clear DOM elements
                    document.getElementById('message-stream').innerHTML = '';
                    document.getElementById('pipeline-view').innerHTML = '';
                    document.getElementById('perf-summary').innerHTML = '';
                    document.getElementById('trace-stream').innerHTML = '';
                    document.getElementById('results-count').textContent = 'Cleaning up...';
                    
                    // 4. Reset Grid and Charts
                    if (grid) grid.setData([]);
                    if (perfChart) { perfChart.destroy(); perfChart = null; }
                    if (trendChart) { trendChart.destroy(); trendChart = null; }
                    
                    // 5. Reset Selector visibility
                    updateSelector();
                    break;
                case 'status':
                    addLog('System Ready: ' + (message.buildId || 'v1.0'), 'sys');
                    break;
                case 'message':
                    addLog(message.text, message.level);
                    break;
                case 'progress':
                    throttledUpdatePipeline(message.data);
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
        }` +
`       let logBuffer = [];
        let logTimeout = null;
        let lastPipelineUpdate = 0;
        let pendingPipelineSnap = null;

        function addLog(text, level) {
            logBuffer.push({ text, level, time: new Date().toLocaleTimeString() });
            if (!logTimeout) {
                logTimeout = setTimeout(flushLogs, 100);
            }
        }

        function flushLogs() {
            const stream = document.getElementById('message-stream');
            if (!stream) return;
            
            const fragment = document.createDocumentFragment();
            logBuffer.forEach(m => {
                const row = document.createElement('div');
                row.className = 'msg-' + (m.level || 'info');
                row.textContent = '[' + m.time + '] ' + m.text;
                fragment.appendChild(row);
            });
            
            stream.appendChild(fragment);
            logBuffer = [];
            logTimeout = null;

            while (stream.childElementCount > 500) {
                stream.removeChild(stream.firstChild);
            }
            stream.scrollTop = stream.scrollHeight;
        }` +
`       function throttledUpdatePipeline(snap) {
            pendingPipelineSnap = snap;
            const now = Date.now();
            if (now - lastPipelineUpdate > 200) { 
                updatePipeline(snap);
                lastPipelineUpdate = now;
            } else if (!window._pipelineTimer) {
                window._pipelineTimer = setTimeout(() => {
                    updatePipeline(pendingPipelineSnap);
                    lastPipelineUpdate = Date.now();
                    window._pipelineTimer = null;
                }, 200);
            }
        }

        function updateSelector() {
            const sel = document.getElementById('dataset-selector');
            if (resultsHistory.length <= 1) { sel.style.display = 'none'; return; }
            sel.style.display = 'block';
            sel.innerHTML = '';
            resultsHistory.forEach((r, i) => {
                const opt = document.createElement('option');
                opt.value = i;
                opt.textContent = 'Result Set ' + (i+1);
                sel.appendChild(opt);
            });
            sel.value = currentResultIndex;
        }

        function loadResult(idx) {
            try {
                currentResultIndex = idx;
                const data = resultsHistory[idx];
                if (!grid) {
                    grid = new Tabulator('#results-grid-container', {
                        data: data.rows,
                        autoColumns: true,
                        layout: 'fitColumns',
                        pagination: 'local',
                        paginationSize: 50,
                        maxHeight: '100%',
                    });
                } else {
                    grid.setData(data.rows);
                }
                document.getElementById('results-count').textContent = data.rows.length + ' rows returned';
            } catch (err) {
                addLog('Grid Error: ' + err.message, 'err');
            }
        }` +
`       let _isPipelineUpdating = false;
        function updatePipeline(snap) {
            if (_isPipelineUpdating) return;
            const view = document.getElementById('pipeline-view');
            const section = document.getElementById('pipeline-section');
            if (!snap || !view || !section || !section.classList.contains('active')) return;
            const roots = Array.isArray(snap) ? snap : (snap.roots || []);
            if (roots.length === 0) return;
            _isPipelineUpdating = true;
            try {
                view.innerHTML = '';
                const walk = (node) => {
                    const card = document.createElement('div');
                    card.className = 'node-card ' + (node.status || 'Pending');
                    const rowsProcessed = node.rows !== undefined ? node.rows : (node.rowsProcessed || 0);
                    const duration = node.durationMs !== undefined ? node.durationMs : (node.executionTimeMs || 0);
                    card.innerHTML = '<span style="font-weight:600; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">' + node.name + '</span>' +
                        '<span class="stat-label-inline">Rows:</span><span class="stat-val-inline">' + rowsProcessed.toLocaleString() + '</span>' +
                        '<span class="stat-label-inline">Time:</span><span class="stat-val-inline">' + duration + 'ms</span>' +
                        '<span style="margin-left: 12px; font-size: 9px; opacity: 0.5; width: 60px; text-align: right;">' + (node.status || 'Pending').toUpperCase() + '</span>';
                    view.appendChild(card);
                    if (node.children) node.children.forEach(walk);
                };
                roots.forEach(walk);
            } finally {
                _isPipelineUpdating = false;
            }
        }

        function drawPerformance(perf) {
            if (!perf) return;
            const summary = document.getElementById('perf-summary');
            if (!summary) return;
            const execMs = perf.executionMs || perf.durationMs || 0;
            const rows = perf.rowsProcessed || 0;
            const mem = perf.memoryMb || 0;
            summary.innerHTML = '<div class="stat-box"><div class="stat-val">' + execMs + 'ms</div><div class="stat-label">Exec</div></div>' +
                               '<div class="stat-box"><div class="stat-val">' + rows.toLocaleString() + '</div><div class="stat-label">Total Rows</div></div>' +
                               '<div class="stat-box"><div class="stat-val">' + mem + 'MB</div><div class="stat-label">Memory Usage</div></div>';
            const ctx = document.getElementById('perfChart').getContext('2d');
            if (perfChart) perfChart.destroy();
            const statements = perf.statements || [];
            const groups = {};
            statements.forEach(s => {
                const type = s.type || s.statementType || 'Statement';
                groups[type] = (groups[type] || 0) + (s.totalMs || s.durationMs || 0);
            });
            perfChart = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: Object.keys(groups),
                    datasets: [{
                        label: 'Duration (ms)',
                        data: Object.values(groups),
                        backgroundColor: '#6366f1cc',
                        borderRadius: 4
                    }]
                },
                options: { 
                    responsive: true, maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: { 
                        x: { grid: { display: false }, ticks: { color: '#888', font: { size: 10 } } },
                        y: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#888', font: { size: 10 } } }
                    }
                }
            });
            const tctx = document.getElementById('trendChart').getContext('2d');
            if (trendChart) trendChart.destroy();
            trendChart = new Chart(tctx, {
                type: 'line',
                data: {
                    labels: statements.map((_, i) => i + 1),
                    datasets: [{
                        data: statements.map(s => s.totalMs || s.durationMs || 0),
                        borderColor: '#3b82f6',
                        borderWidth: 1.5,
                        pointRadius: 0,
                        fill: true,
                        backgroundColor: 'rgba(59, 130, 246, 0.1)',
                        tension: 0.4
                    }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { display: false },
                        y: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#888', font: { size: 9 } } }
                    }
                }
            });
        }
    </script>
</body>
</html>`;
        // @TEMPLATE-END

        return html
            .replace(/\$\{nonce\}/g, nonce)
            .replace(/\$\{webview.cspSource\}/g, webview.cspSource)
            .replace(/\$\{stylePath\}/g, stylePath.toString())
            .replace(/\$\{scriptPath\}/g, scriptPath.toString())
            .replace(/\$\{chartPath\}/g, chartPath.toString())
            .replace(/\$\{xlsxPath\}/g, xlsxPath.toString());
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
