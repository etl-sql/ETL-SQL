# ETL-SQL VS Code Extension Architecture

This document describes the internal design of `etl-sql-vscode` — the TypeScript VS Code extension that provides syntax highlighting, inline diagnostics, script execution, a results panel, report preview, and the connections sidebar for `.etlsql` and `.rptsql` files.

For the language server the extension hosts, see [LanguageServer.md](LanguageServer.md).

---

## 1. Overview

The extension runs two independent communication channels to the ETL-SQL backend in parallel:

```
VS Code Editor
      │
      ├─── Language Server Protocol (stdio) ────► ETL-SQL.LanguageServer.exe
      │    Completions, diagnostics, hover,         (see LanguageServer.md)
      │    definitions, formatting, schema sidebar
      │
      └─── REPL Process (JSON over stdio) ─────► ETL-SQL.App.exe ui repl
           Script execution, results display,         --session <id> --json
           variable tracking
```

| Channel | Used for | Server executable |
|---------|----------|-------------------|
| LSP | Completions, diagnostics, hover, go-to-definition, formatting, schema queries | `ETL-SQL.LanguageServer.exe` |
| REPL | Script execution, live results, variable snapshots | `ETL-SQL.App.exe ui repl` |

---

## 2. Activation & Entry Point

**File:** `src/extension.ts`  
**Export:** `activate(context: vscode.ExtensionContext)`

Startup sequence:

1. Create output channel (`"ETL-SQL"`)
2. Initialize `ReplManager` singleton — set output channel, subscribe to variable change events
3. Register `ConnectionsProvider` as tree view provider (`etlsql-connections`)
4. Register `ResultsPanel` as WebView view provider (`etlsql-results-view`)
5. Start the Language Client (LSP) — auto-discovers server executable
6. On LSP ready: send global connections to server via `etlsql/setConnections`; subscribe to `etlsql/scriptConnections` notifications
7. Register all commands (see §4)
8. Register event listeners (config changes, document open/close)

**Deactivation:** `deactivate()` stops the Language Client.

---

## 3. Language Client (LSP Handshake)

**File:** `src/extension.ts` (language client setup)

```typescript
const serverOptions: ServerOptions = {
    run:   { command: serverPath, transport: TransportKind.stdio },
    debug: { command: serverPath, transport: TransportKind.stdio }
};

const clientOptions: LanguageClientOptions = {
    documentSelector: [
        { scheme: 'file',     language: 'etlsql' },
        { scheme: 'untitled', language: 'etlsql' }
    ],
    synchronize: {
        fileEvents: vscode.workspace.createFileSystemWatcher('**/*.etlsql')
    }
};
```

The client uses **full text synchronization** — every document change sends the complete document text to the server. The server re-parses, re-lints, and re-publishes diagnostics on every change.

**Server path resolution:** The extension reads `etlsql.server.path` from settings. If empty, it walks up the directory tree from the workspace root looking for `ETL-SQL.LanguageServer.exe` or `ETL-SQL.LanguageServer.dll` in common build output folders.

**Post-startup:** Once the client is ready:
1. `syncConnectionsToLsp()` — loads global connections from `context.globalState` and sends `etlsql/setConnections` so the server can populate schema caches
2. Subscribe to `etlsql/scriptConnections` notifications — when received, call `connectionsProvider.updateScriptConnections()` to refresh the sidebar

---

## 4. Commands

| Command ID | Palette label | Handler | Behavior |
|------------|---------------|---------|----------|
| `etlsql.runScript` | ETL-SQL: Run Script | `runEtlSql(ctx, false)` | Executes entire active document via REPL |
| `etlsql.runSelection` | ETL-SQL: Run Selection | `runEtlSql(ctx, true)` | Executes selected text only |
| `etlsql.showLineage` | ETL-SQL: Show Lineage | `editor.action.showHover()` | Delegates to the hover provider |
| `etlsql.removeConnection` | ETL-SQL: Remove Connection | Provider method | Removes from global state, syncs to LSP |
| `etlsql.refreshConnections` | ETL-SQL: Refresh Connections | LSP notification | Sends `etlsql/refreshMetadata`; server clears cache and re-analyzes |
| `etlsql.copyConnection` | ETL-SQL: Copy Connection | Clipboard write | Copies `CREATE CONNECTION` statement for the selected connection |
| `etlsql.browseFile` | ETL-SQL: Browse for File | File picker | Opens OS file dialog, inserts relative path at cursor |
| `etlsql.previewReport` | ETL-SQL: Preview Report | `ReportPreviewPanel.open()` | Opens report preview panel for `.rptsql` file |

