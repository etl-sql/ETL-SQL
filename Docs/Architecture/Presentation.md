# ETL-SQL Presentation Layer Architecture

**Applies to ETL-SQL 0.15.0**

This document describes the current presentation surfaces around the ETL-SQL engine: CLI output, JSON/REPL output for VS Code, the terminal UI, the language server, and the reporting browser runtime. It is intentionally descriptive. Do not treat this as a target design document.

---

## 1. Current Boundary

The engine evaluates parsed AST objects and records execution telemetry. Presentation is handled by host projects around the engine:

```
Script text
   │
   ├─ ETL-SQL.App / EngineRunner
   │    ├─ normal CLI output through ILogger / Spectre.Console
   │    └─ JSON mode packets over stdout for VS Code
   │
   ├─ ETL-SQL.TUI
   │    └─ terminal editor, result viewer, execution tree, and REPL views
   │
   ├─ ETL-SQL.LanguageServer
   │    └─ diagnostics, completions, hover, formatting, and navigation over LSP
   │
   └─ Report hosts
        ├─ ETL-SQL.Reporting builds manifests and static exports
        ├─ ETL-SQL.ReportHosting manages live report sessions
        ├─ ETL-SQL.ReportPlayer hosts local dashboards
        └─ ETL-SQL.ReportPortal hosts authenticated dashboards and admin UI
```

There is no shared presentation-sink abstraction in the current source tree. Presentation is host-specific: CLI, TUI, VS Code, language server, ReportPlayer, and ReportPortal each own their own rendering or protocol boundary.

---

## 2. Engine Telemetry

The engine exposes presentation-relevant state through runtime objects rather than through UI callbacks:

| Surface | Source |
| :--- | :--- |
| Execution tree | `Evaluator.Telemetry.ExecutionTree` |
| Statement profile metrics | `Evaluator.Telemetry.ProfileMetrics` |
| Rows processed and spill counters | `Evaluator.Telemetry` |
| Variables in a persistent session | `evaluator.VarContext.GetVariablesWithMetadata()` |
| Result sets | `Evaluator.LastResultSets` / result formatter paths |
| Report definitions | `IExecutionContext.ReportContext` |

Hosts choose how to display or serialize that state. The core rule is that engine and connector code should not directly own UI behavior. CLI and host projects may write to console/stdout when that is their transport.

---

## 3. CLI and VS Code JSON Mode

`ETL-SQL.App\App\EngineRunner.cs` is the main bridge between script execution and command-line presentation.

### Normal CLI Mode

Normal CLI execution uses `ILogger` and Spectre.Console output for human-readable messages, errors, tables, and performance summaries. This path is intended for terminal users and command-line scripts.

### JSON Mode

VS Code runs the app in JSON mode via the REPL manager. In this mode, `EngineRunner` writes newline-delimited JSON packets to stdout and keeps raw diagnostics on stderr.

Important packet types:

| Packet | Purpose |
| :--- | :--- |
| `status` | REPL ready/build status |
| `clear` | Tell the client to clear views before a run |
| `message` | User-facing log/warning/error text |
| `results` | Tabular result data |
| `progress` | Execution tree snapshot |
| `variables` | Current session variable metadata |
| `performance` | Timings, row counts, memory/spill counters |
| `done` | Completion marker with `exitCode` |

The VS Code extension treats stdout as the JSON protocol stream. Non-protocol diagnostics should go to stderr or the extension output channel.

---

## 4. VS Code Extension Presentation

`src/etl-sql-vscode` owns the editor-facing presentation layer.

| Component | Responsibility |
| :--- | :--- |
| `extension.ts` | Activation, command registration, LSP startup, REPL launch, report commands |
| `ReplManager.ts` | Long-lived `ETL-SQL.App ui repl` child process and JSON packet parsing |
| `resultsPanel.ts` | Webview host for the React results UI |
| `ui/src/*` | React panels for results, variables, metadata, reports, and pipeline views |
| `connectionsProvider.ts` | Connections/sidebar tree |
| `reportPreviewPanel.ts` | Builds `.rptsql` manifests and renders them with shared report runtime assets |

The extension has two independent backend channels:

- LSP over stdio for diagnostics, completions, hover, formatting, and navigation.
- REPL JSON over stdio for execution, result display, variables, and progress.

---

## 5. Terminal UI

`ETL-SQL.TUI` owns the terminal IDE experience. It uses the project's custom terminal renderer with Spectre.Console-style output and should keep UI work inside the TUI project.

Key responsibilities:

- Editor buffers and keyboard navigation.
- Result viewing and script execution actions.
- REPL-style interaction.
- Execution tree/demo rendering.
- Terminal-specific formatting and layout.

The TUI may consume engine telemetry, result sets, and logger output, but it should not move terminal UI concepts into `ETL-SQL.Engine` or connector projects.

---

## 6. Language Server Presentation

`ETL-SQL.LanguageServer` presents static analysis through LSP:

- Parser and lint diagnostics.
- Completion items for keywords, functions, variables, connections, tables, columns, and portal datasets.
- Hover cards for lineage, connections, and datasets.
- Go-to-definition for variables, temp tables, and connections.
- Document formatting through `ETL_SQL.Core.Formatting.SqlFormatter`.

The language server uses `ETL-SQL.Analysis` for linting and lineage over Core AST objects. It does not execute scripts for normal diagnostics.

---

## 7. Reporting Runtime Presentation

Browser report presentation has a single canonical runtime source:

```
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

Host copies under ReportPlayer, ReportPortal, and VS Code media are generated sync outputs. Edit the shared runtime first, then run:

```powershell
node .\scripts\sync-assets.js
node .\scripts\sync-assets.js -Check
```

Report presentation layers:

| Project | Responsibility |
| :--- | :--- |
| `ETL-SQL.Reporting` | Manifest building, chart option generation, Markdown/PDF/CSV/SVG export |
| `ETL-SQL.ReportHosting` | Live dashboard sessions, parameters, selective refresh, drill state |
| `ETL-SQL.ReportPlayer` | Local Kestrel host and static asset serving |
| `ETL-SQL.ReportPortal` | Authenticated portal UI, report catalog, snapshots, subscriptions, sharing, embeds, saved views, alerts |
| `src/etl-sql-vscode/media` | Generated VS Code preview runtime assets |

---

## 8. Security and Logging Notes

- Engine and connector code should use injected `ILogger` or the logger available from `IExecutionContext`.
- Connector and handler filesystem access must go through `IExecutionContext.ResolvePath()`.
- CLI/host projects may write to stdout when stdout is their protocol or user interface.
- JSON protocol output must avoid secrets, connection strings, tokens, and `ENC:` values.
- Portal audit records are database-backed operational records; make them tamper-resistant by forwarding logs or database changes to protected external storage when required.

---

## 9. Troubleshooting

| Symptom | Check |
| :--- | :--- |
| VS Code results panel hangs | Confirm a `done` packet is emitted by `EngineRunner` and parsed by `ReplManager`. |
| VS Code progress tree does not update | Confirm JSON mode is active and `progress` packets are emitted during execution. |
| Variables tab is stale | Confirm `variables` packets are emitted and `ReplManager.onVariablesChange` reaches the sidebar/results UI. |
| Report preview differs from ReportPlayer | Sync shared runtime assets and verify the generated host copies. |
| Browser chart is blank | Check `VisualManifest.ChartConfig`, local `echarts.min.js`, and runtime console errors. |
| CLI output includes protocol JSON | Confirm the app is not running with `--json` or through `ui repl`. |
