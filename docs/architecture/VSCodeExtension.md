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

### Snippet completions

The LSP `CompletionProvider` (`src/ETL-SQL.LanguageServer/CompletionProvider.cs`) surfaces `$trigger` snippet templates as VS Code-native completion items. When the cursor prefix matches a `$` word at statement start, `SnippetLibrary.Instance.GetByPrefix()` is queried and each matching `SnippetDef` is returned with:

| Field | Value |
|-------|-------|
| `Kind` | `CompletionItemKind.Snippet` |
| `InsertText` | `SnippetDef.LspBody` — the template body with `«placeholder»` markers converted to `${N:placeholder}` VS Code tab-stop syntax |
| `InsertTextFormat` | `InsertTextFormat.Snippet` — enables native VS Code tab-stop navigation |
| `SortText` | `"0001_" + trigger` — snippets sort above keyword completions |

The `LspBody` is pre-computed by `SnippetLibrary.ConvertToLspTabStops()` when the library loads. Each `«text»` becomes `${1:text}`, `${2:text}`, etc. in order of appearance.

User snippets from `Snippets:UserSnippetsPath` are loaded into the same `SnippetLibrary.Instance` and delivered identically.

---

## 4. Commands

| Command ID | Palette label | Handler | Behavior |
|------------|---------------|---------|----------|
| `etlsql.runScript` | ETL-SQL: Run Script | `runEtlSql(ctx, false)` | Executes entire active document via REPL |
| `etlsql.runSelection` | ETL-SQL: Run Selection | `runEtlSql(ctx, true)` | Executes selected text only |
| `etlsql.stopScript` | ETL-SQL: Stop Script | `ReplManager.cancel()` | Sends a cooperative cancel request to the REPL process |
| `etlsql.rollbackTransactions` | ETL-SQL: Rollback Transactions | `ReplManager.rollback()` | Sends `ROLLBACK;` to the active REPL session |
| `etlsql.showLineage` | ETL-SQL: Show Lineage | `editor.action.showHover()` | Delegates to the hover provider |
| `etlsql.removeConnection` | ETL-SQL: Remove Connection | Provider method | Removes from global state, syncs to LSP |
| `etlsql.refreshConnections` | ETL-SQL: Refresh Connections | LSP notification | Sends `etlsql/refreshMetadata`; server clears cache and re-analyzes |
| `etlsql.copyConnection` | ETL-SQL: Copy Connection | Clipboard write | Copies `CREATE CONNECTION` statement for the selected connection |
| `etlsql.browseFile` | ETL-SQL: Browse for File | File picker | Opens OS file dialog, inserts relative path at cursor |
| `etlsql.previewReport` | ETL-SQL: Preview Report | `ReportPreviewPanel.open()` | Opens report preview panel for `.rptsql` file |
| `etlsql.launchInBrowser` | ETL-SQL: Launch Report in Browser | Preview panel helper | Starts ReportPlayer for the active report preview |
| `etlsql.launchReportFile` | ETL-SQL: Launch Report File | `launchReport*` helper | Starts ReportPlayer for a selected `.rptsql` file |
| `etlsql.launchReportDirectory` | ETL-SQL: Launch Report Directory | `launchReport*` helper | Starts ReportPlayer for a directory/manifest workflow |
| `etlsql.launchReportManifest` | ETL-SQL: Launch Report Manifest | `launchReport*` helper | Starts ReportPlayer with a multi-report manifest |
| `etlsql.publishToPortal` | ETL-SQL: Publish to Portal | Preview panel helper | Publishes the current report to the configured portal |
| `etlsql.exportMarkdown` | ETL-SQL: Export Markdown | Preview panel helper | Runs report CLI export as Markdown |
| `etlsql.exportPdf` | ETL-SQL: Export PDF | Preview panel helper | Runs report CLI export as PDF |
| `etlsql.exportText` | ETL-SQL: Export Text | Preview panel helper | Runs report CLI text export |
| `etlsql.exportNotebook` | ETL-SQL: Export Notebook | notebook helper | Exports the active script/results as a notebook artifact |
| `etlsql.showWelcome` | ETL-SQL: Show Welcome | welcome panel | Opens the extension welcome view |

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
{ "Action": "run",    "Script": "SELECT ...", "ScriptPath": "...", "WorkspaceRoot": "...", "InteractiveMode": false }
{ "Action": "cancel" }
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

If the webview is hidden when a message is posted, the message is queued. When the webview becomes visible (`resolveWebviewView()`), queued messages are drained in order and the panel is automatically shown.

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
  load: echarts.min.js (from extension media resources)
    ↓