---

## 5. Script Execution Flow

**File:** `src/extension.ts` → `runEtlSql()`

```
1. Get active editor; check file is open
2. Warn if diagnostics contain errors (user can proceed anyway)
3. Get script text (full document or selection)
4. ResultsPanel.postMessage({ type: 'clear' })
5. ReplManager.execute(script, exePath, ['--json','--session',id,'--perf','--verbose'])
6. After completion:
   a. connectionsProvider.refresh()
   b. client.sendNotification('etlsql/refreshMetadata', { uri })
```

---

## 6. REPL Manager

**File:** `src/ReplManager.ts`  
**Pattern:** Singleton (`ReplManager.getInstance()`)

### Process Lifecycle

The REPL manager maintains one long-lived child process per session:

```
First execute() call
  ↓
_start(exePath, ['ui', 'repl', '--session', id, '--json'])
  ↓
Process spawns with FORCE_COLOR=0
  ↓
stdout: { type: 'status', status: 'ready' }
  ↓
_processNext() — dequeues first command, sends to stdin
```

The process stays alive between executions. Subsequent `execute()` calls enqueue their commands and the manager processes them serially.

### JSON Protocol (over stdin/stdout)

**Commands sent to process (stdin):**

```json
{ "action": "run",  "script": "SELECT ..." }
{ "action": "exit" }
```

**Messages received from process (stdout):**

| Message type | Fields | Routing |
|---|---|---|
| `status` | `status`, `buildId` | Internal (ready signal) |
| `message` | `text`, `level` | → ResultsPanel |
| `results` | `data: Row[]`, `timestamp` | → ResultsPanel |
| `performance` | timing breakdown | → ResultsPanel |
| `variables` | `data: Variable[]` | → `onVariablesChange` event → ConnectionsProvider |
| `done` | `exitCode` | Resolves current command's Promise |

### Error Handling

- 60-second timeout per command; rejects the Promise on expiry
- Process crash → remaining queued commands rejected; process restarted on next `execute()` call
- stderr → forwarded to VS Code output channel

---

## 7. Results Panel

**File:** `src/resultsPanel.ts`  
**View type:** `etlsql-results-view`  
**Implementation:** `vscode.WebviewViewProvider`

The Results Panel hosts a React application loaded from `ui/dist/index.html` (built by Vite). It lives in the bottom panel beside the terminal.

### Message Protocol (Extension ↔ Webview)

**Extension → Webview:**

| Message type | Fields | Display |
|---|---|---|
| `clear` | — | Reset the panel |
| `message` | `text`, `level` | Log line (color-coded by level) |
| `results` | `data: Row[]` | Render data grid |
| `performance` | timing data | Render performance breakdown |
| `done` | `exitCode` | Show completion status |

**Webview → Extension:**

| Message type | Effect |
|---|---|
| `cancel` | Calls `ReplManager.stop()` to kill the current execution |

### Message Queueing

If the webview is not yet visible when a message is posted, the message is queued. When the webview becomes visible (`resolveWebviewView()`), queued messages are drained in order and the panel is automatically shown.

---

## 8. Report Preview Panel

**File:** `src/reportPreviewPanel.ts`  
**Pattern:** One panel per file (reuses existing panel for the same file)

### Preview Flow

```
User runs etlsql.previewReport (or saves .rptsql file with auto-preview on)
    ↓
ReportPreviewPanel.open(extensionUri, scriptPath)
    ↓
_buildManifest():
  spawn: etl-sql-report build --format json <scriptPath>
  read stdout → JSON manifest
    ↓
_getReportHtml(manifest):
  inject: window.__MANIFEST__ = <manifest JSON>
  load: report-runtime.js (from extension resources)
  load: ECharts from CDN
    ↓
webviewPanel.webview.html = html
```

### Auto-refresh

