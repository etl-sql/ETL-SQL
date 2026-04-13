# ETL-SQL Reporting Architecture & Engineering Reference

**Version 1.0**

This document describes the internal mechanics of the ETL-SQL reporting subsystem — the layer responsible for parsing `.rptsql` files, evaluating their data sources, building serializable manifests, and serving interactive dashboards. It is the primary reference for engineers working on `ETL-SQL.ReportBuilder`, `ETL-SQL.ReportBuilder.CLI`, and `ETL-SQL.ReportPlayer`.

For the user-facing syntax reference, see [Docs/Report_SQL_Guide.md](../Report_SQL_Guide.md).

---

## 1. Architecture Overview

```
.rptsql script file
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  ETL-SQL.Core  (shared with all other subsystems)             │
│  Lexer → Parser → ReportAst nodes                             │
│  CREATE VISUAL / CREATE PAGE / CREATE DATASET statements      │
└───────────────────────────────┬───────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│  ETL-SQL.Engine  (Evaluator)                                  │
│  CreateVisualStatementHandler  → VisualDefinitions[]          │
│  CreatePageStatementHandler    → PageDefinitions[]            │
│  CreateDatasetStatementHandler → SELECT INTO #temp            │
│  (all other ETL statements execute normally in same context)  │
└───────────────────────────────┬───────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│  ETL-SQL.ReportBuilder                                        │
│  ManifestBuilder   — queries visuals, materialises rows       │
│  ChartJsRenderer   — produces Chart.js config JSON            │
│  MarkdownRenderer  — produces GFM output with embedded charts │
│  SnapshotStore     — persists / loads .snapshot.json          │
└──────────────┬────────────────────────────────────────────────┘
               │
       ┌───────┴──────────────────────────────────────┐
       ▼                                              ▼
ETL-SQL.ReportBuilder.CLI              ETL-SQL.ReportPlayer
build / refresh / serve                Kestrel HTTP + DashboardService
  .md  .json  .snapshot.json           GET /  POST /api/parameter
                                       localhost:5200
```

---

## 2. Project Responsibilities

| Project | Role |
|---------|------|
| `ETL-SQL.Core` | Report-SQL lexer tokens, AST nodes (`ReportAst.cs`), parser (`StatementParser.Report.cs`) |
| `ETL-SQL.Engine` | Statement handlers that register visual/page/dataset definitions into `IExecutionContext` |
| `ETL-SQL.ReportBuilder` | Manifest building, Chart.js rendering, Markdown rendering, snapshot persistence |
| `ETL-SQL.ReportBuilder.CLI` | `etl-sql-report` CLI — `build`, `refresh`, `serve` sub-commands |
| `ETL-SQL.ReportPlayer` | Kestrel web server with live parameter binding and on-demand rebuild |

---

## 3. Parse Pipeline

Report-SQL files use the same lexer and parser as standard ETL-SQL scripts. Report-specific statements are handled by `StatementParser.Report.cs`.

### 3.1 Tokenization

`Lexer` in `ETL-SQL.Core/Parser/Lexer.cs` tokenizes `.rptsql` source into a `List<Token>`. Report-SQL keywords (`VISUAL`, `PAGE`, `DATASET`, `MAPPINGS`, `OPTIONS`, `ACTIONS`, `STRUCTURE`, `MAP`, `SOURCE`, `SLICER`, etc.) are defined in `TokenType.cs`.

### 3.2 Parser Dispatch

`StatementParser.DispatchStatement()` routes to report-specific parsers:

| Token sequence | Parser method | Result |
|---|---|---|
| `CREATE VISUAL` | `ParseCreateVisual()` | `CreateVisualStatement` |
| `CREATE PAGE` | `ParseCreatePage()` | `CreatePageStatement` |
| `CREATE DATASET` | `ParseCreateDataset()` | `CreateDatasetStatement` |

Non-report statements (`SELECT`, `INSERT`, `DECLARE`, etc.) parse and execute normally in the same script context, allowing data preparation and visual definition to coexist in a single file.

### 3.3 AST Nodes (`ETL-SQL.Core/ReportAst.cs`)

All nodes are C# records (immutable value types).

#### `CreateVisualStatement`

```
Name         — identifier used in page slot maps
VisualType   — Bar | Line | Scatter | Pie | Table | Card | Slicer
Title        — optional display title (Markdown supported)
Subtitle     — optional display subtitle (Markdown supported)
Source       — VisualSourceExpression (inline SELECT or #temp reference)
Mappings     — list of VisualMapping (role → column, e.g. x → Region)
Options      — flat key-value pairs (legend, colors, stacked, etc.)
AxisOptions  — per-axis X_AXIS / Y_AXIS config blocks
Actions      — list of VisualAction (ON_CLICK, ON_CHANGE triggers)
```

#### `CreatePageStatement`

```
Name         — page identifier
Structure    — CSS grid template-areas string (e.g. 'A A / B C')
SlotMap      — Dictionary<string, string>: slot letter → visual name
Parameters   — list of PageParameter (name, default value)
```

#### `CreateDatasetStatement`

