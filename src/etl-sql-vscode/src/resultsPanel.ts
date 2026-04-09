import * as vscode from 'vscode';

export class ResultsPanel {
    public static currentPanel: ResultsPanel | undefined;
    private readonly _panel: vscode.WebviewPanel;
    private readonly _extensionUri: vscode.Uri;
    private _isReady: boolean = false;
    private _messageQueue: any[] = [];
    private _disposables: vscode.Disposable[] = [];
    private _onMessageReceived?: (message: any) => void;

    private constructor(panel: vscode.WebviewPanel, extensionUri: vscode.Uri, onMessageReceived?: (message: any) => void) {
        this._panel = panel;
        this._extensionUri = extensionUri;
        this._onMessageReceived = onMessageReceived;
        this._panel.onDidDispose(() => this.dispose(), null, this._disposables);
        
        // Handle messages from the webview
        this._panel.webview.onDidReceiveMessage(message => {
            if (message.type === 'ready') {
                this._isReady = true;
                this._flushQueue();
            }
            if (this._onMessageReceived) {
                this._onMessageReceived(message);
            }
        }, null, this._disposables);

        this._panel.webview.html = this._getHtmlForWebview();
    }

    public static createOrShow(extensionUri: vscode.Uri, onMessageReceived?: (message: any) => void) {
        const column = vscode.window.activeTextEditor
            ? vscode.window.activeTextEditor.viewColumn
            : undefined;

        if (ResultsPanel.currentPanel) {
            ResultsPanel.currentPanel._panel.reveal(vscode.ViewColumn.Beside);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'etlsqlResults',
            'ETL-SQL Results',
            vscode.ViewColumn.Beside,
            {
                enableScripts: true,
                retainContextWhenHidden: true
            }
        );

        ResultsPanel.currentPanel = new ResultsPanel(panel, extensionUri, onMessageReceived);
    }

    public static postMessage(message: any) {
        if (ResultsPanel.currentPanel) {
            if (ResultsPanel.currentPanel._isReady) {
                ResultsPanel.currentPanel._panel.webview.postMessage(message);
            } else {
                ResultsPanel.currentPanel._messageQueue.push(message);
            }
        }
    }

    private _flushQueue() {
        while (this._messageQueue.length > 0) {
            const msg = this._messageQueue.shift();
            this._panel.webview.postMessage(msg);
        }
    }

    public dispose() {
        ResultsPanel.currentPanel = undefined;
        this._panel.dispose();
        while (this._disposables.length) {
            const x = this._disposables.pop();
            if (x) {
                x.dispose();
            }
        }
    }

