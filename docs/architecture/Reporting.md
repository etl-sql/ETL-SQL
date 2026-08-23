# ETL-SQL Reporting Architecture & Engineering Reference

This document describes the internal mechanics of the ETL-SQL reporting subsystem — the layer responsible for parsing `.rptsql` files, evaluating their data sources, building serializable manifests, and serving interactive dashboards. It is the primary reference for engineers working on `ETL-SQL.ReportBuilder`, `ETL-SQL-Report`, and the reporting runtime.

For the user-facing syntax reference, see [docs/guides/report-sql.md](../guides/feature-guides/report-sql.md).

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
│  CreateDatasetStatementHandler   → #temp + optional portal    │
│                                    registry/Parquet cache      │
│  Use/Refresh/Export/PublishDataset handlers                    │
│  CreateContainerStatementHandler → ContainerDefinitions[]     │
│  CreateNavigationStatementHandler→ NavigationDefinitions[]    │
│  SetReportMetadataStatementHandler → ReportTitle/Description  │
└───────────────────────────────┬───────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│  ETL-SQL.Reporting                                            │
│  ManifestBuilder   — queries visuals, materializes rows       │
│  NamedVisualChartLowerer — builds ChartSpec semantics         │
│  PlotPlanResolver  — resolves scales, marks, and facets        │
│  SvgChartRenderer  — native SVG for browser/PDF export        │
│  PdfExporter       — static PDF generation                    │
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
                                      dynamic localhost port by default
```

---

## 2. Project Responsibilities

| Project | Role |
|---------|------|
| `ETL-SQL.Core` | Report-SQL lexer tokens, AST nodes (`ReportAst.cs`), parser (`StatementParser.Report.cs`) |
| `ETL-SQL.Engine` | Statement handlers that register report definitions, materialize datasets, enforce registry decisions, and perform atomic dataset refresh/export/publish file transitions |
| `ETL-SQL.Reporting.Contracts` | Versioned renderer-neutral `ChartSpec`, typed chart data, and resolved `PlotPlan` contracts |
| `ETL-SQL.Reporting` | Manifest building, semantic lowering, native SVG and specialized layouts, PDF/CSV/Markdown/terminal rendering, snapshot persistence, shared interaction refresh semantics |
| `ETL-SQL.ReportHosting` | Reusable report sessions, parameter state, selective refresh, manifest caching, background dataset refresh timers, and multi-report manifest factories |
| `ETL-SQL.ReportRuntime` | Canonical browser runtime assets (`report-runtime.js`, CSS themes, Tabulator assets, and GeoJSON) — sync to host projects via `node .\scripts\sync-assets.js` and verify with `node .\scripts\sync-assets.js -Check` |
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
| `USE DATASET` | `ParseUseDataset()` | `UseDatasetStatement` |
| `REFRESH DATASET` | `ParseRefreshDataset()` | `RefreshDatasetStatement` |
| `EXPORT DATASET` | `ParseExportDataset()` | `ExportDatasetStatement` |
| `PUBLISH DATASET` | `ParsePublishDataset()` | `PublishDatasetStatement` |
| `eng.tables` | Normal `SELECT` parsing | Engine/Portal catalog data source |
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
               Bubble | Radar | Candlestick | Map | Gantt |
               Sankey | Sunburst | Network | Trellis | Matrix |
               Table | Card | Text | Image |
               Slicer | DatePicker | RelDatePicker | Slider | MultiSelect |
               Search | Checkbox | Textbox | Numberbox
Title        — optional display title string
TitleIsMarkdown / SubtitleIsMarkdown — markdown flags for title text
Subtitle     — optional display subtitle string
Source       — VisualSourceExpression (inline SELECT or #temp reference)
              (null / empty for Text, Image, DatePicker, Slider, Search, Checkbox, Textbox, Numberbox)
Mappings     — List<VisualMapping> (role → column, e.g. X → Region)
Options      — List<VisualOption> flat key-value pairs (STACKED, SMOOTH, FORMAT, etc.)
AxisOptions  — List<AxisOptions> per-axis X_AXIS / Y_AXIS config blocks
TypedSeries      — List<TypedSeries> for COMBO charts (BAR col, LINE col)
FormattingRules  — List<FormattingRule> for TABLE conditional cell colors
Overlays         — List<VisualOverlay> for GOAL / MOVING_AVG / regression overlays
Summaries        — List<TableSummaryItem> for MATRIX/TABLE aggregate summaries
SummaryOptions   — TableSummaryOptions for grand totals and summary placement
Styles           — Dictionary<string, string> (THEME, WIDTH, HEIGHT, BORDER, etc.)
StyleName        — optional name of a CREATE STYLE to inherit
Actions          — List<VisualAction> (ON_CLICK, ON_CHANGE triggers)
Interactions     — List<VisualInteraction> for cross-visual filter/highlight/select behavior
Tooltip          — optional text/container/inline tooltip
Visible          — false when VISIBLE = OFF defers data fetch until runtime
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
AccessLevel        — Private (default) | Public
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
| `DrillDownAction` | trigger, targetVisual, keyColumns | Navigate to target visual with row context |
| `DrillInAction` | trigger, hierarchy | Drill within the same visual by hierarchy level |
| `RunScriptAction` | trigger, scriptPath, parameters | Runs an ETL-SQL script through the report host |
| `ClearFiltersAction` | trigger | Clears active client-side filters |
| `ApplyParametersAction` | trigger | Applies staged parameter edits |
| `ReportCommandAction` | trigger, command | Runs a named report-runtime command |
| `DrillReportAction` | trigger, targetReport, parameters | Opens another report with parameter bindings |
| `NavigatePageAction` | trigger, targetPage | Switches to a report page |
| `RefreshVisualsAction` | trigger, targets | Selectively refreshes named visuals |
| `SetUiStateAction` | trigger, targets, key, value | Updates runtime UI state for target visuals |

---

## 4. Execution: Statement Handlers

### 4.1 `CreateVisualStatementHandler`

- Validates that any referenced `#temp` source exists in `IExecutionContext.Connections`
- Registers: `context.VisualDefinitions[stmt.Name] = stmt`
- Does **not** query data — data is queried by `ManifestBuilder`