```
TempTableName   — #name of the resulting temp table
RefreshInterval — advisory "1 HOUR" / "15 MINUTES" string
Ttl             — time-to-live advisory
Compress        — store compressed
Encrypt         — encrypt on disk (requires KeyFile)
KeyFile         — path to encryption key
SourceQuery     — SelectStatement materialised into TempTableName
```

#### Visual Action sub-nodes

| Type | Fields | Runtime effect |
|---|---|---|
| `SetParameterAction` | trigger, paramName, value | Updates a `@param` and triggers rebuild |
| `DrillDownAction` | trigger, targetVisual, keyColumn | Navigate to target visual with row context |

---

## 4. Execution: Statement Handlers

### 4.1 `CreateVisualStatementHandler`

- Validates that any referenced `#temp` source exists in `IExecutionContext.Connections`
- Registers the statement: `context.VisualDefinitions[stmt.Name] = stmt`
- Does **not** query data at this point — data is queried by `ManifestBuilder`

### 4.2 `CreatePageStatementHandler`

- Validates all slot-mapped visual names exist in `VisualDefinitions`
- Registers: `context.PageDefinitions[stmt.Name] = stmt`

### 4.3 `CreateDatasetStatementHandler`

- Validates encryption key is present if `Encrypt = true`
- Rewrites to an equivalent `SELECT INTO #tempName FROM (source)` and executes it
- Logs a refresh advisory for the scheduler (actual scheduling is deferred — see Rpt-1 in TODO)

### 4.4 `IReportContext` (on `IExecutionContext`)

```csharp
IDictionary<string, CreateVisualStatement> VisualDefinitions { get; }
IDictionary<string, CreatePageStatement>   PageDefinitions   { get; }
```

`ManifestBuilder` reads both dictionaries after script evaluation completes.

---

## 5. Manifest Building (`ETL-SQL.ReportBuilder`)

### 5.1 Data Flow

```
IExecutionContext (post-evaluation)
        │
        ▼
ManifestBuilder.BuildAsync(context)
        │
        ├─ foreach VisualDefinitions
        │       │
        │       ├─ Execute source query → DataTable
        │       ├─ Materialize rows → List<List<string?>>
        │       ├─ Copy mapping hints as "mapping:{role}" options
        │       └─ ChartJsRenderer.Render(vm) → Chart.js JSON
        │
        ├─ foreach PageDefinitions
        │       └─ Copy structure, slot map, parameter defaults
        │
        └─ foreach Connections where key starts with '#'
                └─ Count rows → DatasetManifest
```

### 5.2 `ReportManifest` (serializable POCO)

```
Source      — script file path
BuiltAt     — UTC timestamp
Visuals     — List<VisualManifest>
Pages       — List<PageManifest>
Datasets    — List<DatasetManifest>
```

### 5.3 `VisualManifest`

```
Name        — visual identifier
VisualType  — string ("Bar", "Table", etc.)
ChartConfig — Chart.js JSON string (null for Table / Card / Slicer)
Columns     — List<string> column names
Rows        — List<List<string?>> — all data as strings for portability
Options     — Dictionary<string, string> (includes "mapping:{role}" entries)
```

All numeric data is serialized as strings in `Rows` to avoid JSON type loss and ensure the client runtime can format values appropriately.

---

## 6. Rendering

### 6.1 `ChartJsRenderer`

Converts a `VisualManifest` into a Chart.js configuration JSON string.

| VisualType | Chart.js type | Key roles |
|---|---|---|
| Bar | `bar` | x, y, series |
| Line | `line` | x, y, series |
| Scatter | `scatter` | x, y |
| Pie | `pie` | label, value |
| Table | *(none — HTML table)* | all columns |
| Card | *(none — scalar div)* | label, value |
| Slicer | *(none — `<select>`)* | value |

For multi-series charts, rows are pivoted: unique values in the `series` column become separate Chart.js datasets. Colors are assigned from a built-in palette.

### 6.2 `MarkdownRenderer`

Produces a static, portable `.md` file:

- Pages become top-level `##` sections
- Chart visuals: `<!-- CHART:{...config...} -->` comment block + GFM table fallback
- Table visuals: GFM pipe table
- Card visuals: blockquote `> **Label** Value`
- Slicer visuals: italic note *(interactive only — no static representation)*

### 6.3 Client-Side Runtime (`wwwroot/report-runtime.js`)

Dual-mode JavaScript file:

| Mode | Data source | Activation |
|---|---|---|
| VS Code preview | `window.__MANIFEST__` injected by extension | `window.__MANIFEST__` present |
| Web (ReportPlayer) | `GET /api/manifest` | default |

Rendering logic per visual type mirrors the server-side renderer but produces live DOM. Chart visuals use `new Chart(canvas, JSON.parse(config))`. Slicer controls post to `POST /api/parameter` on change, which triggers a server-side rebuild and manifest refresh.

---

## 7. SnapshotStore

**File:** `ETL-SQL.ReportBuilder/SnapshotStore.cs`  
**Format:** indented JSON at `<script-basename>.snapshot.json`