webviewPanel.webview.html = html
```

### Auto-refresh

The panel registers a `vscode.workspace.onDidSaveTextDocument` listener. When the `.rptsql` file is saved, `_refreshContent()` re-runs `_buildManifest()` and updates the webview HTML, giving a live preview experience.

### Relationship to ReportPlayer

The preview panel uses the **VS Code mode** of `report-runtime.js` — it pre-embeds the manifest as `window.__MANIFEST__` rather than fetching from a server. This means:
- No HTTP server is needed for preview
- Interactive controls can render in preview mode, but actions that require a live report session, such as drill-in, server-side parameter refresh, and script actions, require the ReportPlayer/Portal API
- To test interactivity, use `etl-sql-report serve` to launch the full ReportPlayer

---

## 9. Connections Provider

**File:** [connectionsProvider.ts](../../src/etl-sql-vscode/src/connectionsProvider.ts)  
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
| `etlsql.refreshConnections` command | `refresh()` | User action |

### In-Memory Scope

Connections are dynamically discovered within script documents and provided in-memory scoped to the active editor. The extension does not store connection strings or credentials persistently to enforce a Zero-Trust stance.

---

## 10. Report Designer Panel

**File:** [reportDesignerPanel.ts](../../src/etl-sql-vscode/src/reportDesignerPanel.ts)  
**Class:** [ReportDesignerPanel](../../src/etl-sql-vscode/src/reportDesignerPanel.ts#L15)  
**View Type:** `etlsql.reportDesigner`

The Report Designer Panel provides an offline graphical editor canvas for `.rptsql` files. It hosts the client-side report designer UI module via a `WebviewPanel`.

- **LSP request bridging:** Instead of spawning a local HTTP web server, the panel intercepts designer API requests and routes them directly to the active Language Client via custom JSON-RPC request endpoints:
  - `etlsql/designerParse`: parses the ETL-SQL script text into a visual layout State JSON.
  - `etlsql/designerGenerate`: converts a visual layout State JSON back into ETL-SQL syntax.
- **Disk persistence:** When the user triggers a save event within the webview, a message is posted to the extension host, which directly overwrites the target file on disk via the Node.js filesystem API (`fs.writeFileSync`).

---

## 11. Sidebar Provider

**File:** [sidebarProvider.ts](../../src/etl-sql-vscode/src/sidebarProvider.ts)  
**Class:** [SidebarProvider](../../src/etl-sql-vscode/src/sidebarProvider.ts#L6)  
**View ID:** `etlsql-sidebar`

The `SidebarProvider` implements `vscode.WebviewViewProvider` to render an interactive webview panel inside the side explorer view.

- **Vite Integration:** It loads the shared React UI bundle (`ui/dist/index.html`) with `window.VIEW_TYPE = 'sidebar'` injected to conditionally mount the sidebar component tree.
- **Metadata Queries:** To fetch tables, columns, and temporary tables dynamically as the tree nodes expand, it executes custom JSON-RPC request calls to the Language Client (`etlsql/getTables`, `etlsql/getColumns`, `etlsql/getTempTables`).
- **Interactive Insertion:** Users can click connections, tables, or columns in the sidebar to insert their names directly into the active text editor cursor position (`_insertTextAtActiveEditor`).

---

## 12. Welcome View

**File:** [WelcomeView.ts](../../src/etl-sql-vscode/src/WelcomeView.ts)  
**Class:** [WelcomeView](../../src/etl-sql-vscode/src/WelcomeView.ts#L5)

The `WelcomeView` controller manages a static HTML panel (`welcome.html`) offering quick start links.

- **Actions handled:**
  - `newScript` / `newReport`: opens a blank `etlsql` document in the editor.
  - `newNotebook`: opens a new `.etlnb` document utilizing the ETL-SQL notebook kernel.
  - Quick-links: maps UI button events to open documentation, cookbooks, samples, or licenses.

---

## 13. Notebook Support

**Files:** [notebookController.ts](../../src/etl-sql-vscode/src/notebookController.ts), [notebookSerializer.ts](../../src/etl-sql-vscode/src/notebookSerializer.ts)  
**Classes:** [ETLNotebookController](../../src/etl-sql-vscode/src/notebookController.ts#L7), [ETLNotebookSerializer](../../src/etl-sql-vscode/src/notebookSerializer.ts#L13)

The extension provides a native notebook interface (`.etlnb`) for writing and running multi-cell ETL-SQL scripts.

- **Notebook Serializer:** [ETLNotebookSerializer](../../src/etl-sql-vscode/src/notebookSerializer.ts#L13) parses and serializes notebook cells to and from a JSON structure.
- **Execution Kernel:** [ETLNotebookController](../../src/etl-sql-vscode/src/notebookController.ts#L7) handles execution requests by sending scripts to a persistent background engine session.
- **Rich Outputs:** It captures output stream events from the REPL engine session.
  - Data grids are formatted and rendered as HTML tables.
  - Lineage maps are embedded as collapsible Markdown sections enclosing Mermaid graphs.
  - Error messages are piped into VS Code's standard notebook error objects.

---

## 14. Publish to Portal Command

**File:** [portalPublishCommand.ts](../../src/etl-sql-vscode/src/portalPublishCommand.ts)  
**Function:** [publishToPortal](../../src/etl-sql-vscode/src/portalPublishCommand.ts#L111)

The publish command allows direct publication of report scripts from the local workspace to the ETL-SQL Portal.

- **Authentication:** Prompts for portal credentials, performs login at `/api/auth/login`, and caches the JWT access token in `ExtensionContext.globalState` for 55 minutes.
- **Upload:** Uploads the raw script content to `/api/scripts/upload` to place it in the portal's storage area.
- **Registration:** Fetches destination folder trees via `/api/folders`, lets the user pick a folder via `showQuickPick`, prompts for report parameters, and registers the report record via a `POST` request to `/api/reports`.

---

## 15. Language & File Associations

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

## 16. Configuration Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `etlsql.executable.path` | `ETL-SQL.exe` | Path to the ETL-SQL CLI executable used for running scripts. |
| `etlsql.server.path` | *(empty)* | Path to `ETL-SQL-LSP.exe` / `ETL-SQL.LanguageServer`. Auto-discovered if empty. |
| `etlsql.report.executable.path` | `ETL-SQL-Report.exe` | Path to the report CLI used by the preview/export/publish workflows. |
| `etlsql.portal.url` | *(empty)* | Base URL of the ETL-SQL Portal used by report publishing. |
| `etlsql.report.autoOpenPreview` | `false` | Automatically open preview panel when a `.rptsql` file is opened. |

---

## 17. Extension Points for Contributors

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