### 4.2 `CreatePageStatementHandler`

- Validates all slot-mapped visual/container names exist in `VisualDefinitions` or `ContainerDefinitions`
- Validates all slot-mapped visual/container/button names exist in `VisualDefinitions`, `ContainerDefinitions`, or `ButtonDefinitions`
- Registers: `context.PageDefinitions[stmt.Name] = stmt`

### 4.3 `CreateDatasetStatementHandler`

- Rewrites the source to an equivalent `SELECT INTO &dataset` and materializes it in engine memory.
- In standalone mode, applies the statement's `MACHINE`, `PASSWORD`, or `KEYFILE` encryption directly
  when writing a Parquet cache.
- In portal mode, resolves the global dataset name through `IDatasetRegistry`, forwards the real caller
  context, and serves a fresh TTL cache without rerunning the source query.
- Portal writes always use the configured portal at-rest key, stamp its non-secret key version, write to
  a staging file, atomically replace the managed cache, and then update registry metadata.
- `CREATE OR ALTER` requires dataset Editor/Owner permission. A new report-created dataset is linked to
  its owning report and folder.
- Registers `context.DatasetDefinitions[tableName] = stmt` for manifest metadata.

### 4.4 Shared Dataset Handlers

- `USE DATASET` resolves by globally unique name. It loads the managed cache only after centralized
  registry authorization and serves the last complete snapshot with a warning when TTL is stale.
- Portal dataset catalog queries return only datasets visible to the caller.
- `REFRESH DATASET` requires Refresh or higher permission, reruns the stored source, and preserves the
  prior cache if materialization or registry update fails.
- `EXPORT DATASET` requires read permission and creates a failure-atomic portable copy encrypted with a
  one-operation PASSWORD or KEYFILE transport credential.
- `PUBLISH DATASET` requires destination folder Manage permission, decrypts the transport copy once,
  re-encrypts with the destination portal at-rest key, and rolls back its row/files on failure.
- The published portal cache is not a transport artifact. Keep the original export if another transfer
  may be required.

### 4.5 `CreateContainerStatementHandler`

- Registers: `context.ContainerDefinitions[stmt.Name] = stmt`
- No data query at registration time

### 4.6 `CreateNavigationStatementHandler`

