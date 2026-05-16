# ETL-SQL Reporting Architecture & Engineering Reference

This document describes the internal mechanics of the ETL-SQL reporting subsystem — the layer responsible for parsing `.rptsql` files, evaluating their data sources, building serializable manifests, and serving interactive dashboards. It is the primary reference for engineers working on `ETL-SQL.ReportBuilder`, `ETL-SQL-Report`, and the reporting runtime.

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
│  CREATE VISUAL / CREATE PAGE / CREATE DATASET                 │
│  CREATE CONTAINER / CREATE NAVIGATION                         │
│  SET REPORT TITLE / SET REPORT DESCRIPTION                    │
└───────────────────────────────┬───────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│  ETL-SQL.Engine  (Evaluator)                                  │
│  CreateVisualStatementHandler    → VisualDefinitions[]        │
│  CreatePageStatementHandler      → PageDefinitions[]          │
│  CreateDatasetStatementHandler   → SELECT INTO #temp          │
│  CreateContainerStatementHandler → ContainerDefinitions[]     │
│  CreateNavigationStatementHandler→ NavigationDefinitions[]    │
│  SetReportMetadataStatementHandler → ReportTitle/Description  │
└───────────────────────────────┬───────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│  ETL-SQL.Reporting                                            │
│  ManifestBuilder   — queries visuals, materializes rows       │
│  EChartsRenderer   — produces ECharts option JSON             │
│  SvgChartRenderer  — server-side SVG for PDF export           │
│  PdfExporter       — QuestPDF-based PDF generation            │
│  MarkdownRenderer  — produces GFM output                      │
│  SnapshotStore     — persists / loads .snapshot.json          │
└──────────┬────────────────────────────────────────────────────┘
           │
   ┌───────┴─────────────────────────────────────┐
   ▼                                             ▼
ETL-SQL.ReportBuilder.CLI              ETL-SQL.ReportPlayer
build / refresh / serve                Kestrel HTTP + ReportHosting
  .md  .json  .pdf  .snapshot.json    Single-report: GET /
                                      Multi-report:  GET /  (catalog)
                                                     GET /reports/{name}
                                      localhost:5200