| Method | Behavior |
|---|---|
| `SaveAsync(manifest, path)` | Serialize manifest to JSON; overwrites existing file |
| `LoadAsync(path)` | Deserialize JSON → `ReportManifest`; returns `null` if absent |
| `IsStale(manifest, scriptPath, ttl?)` | True if script file is newer than `BuiltAt`, or TTL elapsed |

**Known gaps (see TODO Rpt-2):**
- Writes are not atomic — a crash mid-write can corrupt the file
- No reader/writer lock; concurrent reads and `CREATE DATASET` refreshes can race

---

## 8. Parameter & Slicer System

### 8.1 Declaration

Parameters are declared in `CREATE PAGE ... WITH PARAMETERS`:

```sql
CREATE PAGE Sales AS LAYOUT (
    STRUCTURE = 'A A / B C',
    MAP ( 'A' = RevChart, 'B' = RegionSlicer, 'C' = DetailTable )
)
WITH PARAMETERS (
    @region   = 'North America',
    @startDate = null
);
```

### 8.2 Propagation (DashboardService — web mode)

1. `DashboardService` maintains `Dictionary<string, string> _parameters`
2. Initial defaults are loaded from `PageParameter.DefaultValue`
3. On slicer interaction, browser posts `{ name: "@region", value: "Europe" }` to `POST /api/parameter`
4. `SetParameterAsync(name, value)` updates the dict and calls `RebuildAsync()`
5. `RebuildAsync()` prepends `DECLARE @region = 'Europe';` statements before the script, then re-executes in a fresh `Evaluator`

> **Note:** This is a full rebuild on every parameter change (Phase 9D simplified). Selective re-evaluation (only re-querying visuals whose `SourceSql` references the changed parameter) is tracked as **Rpt-1** in TODO.md.

### 8.3 Slicer Visuals

A `Slicer` visual executes a `SELECT` query to populate its options and binds to a `@param` name via its `ON_CHANGE = SET_PARAMETER(...)` action. In the web runtime it renders as a `<select>` dropdown. In Markdown output it is represented as a text note only.

---

## 9. Report Player (Web Server)

**Project:** `ETL-SQL.ReportPlayer`  
**Default port:** `localhost:5200`

### 9.1 Routes

| Route | Method | Behavior |
|---|---|---|
| `/` | GET | Returns full HTML page with embedded initial manifest |
| `/api/manifest` | GET | Returns current `ReportManifest` as JSON |
| `/api/parameter` | POST | Updates a parameter, triggers rebuild, returns new manifest |
| `/api/refresh` | GET | Forces full rebuild regardless of staleness |

### 9.2 Startup

`DashboardService` is registered as a singleton. On first request to `/` or `/api/manifest`, `GetManifestAsync()` lex-parses-evaluates the script, builds the manifest, and caches it. Subsequent requests return the cache until a parameter change or refresh invalidates it.

### 9.3 Static Assets

`wwwroot/` contains:

- `report-runtime.js` — client-side rendering runtime
- `chart.js` — Chart.js library (bundled locally for offline use)

---

## 10. CLI (`ETL-SQL.ReportBuilder.CLI`)

Invoked as `etl-sql-report <command>`.

| Command | Flags | Behaviour |
|---|---|---|
| `build <script.rptsql>` | `--output <file>`, `--format md\|json` | Lex → Parse → Evaluate → Manifest → write output file and `.snapshot.json` |
| `refresh <script.rptsql>` | | Re-execute script, overwrite `.snapshot.json` |
| `serve <script.rptsql>` | `--port <n>` | Launch `ETL-SQL.ReportPlayer` on specified port (default 5200) |

---

## 11. Execution Phases Reference

| Phase | What was built |
|---|---|
| **9A** | `ReportAst.cs` — `CreateVisualStatement`, `CreatePageStatement`, `CreateDatasetStatement` records; `StatementParser.Report.cs` |
| **9B** | `ManifestBuilder`, `ChartJsRenderer`, `MarkdownRenderer`, `SnapshotStore`, `ReportManifest` POCOs |
| **9C** | `report-runtime.js` — dual-mode client runtime for VS Code preview and web |
| **9D** | `DashboardService`, `ETL-SQL.ReportPlayer` Kestrel server, parameter binding, live rebuild |

---

## 12. Outstanding Work

| Item | Description |
|---|---|
| **Rpt-1** | Selective re-evaluation on parameter change — only re-query visuals whose source references the changed `@param` |
| **Rpt-2** | `SnapshotStore` write safety — atomic write via `.tmp` rename; `ReaderWriterLockSlim` for concurrent access |
| **Rpt-3** | Linter rule warning when column aliases shadow Report-SQL keywords (`VISUAL`, `PAGE`, `DATASET`, etc.) |
| **Rpt-4** | `STRUCTURE` string validation — every slot letter must appear in both `STRUCTURE` and `MAP(...)` |
| **Drill-down** | `DrillDownAction` defined in AST but not wired in client runtime |
| **Scheduled refresh** | `REFRESH EVERY` advisory is logged but requires external scheduler integration |