- Registers: `context.NavigationDefinitions[stmt.Name] = stmt`

### 4.7 `SetReportMetadataStatementHandler`

- Sets report-level metadata on `context.ReportContext`, including `TITLE`, `DESCRIPTION`, custom CSS/JS/HTML fragments, favicon, logo, background, theme, and navigation reference.

### 4.8 Other report object handlers

- `CreateStyleStatementHandler` registers named style dictionaries.
- `CreateButtonStatementHandler` registers page-addressable buttons.
- `CreateTemplateStatementHandler` registers reusable option/style defaults.
- `CreateThemeStatementHandler` registers custom renderer-neutral theme tokens.
- `AlterReportObjectStatementHandler` and `DropReportObjectStatementHandler` mutate or remove existing report definitions.

### 4.8 `IReportContext` (on `IExecutionContext`)

```csharp
IDictionary<string, CreateVisualStatement>    VisualDefinitions     { get; }
IDictionary<string, CreatePageStatement>      PageDefinitions       { get; }
IDictionary<string, CreateDatasetStatement>   DatasetDefinitions    { get; }
IDictionary<string, CreateContainerStatement> ContainerDefinitions  { get; }
IDictionary<string, CreateNavigationStatement>NavigationDefinitions { get; }
IDictionary<string, CreateStyleStatement>     StyleDefinitions      { get; }
IDictionary<string, CreateButtonStatement>    ButtonDefinitions     { get; }
IDictionary<string, CreateTemplateStatement>  TemplateDefinitions   { get; }
IDictionary<string, CreateThemeStatement>     ThemeDefinitions      { get; }
string TemplatePath { get; set; }
string? ReportTitle       { get; set; }
bool ReportTitleIsMarkdown{ get; set; }
string? ReportDescription { get; set; }
IDictionary<string, string> BaselineParameters { get; }
string? ReportCss         { get; set; }
string? ReportJs          { get; set; }
string? ReportHtmlHead    { get; set; }
string? ReportHtmlBody    { get; set; }
string? ReportHtmlFooter  { get; set; }
string? ReportFavicon     { get; set; }
string? ReportLogo        { get; set; }
string? ReportBackground  { get; set; }
string? ReportTheme       { get; set; }
string? ReportNavigation  { get; set; }
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
        │       ├─ NamedVisualChartLowerer → ChartSpec + typed ChartData
        │       ├─ PlotPlanResolver → resolved renderer-neutral PlotPlan
        │       └─ SvgChartRenderer → native SVG (shared or focused layout)
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
IsInteraction — true when built from an interaction refresh
Title       — from SET REPORT TITLE
TitleIsMarkdown — markdown flag for the title
Description — from SET REPORT DESCRIPTION
Css / Js / HtmlHead / HtmlBody / HtmlFooter — custom report shell fragments
Favicon / Logo / Background / Theme / Styles / Navigation — report-level presentation metadata
Visuals     — List<VisualManifest>
Pages       — List<PageManifest>
Containers  — List<ContainerManifest>?  (null if none defined)
Navigations — List<NavigationManifest>? (null if none defined)
Buttons     — List<ButtonManifest>?     (null if none defined)
Datasets    — List<DatasetManifest>
Parameters  — Dictionary<string, string> active session parameter values
ParameterMetadata — Dictionary<string, ParameterMetadataManifest>
CustomThemes — List<ThemeManifest>? registered CREATE THEME definitions
Telemetry   — TelemetryManifest? execution and spill counters
Messages    — List<LogEntryManifest>? report build/runtime log messages
ExecutionTree — execution tree object when available
Error       — top-level build error when report creation fails
```

### 5.3 `VisualManifest`