```

---

## 2. Project Responsibilities

| Project | Role |
|---------|------|
| `ETL-SQL.Core` | Report-SQL lexer tokens, AST nodes (`ReportAst.cs`), parser (`StatementParser.Report.cs`) |
| `ETL-SQL.Engine` | Statement handlers that register visual/page/dataset/container/navigation definitions into `IExecutionContext` |
| `ETL-SQL.Reporting` | Manifest building, ECharts rendering, SVG rendering, PDF/CSV/Markdown/terminal rendering, snapshot persistence, shared interaction refresh semantics |
| `ETL-SQL.ReportHosting` | Reusable report sessions, parameter state, selective refresh, manifest caching, background dataset refresh timers, and multi-report manifest factories |
| `ETL-SQL.ReportRuntime` | Canonical browser runtime assets (`report-runtime.js`, `echarts.min.js`, CSS themes) — sync to host projects via `scripts/sync-assets.ps1` |
| `ETL-SQL.ReportBuilder` | Engine-facing `EXPORT REPORT` statement handler compatibility assembly |
| `ETL-SQL.ReportBuilder.CLI` | `etl-sql-report` CLI — `build`, `refresh`, `serve` sub-commands |
| `ETL-SQL.ReportPlayer` | Local Kestrel web server, routes, HTML shell, static asset hosting, and report embedding |

---

## 3. Parse Pipeline

Report-SQL files use the same lexer and parser as standard ETL-SQL scripts. Report-specific statements are handled by `StatementParser.Report.cs`.

### 3.1 Tokenization

`Lexer` in `ETL-SQL.Core/Parser/Lexer.cs` tokenizes `.rptsql` source into a `List<Token>`. Report-SQL keywords (`VISUAL`, `PAGE`, `DATASET`, `MAPPINGS`, `OPTIONS`, `ACTIONS`, `STRUCTURE`, `MAP`, `SOURCE`, `SLICER`, `CONTAINER`, `NAVIGATION`, `DATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`, etc.) are defined in `TokenType.cs`.

### 3.2 Parser Dispatch

`StatementParser.DispatchStatement()` routes to report-specific parsers:

| Token sequence | Parser method | Result |
|---|---|---|
| `CREATE VISUAL` | `ParseCreateVisual()` | `CreateVisualStatement` |
| `CREATE OR ALTER VISUAL` | `ParseCreateVisual()` | `CreateVisualStatement` (Mode=CreateOrAlter) |
| `CREATE PAGE` | `ParseCreatePage()` | `CreatePageStatement` |
| `CREATE DATASET` | `ParseCreateDataset()` | `CreateDatasetStatement` |
| `CREATE CONTAINER` | `ParseCreateContainer()` | `CreateContainerStatement` |
| `CREATE NAVIGATION` | `ParseCreateNavigation()` | `CreateNavigationStatement` |
| `CREATE STYLE` | `ParseCreateStyle()` | `CreateStyleStatement` |
| `CREATE BUTTON` | `ParseCreateButton()` | `CreateButtonStatement` |
| `CREATE TEMPLATE` | `ParseCreateTemplate()` | `CreateTemplateStatement` |
| `ALTER <Type>` | `ParseAlterReportObject()` | `AlterReportObjectStatement` |
| `DROP <Type>` | `ParseDropReportObject()` | `DropReportObjectStatement` |
| `SET REPORT` | `ParseSetReportMetadata()` | `SetReportMetadataStatement` |

Non-report statements (`SELECT`, `INSERT`, `DECLARE`, etc.) parse and execute normally in the same script context, allowing data preparation and visual definition to coexist in a single file.

### 3.3 AST Nodes (`ETL-SQL.Core/ReportAst.cs`)

All nodes are C# records (immutable value types).

#### `CreateVisualStatement`

```
Name         — identifier used in page slot maps and container VISUALS lists
VisualType   — Bar | Line | Scatter | Pie | Donut | HorizontalBar | BoxPlot |
               Treemap | HeatMap | Combo | Gauge | Funnel | Waterfall |
               Table | Card | Text |
               Slicer | DatePicker | Slider | MultiSelect | Search