The panel registers a `vscode.workspace.onDidSaveTextDocument` listener. When the `.rptsql` file is saved, `_refreshContent()` re-runs `_buildManifest()` and updates the webview HTML, giving a live preview experience.

### Relationship to ReportPlayer

The preview panel uses the **VS Code mode** of `report-runtime.js` — it pre-embeds the manifest as `window.__MANIFEST__` rather than fetching from a server. This means:
- No HTTP server is needed for preview
- Interactive controls (SLICER, DATEPICKER, etc.) are not active in preview mode — they require the web server's `/api/parameter` endpoint
- To test interactivity, use `etl-sql-report serve` to launch the full ReportPlayer

---

## 9. Connections Provider

**File:** `src/connectionsProvider.ts`  
**View ID:** `etlsql-connections`  
**Implementation:** `vscode.TreeDataProvider<TreeItem>`

### Tree Structure

```
etlsql-connections (view root)
├─ GlobalConnection (type badge: MSSQL)
│   ├─ Tables
│   │   ├─ customers  [LSP: etlsql/getTables]
│   │   │   ├─ id     [LSP: etlsql/getColumns]
│   │   │   └─ name
│   │   └─ orders
│   └─ Views         [LSP: etlsql/getViews]
├─ ScriptConnection (type badge: FLATFILE) ← from etlsql/scriptConnections
├─ Temporary Tables  ← discovered by TextDocumentHandler
│   └─ #stage_data  [LSP: etlsql/getTempTables]
└─ Script Variables  ← from ReplManager onVariablesChange
    ├─ @region = 'East'
    └─ @year = '2026'
```

### Update Triggers

| Event | Method called | Source |
|-------|---------------|--------|
| LSP `etlsql/scriptConnections` notification | `updateScriptConnections(uri, conns)` | TextDocumentHandler after parse |
| REPL `variables` message | `updateVariables(vars)` | ReplManager event |
| User adds/removes connection | `saveConnections()` → `refresh()` | Command handlers |
| `etlsql.refreshConnections` command | `refresh()` | User action |

### Persistence

Global connections are stored in `context.globalState` under key `etlsql.connections`. The tree view loads them on activation via `loadConnections()` and saves any changes via `saveConnections()`.

---

## 10. Language & File Associations

Defined in `package.json`:

```json
"languages": [{
    "id": "etlsql",
    "aliases": ["ETL-SQL", "etlsql"],
    "extensions": [".etlsql", ".rptsql"],
    "configuration": "./language-configuration.json"
}],
"grammars": [{
    "language": "etlsql",
    "scopeName": "source.etlsql",
    "path": "./syntaxes/etlsql.tmLanguage.json"
}]
```

Both `.etlsql` and `.rptsql` use the same language ID (`etlsql`) and grammar, so the LSP activates for both file types. The extension distinguishes `.rptsql` files only for the report preview command.

---

## 11. Configuration Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `etlsql.executablePath` | *(empty)* | Path to `ETL-SQL.App.exe`. Auto-discovered from workspace build output if empty. |
| `etlsql.server.path` | *(empty)* | Path to `ETL-SQL.LanguageServer.exe`. Auto-discovered if empty. |
| `etlsql.reportCliPath` | *(empty)* | Path to `etl-sql-report.exe`. Used by the preview panel build step. |
| `etlsql.autoPreviewReport` | `false` | Automatically open preview panel when a `.rptsql` file is opened. |

---

## 12. Extension Points for Contributors

**Adding a new command:**
1. Register the command handler in `activate()` with `context.subscriptions.push(vscode.commands.registerCommand(...))`
2. Add the command to `package.json` under `"commands"` and optionally `"menus"`

**Adding a new LSP feature:**
1. Implement the handler in `ETL-SQL.LanguageServer` (see [LanguageServer.md](LanguageServer.md))
2. The Language Client picks up standard LSP capabilities automatically from server capability negotiation — no client-side changes needed for standard capabilities
3. For custom requests/notifications: add the send/receive call in `extension.ts` after `client.start()` resolves

**Adding a new tree view node type:**
1. Create a subclass of `TreeItem` in `connectionsProvider.ts`
2. Add a case in `getChildren()` to return instances of the new type
3. Add a case in `getTreeItem()` for icon, label, and context value