```
Name        — visual identifier
VisualType  — string ("Bar", "Donut", "HeatMap", etc.)
ChartSpec    — versioned renderer-neutral semantic specification for charts
ChartData    — typed columns with raw and display values
PlotPlan     — resolved scales, series, marks, facets, style, and fallback semantics
NativeSvg    — sanitized server-generated SVG used by browser and static exports
ChartConfig  — obsolete compatibility slot; native manifests leave it null
TitleIsMarkdown / SubtitleIsMarkdown / IsMarkdown — markdown rendering flags
IsHidden    — true when VISIBLE = OFF deferred data fetch
DefaultValue / Min / Max / Decimals / Placeholder / LabelPosition — filter/input metadata
Tooltip     — optional TooltipManifest
Columns     — List<string> column names
Rows        — List<List<string?>> — all data as strings for portability
HighlightRows / RowStyles — cross-visual interaction and formatting output
Options     — Dictionary<string, string> (mapping:x, axis:x:label, FORMAT, etc.)
Styles      — Dictionary<string, string>? (THEME, HEIGHT, etc.)
Actions     — List<VisualActionManifest>
Interactions — Dictionary<string, string>? cross-visual behavior
SeriesDefs  — List<SeriesDefManifest>? (COMBO charts only)
FormattingRules — List<FormattingRuleManifest>?
Overlays    — List<OverlayManifest>?
SummaryData — TableSummaryData?
GridStyle   — TABLE grid display mode
DataLabels  — chart label settings
DrillState  — active DRILL_IN state
Error       — string? (non-null if source query failed)
```

All numeric data is serialized as strings to avoid JSON type loss.

### 5.4 `ApplyFormatting`

Before semantic lowering, `ApplyFormatting(vm)` applies the `FORMAT` option (a .NET numeric format string such as `N0`, `C2`, `P1`) to the value column of each row. This keeps display values consistent across tables, cards, native SVG, and accessible fallbacks.

---

## 6. Rendering

### 6.1 Native semantic chart pipeline

Standard charts are lowered by `NamedVisualChartLowerer` into a versioned `ChartSpec` and typed
`ChartDataSet`. `PlotPlanResolver` resolves scale domains, deterministic series order, null policy,
facets, mark geometry inputs, palette assignments, accessibility summaries, and semantic fallbacks.
`PlotPlanSvgRenderer` and `PlotPlanTerminalRenderer` consume that same plan, so browser, static/PDF,
and terminal surfaces do not independently reinterpret report semantics.

The shared PlotPlan path covers Cartesian, circular, statistical, polar, financial, timeline, and
faceted charts. `TREEMAP`, `SUNBURST`, `SANKEY`, `NETWORK`, `MAP`, and `MATRIX` use the approved
focused `SpecializedNativeSvgRenderer`; it performs deterministic managed-code layout and GeoJSON
projection without a client chart engine. `TABLE`, `CARD`, narrative, media, and filter controls keep
their purpose-built DOM/static renderers.

Every graphical `VisualManifest` carries `NativeSvg`. The browser imports this SVG into the DOM and
wires `data-row-index` marks to actions, cross-filtering, drill context, and native `<title>` tooltips.
No JavaScript option compiler or server-side script engine participates in rendering.

#### Option Key Naming Convention

`VisualManifest.Options` is a `Dictionary<string, string>` shared between `VisualBuilder` and the
native renderers/runtime. Keys follow a strict two-tier convention:

| Tier | Case | Written by | Examples |
|---|---|---|---|
| **Parser-supplied** | **UPPERCASE** | `VisualBuilder` — copied verbatim from the parsed option name | `STACKED`, `SMOOTH`, `FORMAT`, `LEGEND_POSITION`, `TITLE`, `SUBTITLE` |
| **Internally-computed** | **lowercase with colons** | `VisualBuilder` — synthesized during manifest build | `title`, `subtitle`, `mapping:x`, `mapping:value`, `axis:x:label`, `axis:y:min`, `color:Revenue` |

**Why two tiers?** Parser-supplied keys come directly from the source script (`OPTIONS(STACKED = ON)`), so they are stored as-is in uppercase to match the grammar. Internally-computed keys are synthesized by `VisualBuilder` from structured AST nodes (`AxisOptions`, `MappingHints`, `TypedSeries`) — they use lowercase-with-colon namespace notation to avoid clashing with parser keywords.

**Reading rules:**
- Native renderers must read parser-supplied keys in **UPPERCASE** (for example, `TITLE`).
- Native renderers must read internally computed keys in **lowercase** (for example, `axis:x:label`).
- Mixing cases silently misses the option because dictionary lookup is exact.

### 6.2 `SvgChartRenderer`

Server-side native SVG generation shared by manifests and `PdfExporter`. PlotPlan-backed charts use
`PlotPlanSvgRenderer`; approved specialized types use `SpecializedNativeSvgRenderer`.