Title        — optional display title string
Subtitle     — optional display subtitle string
Source       — VisualSourceExpression (inline SELECT or #temp reference)
              (null / empty for Text, DatePicker, Slider, Search)
Mappings     — List<VisualMapping> (role → column, e.g. X → Region)
Options      — List<VisualOption> flat key-value pairs (STACKED, SMOOTH, FORMAT, etc.)
AxisOptions  — List<AxisOptions> per-axis X_AXIS / Y_AXIS config blocks
TypedSeries      — List<TypedSeries> for COMBO charts (BAR col, LINE col)
FormattingRules  — List<FormattingRule> for TABLE conditional cell colors
Styles           — Dictionary<string, string> (THEME, WIDTH, HEIGHT, BORDER, etc.)
StyleName        — optional name of a CREATE STYLE to inherit
Actions          — List<VisualAction> (ON_CLICK, ON_CHANGE triggers)
Mode             — Create | Alter | CreateOrAlter
```

#### `CreateStyleStatement`

```
Name   — style identifier
Styles — Dictionary<string, string> of CSS properties
Mode   — Create | Alter | CreateOrAlter
```

#### `CreateButtonStatement`

```
Name       — identifier
ButtonType — BUTTON
Title      — optional display label
Tooltip    — optional TooltipDefinition
Options    — visual options (e.g. ICON)
Actions    — click behavior (e.g. DRILL_DOWN, SET_PARAMETER)
Styles     — inline styles
StyleName  — style reference
Mode       — Create | Alter | CreateOrAlter
```

#### `CreateTemplateStatement`

```
Name    — template identifier
Options — default options/styles provided by the template
Mode    — Create | Alter | CreateOrAlter
```

#### `AlterReportObjectStatement`

```
ObjectType — Visual | Page | Container | Style | Navigation | Dataset | Template | Button
Name       — target name
...        — partial updates for Source, Mappings, Options, Styles, etc.
```

#### `DropReportObjectStatement`

```
ObjectType — same as above
Name       — target name
IfExists   — boolean (DROP ... IF EXISTS)
```

#### `CreatePageStatement`

```
Name       — page identifier
Structure  — CSS grid-template-areas string (e.g. 'A A / B C')
SlotMap    — Dictionary<string, string>: slot letter → visual/container name
Parameters — List<PageParameter> (name, optional DataType, optional default value)
Styles     — Dictionary<string, string> (THEME, BACKGROUND)
```

#### `CreateDatasetStatement`

```
TempTableName      — &name of the report dataset
RefreshInterval    — advisory interval string (e.g. '1h', '30m')
Ttl                — time-to-live advisory
Compress           — store compressed on disk
EncryptionMode     — None | MachineBound | Password | KeyFile
EncryptionPassword — password string (EncryptionMode = Password)
KeyFile            — path to key file (EncryptionMode = KeyFile)
SourceQuery        — SelectStatement materialized into TempTableName
```

#### `CreateContainerStatement`

```
Name          — identifier used in page MAP entries
ContainerType — "BOX" | "SCROLL"
Visuals       — List<string> ordered visual names
Styles        — Dictionary<string, string> (HEIGHT, WIDTH, BACKGROUND)
```

#### `CreateNavigationStatement`

```
Name        — identifier
NavType     — Tab | Button | Link
Orientation — Horizontal | Vertical
DefaultPage — optional page name shown on load (first in Pages if omitted)
Pages       — List<string> ordered page names
```

#### `SetReportMetadataStatement`

```
Key   — "TITLE" or "DESCRIPTION"
Value — the string value
```

#### Visual Action sub-nodes

| Type | Fields | Runtime effect |
|---|---|---|
| `SetParameterAction` | trigger, parameterName, valueExpression | Updates a `@param` and triggers selective rebuild |
| `DrillDownAction` | trigger, targetVisual, keyColumn | Navigate to target visual with row context |

---

## 4. Execution: Statement Handlers

### 4.1 `CreateVisualStatementHandler`

- Validates that any referenced `#temp` source exists in `IExecutionContext.Connections`
- Registers: `context.VisualDefinitions[stmt.Name] = stmt`
- Does **not** query data — data is queried by `ManifestBuilder`

### 4.2 `CreatePageStatementHandler`

- Validates all slot-mapped visual/container names exist in `VisualDefinitions` or `ContainerDefinitions`
- Registers: `context.PageDefinitions[stmt.Name] = stmt`

### 4.3 `CreateDatasetStatementHandler`

- Validates encryption configuration (KEYFILE mode requires a key path; PASSWORD mode requires a password)
- Rewrites to an equivalent `SELECT INTO #tempName FROM (source)` and executes it
- Registers: `context.DatasetDefinitions[tableName] = stmt` for manifest metadata

### 4.4 `CreateContainerStatementHandler`

- Registers: `context.ContainerDefinitions[stmt.Name] = stmt`
- No data query at registration time

### 4.5 `CreateNavigationStatementHandler`

- Registers: `context.NavigationDefinitions[stmt.Name] = stmt`

### 4.6 `SetReportMetadataStatementHandler`

- Sets `context.ReportTitle` (Key = "TITLE") or `context.ReportDescription` (Key = "DESCRIPTION")

### 4.7 `IReportContext` (on `IExecutionContext`)

```csharp
IDictionary<string, CreateVisualStatement>    VisualDefinitions     { get; }
IDictionary<string, CreatePageStatement>      PageDefinitions       { get; }
IDictionary<string, CreateDatasetStatement>   DatasetDefinitions    { get; }
IDictionary<string, CreateContainerStatement> ContainerDefinitions  { get; }
IDictionary<string, CreateNavigationStatement>NavigationDefinitions { get; }
string? ReportTitle       { get; set; }
string? ReportDescription { get; set; }
```

`ManifestBuilder` reads all dictionaries after script evaluation completes.

---

## 5. Manifest Building (`ETL-SQL.Reporting`)

### 5.1 Data Flow

```
IExecutionContext (post-evaluation)
        │
        ▼
ManifestBuilder.BuildAsync(scriptPath)
        │
        ├─ foreach VisualDefinitions
        │       ├─ Execute source query → rows
        │       ├─ Materialize rows → List<List<string?>>
        │       ├─ Copy mapping hints as "mapping:{role}" options
        │       ├─ Copy axis options as "axis:{x|y}:{key}" options
        │       ├─ Copy action bindings → List<VisualActionManifest>
        │       ├─ ApplyFormatting() — apply FORMAT option to value column
        │       └─ EChartsRenderer.Render(vm) → ECharts option JSON
        │
        ├─ foreach PageDefinitions
        │       └─ Copy structure, slot map, parameter defaults, styles
        │
        ├─ foreach ContainerDefinitions
        │       └─ Copy container type, visual list, styles
        │
        ├─ foreach NavigationDefinitions
        │       └─ Copy nav type, orientation, pages list
        │
        └─ foreach DatasetDefinitions
                └─ Count rows → DatasetManifest
```

### 5.2 `ReportManifest` (serializable POCO)

```
Source      — script file path
BuiltAt     — UTC timestamp
Title       — from SET REPORT TITLE
Description — from SET REPORT DESCRIPTION
Visuals     — List<VisualManifest>
Pages       — List<PageManifest>
Containers  — List<ContainerManifest>?  (null if none defined)
Navigations — List<NavigationManifest>? (null if none defined)
Datasets    — List<DatasetManifest>
```

### 5.3 `VisualManifest`

```
Name        — visual identifier
VisualType  — string ("Bar", "Donut", "HeatMap", etc.)
ChartConfig — ECharts option JSON string (null for Table / Card / Text / filter types)
Columns     — List<string> column names
Rows        — List<List<string?>> — all data as strings for portability
Options     — Dictionary<string, string> (mapping:x, axis:x:label, FORMAT, etc.)
Styles      — Dictionary<string, string>? (THEME, HEIGHT, etc.)
Actions     — List<VisualActionManifest>
SeriesDefs  — List<SeriesDefManifest>? (COMBO charts only)
Error       — string? (non-null if source query failed)
```

All numeric data is serialized as strings to avoid JSON type loss.

### 5.4 `ApplyFormatting`

Before `ChartConfig` is generated, `ApplyFormatting(vm)` applies the `FORMAT` option (a .NET numeric format string such as `N0`, `C2`, `P1`) to the value column of each row. This ensures formatted values appear in TABLE renders and CARD displays.

---

## 6. Rendering

### 6.1 `EChartsRenderer`

Converts a `VisualManifest` into an [Apache ECharts v5](https://echarts.apache.org/) option JSON string.

| VisualType | ECharts series type | Key roles |
|---|---|---|
| Bar | `bar` | x, y, series |
| HorizontalBar | `bar` (yAxis = category) | x, y, series |
| Line | `line` | x, y, series |
| Scatter | `scatter` | x, y |
| Pie | `pie` | label, value |
| Donut | `pie` (radius inner) | label, value |
| Combo | `bar` + `line` mixed | x, SERIES block |
| BoxPlot | `boxPlot` | x, value distribution |
| Treemap | `treemap` | label, value |
| HeatMap | `heatmap` | x, y, value |
| Gauge | `gauge` | value, max (optional), label (optional) |
| Funnel | `funnel` | label, value |
| Waterfall | stacked `bar` (transparent base + delta) | x, y |
| Table | *(none — HTML table with optional FORMATTING rules)* | all columns |
| Card | *(none — scalar div)* | label, value |
| Text | *(none — HTML div)* | VALUE option |
| Slicer / MultiSelect | *(none — `<select>` / checkboxes)* | value |
| DatePicker / Slider / Search | *(none — input controls)* | — |

For multi-series charts, rows with a `series` column are pivoted: each distinct series value becomes a separate ECharts dataset. Colors are assigned from a built-in palette or from `COLORS(...)` option entries (`color:{key}` in `vm.Options`).

The `LEGEND_POSITION` flat option in `vm.Options` controls ECharts legend placement (`TOP`, `BOTTOM`, `LEFT`, `RIGHT`).

#### Option Key Naming Convention

`VisualManifest.Options` is a `Dictionary<string, string>` shared between `VisualBuilder` (writer) and `EChartsRenderer` / the client runtime (readers). Keys follow a strict two-tier convention:

| Tier | Case | Written by | Examples |
|---|---|---|---|
| **Parser-supplied** | **UPPERCASE** | `VisualBuilder` — copied verbatim from the parsed option name | `STACKED`, `SMOOTH`, `FORMAT`, `LEGEND_POSITION`, `TITLE`, `SUBTITLE` |
| **Internally-computed** | **lowercase with colons** | `VisualBuilder` — synthesized during manifest build | `title`, `subtitle`, `mapping:x`, `mapping:value`, `axis:x:label`, `axis:y:min`, `color:Revenue` |

**Why two tiers?** Parser-supplied keys come directly from the source script (`OPTIONS(STACKED = ON)`), so they are stored as-is in uppercase to match the grammar. Internally-computed keys are synthesized by `VisualBuilder` from structured AST nodes (`AxisOptions`, `MappingHints`, `TypedSeries`) — they use lowercase-with-colon namespace notation to avoid clashing with any future parser keyword.

**Reading rules:**
- `EChartsRenderer` must read parser-supplied keys in **UPPERCASE** (e.g., `vm.Options.TryGetValue("TITLE", ...)`).
- `EChartsRenderer` must read internally-computed keys in **lowercase** (e.g., `vm.Options.TryGetValue("axis:x:label", ...)`).
- Mixing the cases is a silent bug — the dictionary lookup returns `null` and the option is silently dropped. Three rendering bugs in the project history were caused by this mismatch.

### 6.2 `SvgChartRenderer`

Server-side SVG generation used by `PdfExporter`. Produces static SVG markup for chart types without requiring a browser.

- `Render(VisualManifest vm, int width, int height) → string`
- Returns SVG XML; the PDF exporter embeds it via QuestPDF's `SvgImage` element

### 6.3 `PdfExporter`

Uses [QuestPDF](https://www.questpdf.com/) (Community License) to produce PDF output.

```csharp
public byte[] Export(ReportManifest manifest)
{
    QuestPDF.Settings.License = LicenseType.Community;
    return Document.Create(container => { ... }).GeneratePdf();
}
```

Layout:
- One QuestPDF page per `VisualManifest`
- Chart types → SVG at 500×292 pt via `SvgChartRenderer`, embedded as `SvgImage`
- TABLE → native QuestPDF table, capped at 500 rows
- CARD → label + large-text value
- TEXT → `VALUE` option rendered as paragraph
- SLICER / filter types → placeholder paragraph

### 6.4 `MarkdownRenderer`

Produces a static, portable `.md` file:

- Pages become top-level `##` sections
- Chart visuals: GFM table of raw data (ECharts config is not embedded in Markdown output)
- Table visuals: GFM pipe table
- Card visuals: blockquote `> **Label** Value`
- Slicer / filter visuals: italic note *(interactive only — no static representation)*

### 6.5 Client-Side Runtime (`wwwroot/report-runtime.js`)

Dual-mode JavaScript file:

| Mode | Data source | Activation |
|---|---|---|
| VS Code preview | `window.__MANIFEST__` injected by extension | `window.__MANIFEST__` present |
| Single-report web | `window.__MANIFEST__` pre-embedded in HTML | `window.__IS_WEB__ = true` |
| Multi-report web | `GET {apiBase}/manifest` | `window.__IS_WEB__ = true`, no pre-embedded manifest |

`window.__API_BASE__` is injected in multi-report mode as `/reports/{name}/api`. All API calls use `apiBase` as their prefix so the same script works for both single and multi-report deployments.

Chart visuals use `echarts.init(div)` + `chart.setOption(JSON.parse(config))`. Filter controls (`SLICER`, `MULTISELECT`, `DATEPICKER`, `SLIDER`, `SEARCH`) call `POST {apiBase}/parameters` with a batch payload on change.

---

## 7. SnapshotStore

**File:** `ETL-SQL.Reporting/SnapshotStore.cs`
**Format:** indented JSON at `<script-basename>.snapshot.json`

| Method | Behavior |
|---|---|
| `SaveAsync(manifest, path)` | Serialize manifest to JSON; overwrites existing file |
| `LoadAsync(path)` | Deserialize JSON → `ReportManifest`; returns `null` if absent |
| `IsStale(manifest, scriptPath, ttl?)` | True if script file is newer than `BuiltAt`, or TTL elapsed |

**Known gaps:**
- Writes are not atomic — a crash mid-write can corrupt the file
- No reader/writer lock; concurrent reads and `CREATE DATASET` refreshes can race

---

## 8. Parameter & Slicer System

### 8.1 Declaration

Parameters are declared in `CREATE PAGE ... WITH PARAMETERS`:

```sql
CREATE PAGE Sales AS (
    STRUCTURE = 'A A / B C',
    MAP ( 'A' = RevChart, 'B' = RegionSlicer, 'C' = DetailTable )
)
WITH PARAMETERS (
    @region    = 'All',
    @startDate = '2024-01-01'
);
```

### 8.2 Propagation (ReportHosting DashboardService — web mode)

1. `ETL-SQL.ReportHosting.DashboardService` maintains `Dictionary<string, string> _parameters`
2. Initial defaults are loaded from `PageParameter.DefaultValue`
3. On filter interaction, browser posts to `POST /api/parameter` (single) or `POST /api/parameters` (batch)
4. `SetParameterAsync` / `SetParametersAsync` updates the dict
5. The service checks which visuals depend on the changed parameter(s) via `DependsOnVariable()` inspection
6. Only affected visuals are re-queried via `ManifestBuilder.RefreshVisualAsync()`. If no affected visuals are detected, a full `RebuildAsync()` is performed as a fallback.

### 8.3 Batch Parameter Updates

`POST /api/parameters` accepts a JSON body:

```json
{ "params": [{ "name": "@region", "value": "East" }, { "name": "@year", "value": "2026" }] }
```

The `report-runtime.js` `postParameters(params)` helper is used by all filter controls to send batch updates, reducing round-trips when multiple parameters change simultaneously.

### 8.4 Slicer and Filter Visuals

| Type | Source | Trigger |
|---|---|---|
| `SLICER` | SOURCE query populates `<select>` options | `ON_CHANGE` on selection |
| `MULTISELECT` | SOURCE query populates checkbox list | `ON_CHANGE` on any checkbox |
| `DATEPICKER` | No source — calendar picker | `ON_CHANGE` on date input |
| `SLIDER` | No source — range input | `ON_CHANGE` on slider move |
| `SEARCH` | No source — text input | `ON_CHANGE` with 350 ms debounce |

All filter types use `SET_PARAMETER` in their `ACTIONS` clause to bind the selected value to a `@param`.

---

## 9. Report Player (Web Server)

**Project:** `ETL-SQL.ReportPlayer`  
**Default port:** `localhost:5200`

### 9.1 Single-Report Routes

| Route | Method | Behavior |
|---|---|---|
| `/` | GET | HTML page with pre-embedded manifest and `window.__IS_WEB__ = true` |
| `/api/manifest` | GET | Current `ReportManifest` as JSON |
| `/api/parameter` | POST | Set one parameter, selective rebuild, return new manifest |
| `/api/parameters` | POST | Set multiple parameters, selective rebuild, return new manifest |
| `/api/refresh` | GET | Force full rebuild, return new manifest |

### 9.2 Multi-Report Routes

| Route | Method | Behavior |
|---|---|---|
| `/` | GET | Catalog page listing all reports from `reports.json` |
| `/reports/{name}` | GET | Dashboard HTML for the named report (injects `window.__API_BASE__`) |
| `/reports/{name}/api/manifest` | GET | Report manifest JSON |
| `/reports/{name}/api/parameter` | POST | Set one parameter |
| `/reports/{name}/api/parameters` | POST | Set multiple parameters |
| `/reports/{name}/api/refresh` | GET | Force rebuild |

### 9.3 `DashboardServiceFactory` (multi-report)

`ETL-SQL.ReportHosting.DashboardServiceFactory` maintains a `ConcurrentDictionary<string, DashboardService>` keyed by report name. `GetService(name)` uses `GetOrAdd` for lazy, thread-safe service creation. Relative paths in `reports.json` are resolved against the manifest file's directory.

### 9.4 Startup

**Single-report:** `DashboardService` is registered as a singleton. On first request, `GetManifestAsync()` evaluates the script and caches the manifest. Subsequent requests return the cache until a parameter change or refresh invalidates it.

**Multi-report:** `DashboardServiceFactory` is registered as a singleton. Individual `DashboardService` instances are created on first access per report.

### 9.5 Static Assets

`wwwroot/` contains:

- `report-runtime.js` — client-side rendering runtime
- ECharts is loaded from CDN (no local bundle required)

---

## 10. CLI (`ETL-SQL.ReportBuilder.CLI`)

Invoked as `etl-sql-report <command>`.

| Command | Flags | Behavior |
|---|---|---|
| `build <script.rptsql>` | `--output <file>`, `--format md\|json\|pdf` | Lex → Parse → Evaluate → Manifest → write output file and `.snapshot.json` |
| `refresh <script.rptsql>` | | Re-execute script, overwrite `.snapshot.json` |
| `serve <script.rptsql>` | | Launch `ETL-SQL.ReportPlayer` in single-report mode, open browser |
| `serve --manifest <reports.json>` | | Launch `ETL-SQL.ReportPlayer` in multi-report mode, open catalog |

---

## 11. Execution Phases Reference

| Phase | What was built |
|---|---|
| **9A** | `ReportAst.cs` — core AST nodes; `StatementParser.Report.cs` — CREATE VISUAL / PAGE / DATASET |
| **9B** | `ManifestBuilder`, `EChartsRenderer`, `MarkdownRenderer`, `SnapshotStore`, `ReportManifest` POCOs |
| **9C** | `report-runtime.js` — dual-mode client runtime for VS Code preview and web |
| **9D** | `ETL-SQL.ReportHosting.DashboardService`, `ETL-SQL.ReportPlayer` Kestrel server, parameter binding, live rebuild |
| **9E** | Filter visual types (DATEPICKER, SLIDER, MULTISELECT, SEARCH), batch parameter endpoint, responsive layout, page-level THEME |
| **9F** | Multi-report hosting (`DashboardServiceFactory`, `reports.json`, catalog page, per-report API prefix) |
| **9G** | PDF export (`SvgChartRenderer`, `PdfExporter` via QuestPDF), `--format pdf` CLI flag |
| **9H** | CREATE CONTAINER, CREATE NAVIGATION, SET REPORT TITLE/DESCRIPTION, COMBO visual type, STYLE clause, COLORS/LEGEND options |

---

## 12. Outstanding Work

| Item | Description |
|---|---|
| **Rpt-2** | `SnapshotStore` write safety — atomic write via `.tmp` rename; `ReaderWriterLockSlim` for concurrent access |
| **Rpt-3** | Linter rule warning when column aliases shadow Report-SQL keywords |
| **Rpt-4** | `STRUCTURE` string validation — every slot letter must appear in both `STRUCTURE` and `MAP(...)` |
| **Drill-down** | `DrillDownAction` defined in AST and partially wired in client runtime; full UX pending |
| **Scheduled refresh** | `REFRESH EVERY` advisory is stored but requires Orchestrator integration to act on it |
| **Excel export** | `--format xlsx` via ClosedXML or EPPlus — not yet implemented |