    private _getHtmlForWebview() {
        const webview = this._panel.webview;

        const scriptPath = vscode.Uri.joinPath(this._extensionUri, 'media', 'tabulator.min.js');
        const scriptUri = webview.asWebviewUri(scriptPath);

        const stylePath = vscode.Uri.joinPath(this._extensionUri, 'media', 'tabulator.min.css');
        const styleUri = webview.asWebviewUri(stylePath);

        const chartPath = vscode.Uri.joinPath(this._extensionUri, 'media', 'chart.min.js');
        const chartUri = webview.asWebviewUri(chartPath);

        const xlsxPath = vscode.Uri.joinPath(this._extensionUri, 'media', 'xlsx.full.min.js');
        const xlsxUri = webview.asWebviewUri(xlsxPath);

        const nonce = getNonce();

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; script-src 'nonce-${nonce}' 'unsafe-inline' ${webview.cspSource}; connect-src ${webview.cspSource}; img-src ${webview.cspSource} data: https:;">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet">
    <link href="${styleUri}" rel="stylesheet">
    <title>ETL-SQL Results</title>
    <style>
        :root {
            --glass-bg: var(--vscode-editor-background);
            --glass-border: var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            --accent-primary: #8b5cf6;
            --accent-secondary: #06b6d4;
            --accent-tertiary: #f97316;
            --text-main: var(--vscode-editor-foreground);
            --text-dim: var(--vscode-descriptionForeground);
            --toolbar-bg: var(--vscode-editorGroupHeader-tabsBackground, var(--vscode-sideBar-background));
        }
        body.vscode-light {
            --accent-primary: #7c3aed;
            --accent-secondary: #0891b2;
            --glass-border: var(--vscode-widget-border, rgba(0, 0, 0, 0.1));
        }
        body.vscode-dark {
            --accent-primary: #a78bfa;
            --accent-secondary: #22d3ee;
        }

        body {
            font-family: 'Outfit', var(--vscode-font-family), sans-serif;
            padding: 0;
            margin: 0;
            color: var(--text-main);
            background: var(--vscode-editor-background);
            background-attachment: fixed;
            min-height: 100vh;
            overflow-x: hidden;
        }
        body::before {
            content: '';
            position: fixed;
            top: 0; left: 0; width: 100%; height: 100%;
            background: radial-gradient(circle at top right, rgba(139, 92, 246, 0.06), transparent 40%),
                        radial-gradient(circle at bottom left, rgba(6, 182, 212, 0.06), transparent 40%);
            pointer-events: none;
            z-index: -1;
        }
        body.vscode-light::before { opacity: 0.5; }
        .tabs {
            display: flex;
            background: var(--toolbar-bg);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border-bottom: 1px solid var(--glass-border);
            padding: 10px 20px 0 20px;
            position: sticky;
            top: 0;
            z-index: 100;
            gap: 20px;
        }
        .tab {
            padding: 12px 4px;
            cursor: pointer;
            font-size: 13px;
            font-weight: 500;
            color: var(--vscode-tab-inactiveForeground, var(--text-dim));
            position: relative;
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }
        .tab:hover { color: var(--vscode-tab-activeForeground, var(--text-main)); }
        .tab.active { color: var(--vscode-tab-activeForeground, var(--accent-primary)); font-weight: 600; }
        .tab.active::after {
            content: '';
            position: absolute;
            bottom: 0;
            left: 0;
            right: 0;
            height: 2px;
            background: var(--vscode-tab-activeBorder, var(--accent-primary));
            border-radius: 2px 2px 0 0;
        }
        
        .toolbar {
            display: flex;
            gap: 12px;
            padding: 8px 24px;
            background: var(--toolbar-bg);
            border-bottom: 1px solid var(--glass-border);
            align-items: center;
            opacity: 0.9;
        }
        .btn-action {
            background: var(--vscode-button-secondaryBackground, rgba(139, 92, 246, 0.1));
            border: 1px solid var(--vscode-button-secondaryHoverBackground, rgba(139, 92, 246, 0.3));
            color: var(--vscode-button-secondaryForeground, var(--text-main));
            padding: 4px 10px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 11px;
            font-weight: 500;
            transition: all 0.2s ease;
            display: flex;
            align-items: center;
            gap: 6px;
        }
        .btn-action:hover {
            background: var(--vscode-button-hoverBackground, var(--accent-primary));
            border-color: var(--vscode-button-hoverBackground, var(--accent-primary));
            color: var(--vscode-button-foreground, white);
            transform: translateY(-1px);
        }
        .btn-cancel {
            background: rgba(239, 68, 68, 0.1);
            border: 1px solid rgba(239, 68, 68, 0.3);
            color: #ef4444;
        }
        .btn-cancel:hover {
            background: #ef4444; 
            border-color: #ef4444;
            color: white;
        }

        .running-indicator {
            display: none;
            padding: 10px 24px;
            background: var(--vscode-editor-lineHighlightBackground);
            border-bottom: 1px solid var(--vscode-editor-lineHighlightBorder, var(--glass-border));
            align-items: center;
            gap: 15px;
            font-size: 12px;
            color: var(--vscode-editor-foreground);
        }
        .running-indicator.active { display: flex; animation: slideDown 0.3s ease-out; }
        @keyframes slideDown { from { transform: translateY(-100%); } to { transform: translateY(0); } }

        .spinner {
            width: 14px;
            height: 14px;
            border: 2px solid var(--vscode-descriptionForeground);
            border-top-color: var(--accent-secondary);
            border-radius: 50%;
            animation: spin 0.8s linear infinite;
        }
        @keyframes spin { to { transform: rotate(360deg); } }

        .content { padding: 20px; }
        .hidden { display: none; }
        
        .results-container {
            margin-bottom: 30px;
            background: var(--glass-bg);
            border: 1px solid var(--glass-border);
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
        }
        .result-set-header {
            padding: 10px 16px;
            background: var(--toolbar-bg);
            border-bottom: 1px solid var(--glass-border);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .result-set-label {
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: var(--accent-primary);
        }

        /* Tabulator Theme Integration */
        .tabulator {
            background: transparent !important;
            border: none !important;
            font-size: 12px !important;
            font-family: 'JetBrains Mono', monospace !important;
            color: var(--text-main) !important;
        }
        .tabulator-header {
            background: var(--toolbar-bg) !important;
            border-bottom: 1px solid var(--glass-border) !important;
            color: var(--vscode-tab-activeForeground, var(--text-dim)) !important;
            font-family: 'Outfit', sans-serif !important;
            font-weight: 600 !important;
        }
        .tabulator-col { background: transparent !important; border-right: 1px solid var(--glass-border) !important; }
        .tabulator-row { background: var(--vscode-editor-background, transparent) !important; color: var(--vscode-editor-foreground, inherit) !important; }
        .tabulator-row.tabulator-row-even { background: var(--vscode-editor-lineHighlightBackground, rgba(127, 127, 127, 0.07)) !important; }
        .tabulator-row:hover { background: var(--vscode-list-hoverBackground, rgba(127, 127, 127, 0.12)) !important; }
        .tabulator-cell { 
            border-right: 1px solid var(--glass-border) !important; 
            padding: 6px 8px !important; 
            background: transparent !important;
        }
        .tabulator-header-filter input {
            background: var(--vscode-input-background) !important;
            border: 1px solid var(--vscode-input-border, var(--glass-border)) !important;
            color: var(--vscode-input-foreground) !important;
            border-radius: 3px !important;
        }

        .perf-grid { display: grid; grid-template-columns: 1fr 1.2fr; gap: 20px; align-items: start; }
        .perf-card { background: var(--glass-bg); border: 1px solid var(--glass-border); border-radius: 12px; padding: 20px; }
        .perf-card.wide { grid-column: span 2; }
        .perf-chart-container { height: 240px; position: relative; margin-top: 15px; }

        .message-log {
            font-family: 'JetBrains Mono', monospace;
            padding: 16px;
            background: var(--glass-bg);
            border-radius: 8px;
            border: 1px solid var(--glass-border);
            line-height: 1.5;
            font-size: 12px;
        }
        .message-log.hide-info .message-info:not(.msg-essential) {
            display: none;
        }
        .msg-entry { margin-bottom: 6px; border-left: 3px solid transparent; padding-left: 12px; }
        .message-info { border-color: var(--accent-secondary); color: var(--text-dim); }
        .message-error { border-color: #ef4444; color: #ef4444; background: rgba(239, 68, 68, 0.05); }
        .active-row { background: var(--vscode-editor-lineHighlightBackground) !important; }
        
        .raw-telemetry { margin-top: 30px; padding: 15px; background: rgba(127, 127, 127, 0.05); border-radius: 8px; font-family: 'JetBrains Mono', monospace; font-size: 11px; color: var(--text-dim); border: 1px solid var(--glass-border); }
        .raw-telemetry summary { cursor: pointer; padding: 8px; color: var(--accent-secondary); }
        .perf-row { display: flex; justify-content: space-between; padding: 12px 0; border-bottom: 1px solid var(--glass-border); }
        .perf-label { font-size: 13px; color: var(--text-dim); }
        .perf-val { font-family: 'JetBrains Mono', monospace; font-weight: 600; color: var(--accent-secondary); }
        
        h3 { margin-top: 0; font-size: 16px; font-weight: 600; display: flex; align-items: center; gap: 10px; color: var(--text-main); }
        h3::before { content: ''; width: 3px; height: 16px; background: var(--accent-primary); border-radius: 2px; }
    </style>
</head>
<body>
    <div class="tabs">
        <div id="tab-results" class="tab active" onclick="showTab('results')">Results</div>
        <div id="tab-messages" class="tab" onclick="showTab('messages')">Messages</div>
        <div id="tab-performance" class="tab" onclick="showTab('performance')">Performance</div>
    </div>

    <div id="running-indicator" class="running-indicator">
        <div class="spinner"></div>
        <span id="running-text">Script is running...</span>
        <button class="btn-action btn-cancel" onclick="cancelScript()" style="margin-left: auto;">Cancel Execution</button>
    </div>

    <div class="toolbar" id="grid-toolbar">
        <button class="btn-action" onclick="exportData('csv')">Export CSV</button>
        <button class="btn-action" onclick="exportData('xlsx')">Export Excel</button>
        <button class="btn-action" onclick="exportData('json')">Export JSON</button>
        
        <div style="margin-left: 20px; display: flex; align-items: center; font-size: 11px; gap: 6px;">
            <input type="checkbox" id="chk-hide-info" onchange="toggleHideInfo()">
            <label for="chk-hide-info" style="cursor: pointer;">Hide Info Logs</label>
        </div>

        <span style="margin-left: auto; font-size: 11px; color: var(--text-dim);">Ctrl+C to copy selected cells</span>
    </div>

    <div id="content-results" class="content"></div>

    <div id="content-messages" class="content hidden">
        <div id="messages-log" class="message-log"></div>
    </div>

    <div id="content-performance" class="content hidden">
        <div class="perf-grid">
            <div class="perf-card">
                <h3>Execution Timing</h3>
                <div class="perf-chart-container">
                    <canvas id="timingChart"></canvas>
                </div>
            </div>
            <div class="perf-card">
                <h3>Engine Telemetry</h3>
                <div class="perf-table">
                    <div class="perf-row"><span class="perf-label">Lexer Time</span><span id="lbl-lexer" class="perf-val">0 ms</span></div>
                    <div class="perf-row"><span class="perf-label">Parser Time</span><span id="lbl-parser" class="perf-val">0 ms</span></div>
                    <div class="perf-row"><span class="perf-label">Execution Time</span><span id="lbl-exec" class="perf-val">0 ms</span></div>
                    <div class="perf-row"><span class="perf-label">Memory peak</span><span id="lbl-mem" class="perf-val">0.00 MB</span></div>
                    <div class="perf-row"><span class="perf-label">Rows Processed</span><span id="lbl-rows" class="perf-val">0</span></div>
                    <div class="perf-row"><span class="perf-label">Throughput</span><span id="lbl-rps" class="perf-val" style="color:var(--accent-primary)">0 R/S</span></div>
                </div>
            </div>
            <div class="perf-card wide" id="statement-breakdown-card">
                <h3>Statement Breakdown</h3>
                <div class="perf-chart-container">
                    <canvas id="statementChart"></canvas>
                </div>
            </div>
        </div>
        <details class="raw-telemetry">
            <summary>Raw Telemetry Diagnostics</summary>
            <pre id="raw-telemetry-data">No data received yet.</pre>
        </details>
    </div>

    <script nonce="${nonce}" src="${chartUri}"></script>
    <script nonce="${nonce}" src="${scriptUri}"></script>
    <script nonce="${nonce}" src="${xlsxUri}"></script>
    
    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        const resultsContent = document.getElementById('content-results');
        const messagesLog = document.getElementById('messages-log');
        
        // State variables
        let state = vscode.getState() || {
            results: [],      // Array of result packets
            messages: [],     // Array of message packets
            metrics: null,    // Last metrics packet
            resultSetCount: 0
        };

        let tablesPerResultSet = {}; // Map of index -> Tabulator
        let runningIndicator = document.getElementById('running-indicator');
        let tables = [];

        console.log("ETL-SQL Results Webview Initialized. Active State Results:", state.results.length);

        // Restore state on load
        if (state.results.length > 0) {
            console.log("Restoring previous results...");
            const oldResults = [...state.results];
            state.results = []; // Clear for re-rendering
            state.resultSetCount = 0;
            oldResults.forEach(r => renderResults(r, false));
        }
        if (state.messages.length > 0) {
            state.messages.forEach(m => appendMessage(m, false));
        }
        if (state.metrics) {
            renderPerformance(state.metrics, false);
        }

        window.addEventListener('message', event => {
            const message = event.data;
            console.log("Webview received message:", message.type);
            switch (message.type) {
                case 'results': renderResults(message, true); break;
                case 'performance': renderPerformance(message.metrics, true); break;
                case 'message': appendMessage(message, true); break;
                case 'clear': clearAll(); break;
                case 'done': onExecutionDone(message.exitCode); break;
            }
        });

        function showTab(tab) {
            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
            document.querySelectorAll('.content').forEach(c => c.classList.add('hidden'));
            document.getElementById('tab-' + tab).classList.add('active');
            document.getElementById('content-' + tab).classList.remove('hidden');
        }

        function renderResults(data, saveToState = true) {
            if (saveToState) {
                state.results.push(data);
                vscode.setState(state);
            }

            // AUTO-INIT Table if it doesn't exist for the current stream
            if (data.isFirst || tables.length === 0) {
                if (data.isFirst) state.resultSetCount++;
                else if (tables.length === 0) state.resultSetCount = 1;

                const container = document.createElement('div');
                container.className = 'results-container';
                
                const header = document.createElement('div');
                header.className = 'result-set-header';
                header.innerHTML = `<span class="result-set-label">RESULT SET ${state.resultSetCount}</span>
                                   <span id="row-count-${state.resultSetCount}" style="font-size:11px; margin-left:12px; opacity:0.75; font-family: monospace;">0 rows</span>
                                   <span style="font-size:11px; opacity:0.5; font-family: monospace; margin-left:8px;">${data.columns ? data.columns.length : 0} columns</span>`;
                container.appendChild(header);
                
                const gridDiv = document.createElement('div');
                gridDiv.id = 'grid-' + state.resultSetCount;
                container.appendChild(gridDiv);
                resultsContent.appendChild(container);
                
                const table = new Tabulator('#' + gridDiv.id, {
                    data: data.rows,
                    columns: (data.columns || []).map(c => ({
                        title: c, 
                        field: c, 
                        headerFilter: "input", 
                        headerFilterPlaceholder: "Filter...",
                        hozAlign: (typeof data.rows[0]?.[c] === 'number') ? 'right' : 'left'
                    })),
                    layout: "fitDataFill",
                    height: "400px",
                    movableColumns: true,
                    clipboard: "copy",
                    clipboardCopyConfig: { columnHeaders: true },
                    clipboardCopyRowRange: "active",
                    selectable: true,
                    selectableRange: true,
                    selectableRangeMode: "cell"
                });
                
                table.on("dataLoaded", function(data) {
                    const countEl = document.getElementById('row-count-' + state.resultSetCount);
                    if (countEl) countEl.textContent = data.length.toLocaleString() + ' rows';
                });
                
                table.on("rowAdded", function(row) {
                     const countEl = document.getElementById('row-count-' + state.resultSetCount);
                     if (countEl) countEl.textContent = table.getDataCount().toLocaleString() + ' rows';
                });
                
                tables.push(table);
            } else {
                if (tables.length > 0) {
                    const table = tables[tables.length - 1];
                    // Safeguard: Limit in-browser rows for huge result sets
                    if (table.getDataCount() < 100000) {
                        table.addData(data.rows);
                    } else if (!document.getElementById('row-limit-warn')) {
                        const warn = document.createElement('div');
                        warn.id = 'row-limit-warn';
                        warn.style = "padding:10px; color:#f97316; font-size:11px; text-align:center;";
                        warn.textContent = "Row limit (100k) reached for display. Use EXPORT for full dataset.";
                        resultsContent.appendChild(warn);
                    }
                }
            }
        }

        function appendMessage(msg, saveToState = true) {
            if (saveToState) {
                state.messages.push(msg);
                vscode.setState(state);
            }

            const div = document.createElement('div');
            div.className = 'msg-entry message-' + (msg.level || 'info');
            div.innerHTML = \`<span style="opacity:0.4; margin-right:8px">\${new Date().toLocaleTimeString()}</span> \${msg.text}\`;
            messagesLog.appendChild(div);
            if (msg.level === 'error') {
                showTab('messages');
                runningIndicator.classList.remove('active');
            }
        }

        function toggleHideInfo() {
            const hide = document.getElementById('chk-hide-info').checked;
            if (hide) {
                messagesLog.classList.add('hide-info');
            } else {
                messagesLog.classList.remove('hide-info');
            }
        }

        function clearAll() {
            resultsContent.innerHTML = '';
            messagesLog.innerHTML = '';
            state = {
                results: [],
                messages: [],
                metrics: null,
                resultSetCount: 0
            };
            vscode.setState(state);
            tables = [];
            runningIndicator.classList.add('active');
        }

        function cancelScript() {
            vscode.postMessage({ type: 'cancel' });
            appendMessage({ level: 'info', text: 'Cancellation requested...' });
        }

        function onExecutionDone(exitCode) {
            runningIndicator.classList.remove('active');
            if (exitCode !== 0 && exitCode !== null) {
                showTab('messages');
            }
        }

        function exportData(format) {
            if (tables.length === 0) return;
            const activeTable = tables[tables.length - 1]; // Export only last result set for now
            const filename = "ETLSQL_Results_" + new Date().toISOString().split('T')[0];
            
            if (format === 'csv') activeTable.download("csv", filename + ".csv");
            if (format === 'json') activeTable.download("json", filename + ".json");
            if (format === 'xlsx') activeTable.download("xlsx", filename + ".xlsx", {sheetName:"Results"});
        }

        function renderPerformance(metrics, saveToState = true) {
            if (saveToState) {
                state.metrics = metrics;
                vscode.setState(state);
            }

            runningIndicator.classList.remove('active');
            document.getElementById('raw-telemetry-data').textContent = JSON.stringify(metrics, null, 2);
            try {
                document.getElementById('lbl-lexer').textContent = metrics.lexerMs + ' ms';
                document.getElementById('lbl-parser').textContent = metrics.parserMs + ' ms';
                document.getElementById('lbl-exec').textContent = metrics.executionMs + ' ms';
                document.getElementById('lbl-mem').textContent = (metrics.memoryMb || 0.00).toFixed(2) + ' MB';
                document.getElementById('lbl-rows').textContent = (metrics.rowsProcessed || 0).toLocaleString();
                document.getElementById('lbl-rps').textContent = (metrics.rowsPerSecond || 0).toLocaleString() + ' R/S';

                const breakdownCard = document.getElementById('statement-breakdown-card');
                if (!metrics.statements || metrics.statements.length === 0) {
                    breakdownCard.classList.add('hidden');
                } else {
                    breakdownCard.classList.remove('hidden');
                    renderStatementChart(metrics.statements);
                }

                renderTimingChart(metrics);
            } catch (err) { console.error(err); }
        }

        let timingChartInstance, statementChartInstance;
        function renderTimingChart(m) {
            const ctx = document.getElementById('timingChart').getContext('2d');
            if (timingChartInstance) timingChartInstance.destroy();
            timingChartInstance = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Lexer', 'Parser', 'Execution'],
                    datasets: [{
                        data: [m.lexerMs, m.parserMs, m.executionMs],
                        backgroundColor: ['#8b5cf6', '#06b6d4', '#f97316'],
                        borderColor: 'rgba(0,0,0,0.2)', borderWidth: 2
                    }]
                },
                options: { cutout: '75%', responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom', labels: { color: '#e2e8f0' } } } }
            });
        }

        function renderStatementChart(sList) {
            const ctx = document.getElementById('statementChart').getContext('2d');
            if (statementChartInstance) statementChartInstance.destroy();
            statementChartInstance = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: sList.map(s => (s.sql || 'Unknown').length > 30 ? s.sql.substring(0, 27) + '...' : s.sql),
                    datasets: [{ label: 'Duration (ms)', data: sList.map(s => s.durationMs || 0), backgroundColor: '#8b5cf6', borderRadius: 6 }]
                },
                options: { indexAxis: 'y', responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } },
                           scales: { x: { grid: { color: 'rgba(255,255,255,0.1)' }, ticks: { color: '#94a3b8' } }, y: { grid: { display: false }, ticks: { color: '#94a3b8' } } } }
            });
        }

        window.addEventListener('load', () => vscode.postMessage({ type: 'ready' }));
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