- `Render(VisualManifest vm, int width, int height) → string`
- Returns SVG XML; the PDF exporter embeds it via QuestPDF's `SvgImage` element

### 6.3 PDF Export

`ReportPdfExporter` selects a PDF export mode:

| Mode | Status | Behavior |
|---|---|---|
| `STATIC` | Default | Uses `PdfExporter` with PDFsharp/MigraDoc; no browser is required. |
| `AUTO` | Selector mode | Uses a configured high-fidelity path when available, otherwise falls back to `STATIC` with a warning. |
| `HOSTED` | Planned | Uses Portal or `report serve` to render all report pages through the browser runtime. |
| `BROWSER` | Planned | Uses an installed Chrome, Edge, or Chromium executable; no browser is bundled. |

Explicit `HOSTED` and `BROWSER` modes fail clearly when unavailable. Only `AUTO` is allowed to fall back.

### 6.3.1 `PdfExporter`

Uses PDFsharp/MigraDoc to produce portable static PDF output.

```csharp
public byte[] Export(ReportManifest manifest) => ...
```

Layout:
- One static document containing visuals in report page order
- Chart types → SVG at 500×292 pt via `SvgChartRenderer`, embedded as `SvgImage`
- TABLE → native PDF table, capped for readability
- CARD → label + large-text value
- TEXT → `CONTENT`, `DefaultValue`, or mapped `CONTENT` column rendered as paragraphs
- SLICER / filter types → exported parameter selection summary

### 6.4 `MarkdownRenderer`

Produces a static, portable `.md` file:

- Pages become top-level `##` sections
- Chart visuals: native SVG plus a GFM data fallback
- Table visuals: GFM pipe table
- Card visuals: blockquote `> **Label** Value`
- Slicer / filter visuals: italic note *(interactive only — no static representation)*

### 6.5 Client-Side Runtime (`src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`)

Dual-mode JavaScript file:

| Mode | Data source | Activation |
|---|---|---|
| VS Code preview | `window.__MANIFEST__` injected by extension | `window.__MANIFEST__` present |
| Single-report web | `window.__MANIFEST__` pre-embedded in HTML | `window.__IS_WEB__ = true` |
| Multi-report web | `GET {apiBase}/manifest` | `window.__IS_WEB__ = true`, no pre-embedded manifest |

`window.__API_BASE__` is injected in multi-report mode as `/reports/{name}/api`. All API calls use `apiBase` as their prefix so the same script works for both single and multi-report deployments.

Chart visuals import the manifest's `nativeSvg` into the DOM and bind row-indexed SVG marks to report
actions and cross-filter state. Filter controls (`SLICER`, `MULTISELECT`, `DATEPICKER`, `SLIDER`,
`SEARCH`) call `POST {apiBase}/parameters` with a batch payload on change.

---

## 7. SnapshotStore

**File:** `ETL-SQL.Reporting/SnapshotStore.cs`
**Format:** indented JSON at `<script-basename>.snapshot.json`

| Method | Behavior |
|---|---|
| `SaveAsync(manifest, path)` | Serialize manifest to JSON; overwrites existing file |
| `LoadAsync(path)` | Deserialize JSON → `ReportManifest`; returns `null` if absent |
| `IsStale(manifest, scriptPath, ttl?)` | True if script file is newer than `BuiltAt`, or TTL elapsed |

`SnapshotStore` serializes to a unique temporary file and atomically moves it over the destination.
Per-path async reader/writer locks allow concurrent readers while excluding in-process writes.
Cross-process writers are last-writer-wins, but each committed snapshot is complete. Corrupt JSON is
treated as a missing snapshot so the host can rebuild it, and startup cleanup can remove abandoned
snapshot temporary files.

Dataset Parquet caches use a separate `DatasetFileTransaction`: writes go to a managed staging file,
the previous complete cache is backed up before replacement, and failures restore the backup. Portal
startup reconciliation removes abandoned dataset transaction/rotation files and catalog/filesystem
orphans inside `DatasetRootPath`.

---

## 7.1 Portal Dataset Security Model

Portal datasets have two encryption layers with different purposes:

| Layer | Purpose | Credential lifetime |
|---|---|---|
| Portal at rest | Protect the managed Parquet cache inside one portal | Long-lived portal secret, versioned and backed up with `portal.db` and `DatasetRootPath` |
| Export transport | Move one portable encrypted copy between portals | PASSWORD or KEYFILE supplied only to EXPORT/PUBLISH and never persisted |

`PUBLIC` is not anonymous access. A public dataset linked to a folder requires authenticated folder
Read or higher. `PRIVATE` requires report/dataset ownership, an explicit dataset grant, or administrator
rights. Dataset grants are hierarchical: Viewer, Refresh, Editor, Owner.

Interactive report execution and user-triggered refresh retain `UserId` and administrator role in the
dataset caller context. Only the local orchestrator poller explicitly requests trusted scheduled
dataset execution. Report-created datasets remain linked to their report; interactive publications are
owned by the caller; a userless trusted publication falls back to the destination folder owner.

The at-rest key is a recovery dependency, not just a runtime setting. Production startup fails for
missing, weak, invalid, or unresolved key-version configuration. Operators must back up the current and
previous key mappings together with the database and dataset directory. See the
[Portal Administrator Guide](../administration/portal/publishing.md#65-dataset-at-rest-key-lifecycle)
for provisioning, rotation, restore, and orphan-reconciliation procedures.

---

## 8. Parameter & Slicer System

### 8.1 Declaration

Parameters are declared at the top of the script with `DECLARE @x <TYPE> INPUT = <default>` and consumed by visuals/slicers; pages no longer take a trailing `WITH PARAMETERS` clause:

```sql
DECLARE @region    VARCHAR INPUT = 'All';
DECLARE @startDate DATE    INPUT = '2024-01-01';

CREATE PAGE Sales AS DASHBOARD (
    STRUCTURE = 'A A / B C',
    MAP ( 'A' = RevChart, 'B' = RegionSlicer, 'C' = DetailTable )
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
**Default port:** OS-assigned dynamic port (`0`) unless `--port` or `ReportPlayer:Port` is configured.

### 9.1 Single-Report Routes

| Route | Method | Behavior |
|---|---|---|
| `/` | GET | HTML page with pre-embedded manifest and `window.__IS_WEB__ = true` |
| `/api/manifest` | GET | Current `ReportManifest` as JSON |
| `/api/parameter` | POST | Set one parameter, selective rebuild, return new manifest |
| `/api/parameters` | POST | Set multiple parameters, selective rebuild, return new manifest |
| `/api/drill` | POST | Drill in/up for one visual |
| `/api/refresh-visuals` | POST | Selectively refresh named visuals |
| `/api/refresh` | GET | Force full rebuild, return new manifest |
| `/maps/custom` | GET | Return custom GeoJSON map inventory |

### 9.2 Multi-Report Routes

| Route | Method | Behavior |
|---|---|---|
| `/` | GET | Catalog page listing all reports from `reports.json` |
| `/reports/{name}` | GET | Dashboard HTML for the named report (injects `window.__API_BASE__`) |
| `/reports/{name}/api/manifest` | GET | Report manifest JSON |
| `/reports/{name}/api/parameter` | POST | Set one parameter |
| `/reports/{name}/api/parameters` | POST | Set multiple parameters |
| `/reports/{name}/api/run-script` | POST | Run a report action script |
| `/reports/{name}/api/drill` | POST | Drill in/up for one visual |
| `/reports/{name}/api/refresh-visuals` | POST | Selectively refresh named visuals |
| `/reports/{name}/api/refresh` | GET | Force rebuild |
| `/reports/{name}/maps/custom` | GET | Return custom GeoJSON map inventory for the report |

### 9.3 `DashboardServiceFactory` (multi-report)

`ETL-SQL.ReportHosting.DashboardServiceFactory` maintains a `ConcurrentDictionary<string, DashboardService>` keyed by report name. `GetService(name)` uses `GetOrAdd` for lazy, thread-safe service creation. Relative paths in `reports.json` are resolved against the manifest file's directory.

### 9.4 Startup

**Single-report:** `DashboardService` is registered as a singleton. On first request, `GetManifestAsync()` evaluates the script and caches the manifest. Subsequent requests return the cache until a parameter change or refresh invalidates it.

**Multi-report:** `DashboardServiceFactory` is registered as a singleton. Individual `DashboardService` instances are created on first access per report.

### 9.5 Static Assets

`wwwroot/` contains:

- `report-runtime.js` — client-side rendering runtime
- `report-runtime.css` — shared report styles
- `tabulator.min.js` / `tabulator.min.css` — table runtime assets copied from `ETL-SQL.ReportRuntime`
- `maps/*.geojson` — built-in geometry used by native map rendering

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

## 10a. Author Bookmarks & Resolved Report State

Author bookmarks (`CREATE BOOKMARK`) and Portal saved views share one versioned, serializable
envelope, `ResolvedReportState` (in `ETL-SQL.Core/Reporting`). The envelope carries typed parameter
values (`ReportStateValue` — a number stays a number, a boolean a boolean, null null; nothing is
flattened to a quoted string), the active page, and named-object `VISIBLE`/`COLLAPSED` maps, plus a
`schemaVersion` and, for saved views, the report script `ScriptHash` used to detect revision drift.

- **Author bookmarks** are parsed into `CreateBookmarkStatement` (typed `BookmarkParameterAssignment`
  expressions and strict `BookmarkStateEntry` records constrained to `VISIBLE`/`COLLAPSED` = `ON`/`OFF`),
  registered by `CreateBookmarkStatementHandler` (which rejects duplicate identifiers and more than one
  `DEFAULT = ON`), validated statically by `BookmarkValidationRule`, and emitted by `ManifestBuilder`
  into `BookmarkManifest.State` (a `ResolvedReportState`). `DROP BOOKMARK` removes a registration.
- **Portal saved views** persist the same envelope in `SavedReportView.StateJson` alongside `ScriptHash`,
  retaining legacy `ParametersJson`/`FiltersJson` for backward compatibility
  (`ResolvedReportState.FromLegacy`).
- **Application** is atomic: parameters are staged and committed as one request before the active page
  and presentation state are applied; a failed parameter request applies nothing (no partial bookmark).
- **URL/launch precedence:** URLs carry only an identifier (`#bookmark=Name` or `#view=Id`) — never
  parameter, filter, search, drill, or presentation values. Launch order is explicit URL bookmark →
  explicit saved view → user default saved view → author default bookmark → declared defaults.

See the [Author Bookmarks ADR](decisions/AuthorBookmarks.md) for the accepted contract.

## 11. Execution Phases Reference

| Phase | What was built |
|---|---|
| **9A** | `ReportAst.cs` — core AST nodes; `StatementParser.Report.cs` — CREATE VISUAL / PAGE / DATASET |
| **9B** | `ManifestBuilder`, original chart renderer, `MarkdownRenderer`, `SnapshotStore`, `ReportManifest` POCOs |
| **GoG Phase 8** | Renderer-neutral standard catalog, focused native layouts, shared native SVG browser/export path, and external chart-runtime retirement |
| **9C** | `report-runtime.js` — dual-mode client runtime for VS Code preview and web |
| **9D** | `ETL-SQL.ReportHosting.DashboardService`, `ETL-SQL.ReportPlayer` Kestrel server, parameter binding, live rebuild |
| **9E** | Filter visual types (DATEPICKER, SLIDER, MULTISELECT, SEARCH), batch parameter endpoint, responsive layout, page-level THEME |
| **9F** | Multi-report hosting (`DashboardServiceFactory`, `reports.json`, catalog page, per-report API prefix) |
| **9G** | PDF export (`SvgChartRenderer`, `PdfExporter` via QuestPDF), `--format pdf` CLI flag |
| **9H** | CREATE CONTAINER, CREATE NAVIGATION, SET REPORT TITLE/DESCRIPTION, COMBO visual type, STYLE clause, COLORS/LEGEND options |

---

## 12. Related Subsystem Architecture

For detailed information about adjacent subsystems, refer to the following architecture references:
- **Portal:** [Portal.md](Portal.md) documents the ASP.NET Core web host service exposing catalogs, dashboards, and access control.
- **Portal UI & Designer:** [PortalUI.md](PortalUI.md) describes the shared browser designer interface for parsing and generating Report-SQL scripts.
- **Orchestrator:** [Orchestrator.md](Orchestrator.md) covers background scheduling execution engines that run report ingestion pipelines.

---
