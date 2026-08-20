# ETL-SQL Product Roadmap

This document tracks high-level product tracks and candidate phases. Their actionable work is
decomposed in `TODO.md`. Once an initiative is verified, record its notable outcome in
`CHANGELOG.md` and retire completed TODO and roadmap entries that no longer describe future work.
Release-specific detail belongs in the release notes under `docs/releases/`.

The stable deployment-profile topology, provider, binding, state, and authority decisions are defined
in [`docs/architecture/DeploymentProfiles.md`](docs/architecture/DeploymentProfiles.md). The
Enterprise operating model and trust hierarchy are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

### SaaS Multi-Tenancy — Tenant Portability & Migration (Export/Import)

**Authoritative design:** [`TenantPortability.md`](docs/architecture/TenantPortability.md), which owns
the bundle, classification, rebinding, import, cutover, rollback, deletion, and customer-exit
contracts and the remaining delivery scope.

Customers must be able to enter or leave ETL-SQL SaaS without rewriting pipeline/report logic or
depending on provider-owned infrastructure. The guarantee is full-fidelity migration of portable
customer-owned artifacts and eligible tenant metadata, with explicit rebinding of environment-owned
identities, resources, secrets, keys, and infrastructure. It is deliberately not "zero-loss":
secrets, ephemeral security material, active sessions, leases, caches, and in-flight operations are
not transferable as durable tenant ownership, and saying so plainly is more defensible than a claim
the product cannot keep.

**Delivery stage.** Large evidence/content exports, incremental deltas, cross-provider scale
optimization, and Shared-source isolation mature without weakening the initial exit guarantee.
One unified bundle continues to extend the existing Portal configuration export and Orchestrator
promotion package; do not introduce a competing packaging model or represent the bundle as an opaque
database backup.

**Certification gate.** A representative tenant moves SaaS cluster A → SaaS cluster B and SaaS →
self-hosted Enterprise without changing business logic; export under concurrent activity produces a
declared consistency point and cutover creates no duplicate schedules; every eligible resource
reconciles by stable ID, count, hash, ownership, and ACL, with every exclusion visible and justified;
secret scanners prove no credential, key, capability, or other tenant's record enters the package;
tampered, replayed, cross-tenant, or oversized packages fail before target activation; and a customer
can validate and retain the export with published tooling and customer-held keys after source SaaS
access is gone.

### Platform — Native Object Storage for HA Artifacts

**Candidate, not scheduled.** Current High Availability (HA) deployments rely on shared network file systems (SMB/UNC) for Portal and Orchestrator artifact roots. As the SaaS offering scales, SMB becomes a significant latency bottleneck and a single point of failure (SPOF) due to file-locking contention.

**Delivery stage.** This work introduces native S3 / Azure Blob Storage provider bindings for the Artifact store. It replaces the reliance on durable POSIX file semantics in HA and SaaS environments, ensuring execution checkpoints, datasets, and script histories can scale horizontally without network filesystem lock contention.

### Identity — Service Accounts and M2M API Workflows

**Candidate, not scheduled.** While Enterprise OIDC identity is actively maturing for human users, automated deployments (CI/CD) and integration workflows currently lack hardened Machine-to-Machine (M2M) capabilities.

**Delivery stage.** This introduces formal Service Accounts, long-lived but tightly scoped API tokens, and approval workflows. It enables headless systems to securely publish `.rptsql` artifacts or trigger Orchestrator jobs without assuming a human identity or relying on legacy basic-auth patterns.

### Execution — Lean Worker Profiles and Binary Trimming

**Candidate, not scheduled.** The unified `ETL-SQL.exe` binary provides an excellent developer experience (DX) by bundling the Portal, Admin CLI, and Orchestrator into one drop-in executable. However, loading the entire DI graph and assemblies for these control-plane features adds unnecessary memory footprint and cold-start latency to ephemeral sandboxes.

**Delivery stage.** This work leverages .NET trimming and feature flags to produce a dedicated `ETL-SQL-Engine` binary artifact. It strips out all administrative, portal, and orchestration-hosting code, leaving only the pure script evaluator and connectors. This minimizes the compute cost and attack surface inside Shared SaaS OCI sandboxes.

### SaaS Testing — Chaos Engineering (Fault Injection)

**Candidate, not scheduled.** Shared SaaS High Availability relies on the Orchestrator to handle node and network failures gracefully, but this is difficult to prove without deliberately destructive testing.

**Delivery stage.** This phase integrates automated fault injection (e.g., Chaos Mesh) into the testing pipeline. It ensures the platform survives abrupt worker node reboots, dropped SMB packets, and database disconnects, proving that failed jobs correctly fence themselves and resume from the last valid named checkpoint.

### SaaS Testing — Synthetic Monitoring (Production Canaries)

**Candidate, not scheduled.** Internal staging environments cannot always replicate the specific friction points of production traffic and latency. 

**Delivery stage.** This initiative deploys a persistent "Canary Tenant" in the live production environment. The canary will execute a comprehensive synthetic end-to-end workflow at strict intervals. Any deviation in correctness or latency triggers proactive alerts to platform operations before real tenants experience degradation.

### Tooling & Authoring — Visual Report Builder Round-Trip Fidelity & Trivia Preservation

**In Progress.** The Visual Report Builder provides bi-directional synchronization between the 12-column visual grid and `.rptsql` scripts. To ensure zero loss of developer comments and formatting during visual editing, the authoring surface requires surgical AST serialization and span-patching controls.

**Remaining gaps and risk mitigations:**
1. **LSP Host Parity (`DesignerLspHandler.cs`):** The VS Code Language Server handler currently uses legacy full-script regeneration (`StateToScript`), which risks overwriting data prep SQL if triggered directly. It must be refactored to delegate to `DesignerScriptPatcher`.
2. **Multi-Page & CTE Regression Tests:** Expand test suites to cover scripts with multiple `CREATE PAGE` definitions (ensuring mutations on Page 2 do not shift Page 1 character spans) and complex CTE chains (`WITH cte1 AS (...), cte2 AS (...)`).
3. **Sequential Mutation & Line-Ending Drift:** Implement fuzz/regression tests simulating 50+ consecutive visual card moves and property updates to prove that byte-offset calculations do not accumulate off-by-one errors across Windows CRLF and Linux LF environments.
4. **Statement Body Trivia:** Harden parser preservation for comments written inside visual bodies (e.g. `-- comments inside MAPPINGS or OPTIONS blocks`).
5. **Fault-Tolerant Canvas State:** Implement automated UI/LSP assertions verifying that transient syntax errors display inline warning badges without resetting or corrupting the WYSIWYG canvas state.

### Reporting & Presentation — Grammar-of-Graphics Spec IR and Pluggable Chart Backends

**Candidate, not scheduled.** (Architectural Decision Record: [`docs/architecture/decisions/GrammarOfGraphicsSpecIR.md`](docs/architecture/decisions/GrammarOfGraphicsSpecIR.md)).

Visual definitions currently compile straight from the report AST into ECharts-shaped option objects in `ManifestBuilder`. The vendor's option schema has become the de facto internal contract: every downstream consumer — `report-runtime.js`, `EChartsSsrRenderer`, `PdfExporter`, `MarkdownRenderer`/`SvgChartRenderer`, and `TerminalRenderer` — re-derives meaning from it.

The insight driving this track: **the grammar is the differentiator and the renderer is commodity.** A chart specification that is a first-class part of the query language gains lineage tracking, Analysis-tier lint, LSP completion, and reviewable diffs. Pixel emission is a solved problem that should be compiled, not coupled.

**Design Principles and Boundaries:**
- **Spec first, renderer second:** a neutral grammar-of-graphics IR (`ChartSpec`) representing data bindings, mark layers, scale mappings, coordinate projections, and faceting becomes the canonical contract in `ETL-SQL.Core.Reporting.Spec`.
- **Type keywords are sugar:** existing `BAR`, `LINE`, `PIE`, `DONUT`, `WATERFALL`, `CANDLESTICK`, etc., remain the zero-friction "easy button" for authors and lower automatically into `ChartSpec`.
- **Transparent data prep in SQL:** heavy transformations, cumulative sums, moving averages, and statistical aggregations live in SQL `#temp` tables where they are visible, testable, and lineage-tracked; `ChartSpec` focuses strictly on visual layout and mark encodings.
- **Data delivery tiering:** lightweight **JSON Columnar vectors** for charts enable instant `JSON.parse` with 0 KB library overhead and clean Git diffs; **Apache Arrow IPC** streams are retained for high-density tables (>1,000 rows).
- **The 8 atomic mark primitives:** all 2D visuals compile into compositions of `RECT`, `LINE`, `AREA`, `POINT`, `RULE`, `ARC`, `TEXT`, and `PATH`.
- **First-class orthogonal faceting (`FACET (...)`):** any standard visual (`BAR`, `LINE`, `SCATTER`, `COMBO`, etc.) or `CUSTOM` visual can specify a 1D wrap `FACET (BY = Region, COLUMNS = 3, SCALES = SHARED)` or 2D matrix `FACET (ROW = ..., COLUMN = ..., SCALES = ...)` clause, generating coordinated small-multiples with synchronized axes and shared or independent scale domains.
- **Multi-layer authoring via `CUSTOM` with `SCALES (...)`:** complex composite graphics are authored using `CREATE VISUAL <name> AS CUSTOM (...)` with dedicated spatial ($X, Y, Y_2$) and aesthetic scale blocks.
- **Native C# static export:** server-side PDF and static report exports compile directly from `ChartSpec` into SVG XML via pure C# geometry and SkiaSharp text metrics, completely retiring `ClearScript.V8` and headless browser dependencies.
- **Vega-Lite semantic alignment & interchange:** embedded Vega-Lite v5 JSON specifications are supported via `CREATE VISUAL ... AS VEGA_LITE (SPEC = '...')`.
- **Phased ECharts retirement via D3 micro-modules:** Tier 2 standard charts migrate to our native vector SVG engine, and Tier 3 complex visuals (Maps, Sankey, Treemap, Network) migrate to lightweight D3 micro-packages (`d3-geo`, `d3-hierarchy`, `d3-sankey`, `d3-force`), achieving a **~35 KB total standalone runtime footprint**.

**Delivery Stages:**
- **Stage 1: Neutral Spec IR and ECharts Lowering Compiler:** Implement `ChartSpec` record hierarchy (including `FacetSpec`); build `SpecDesugarer` for all 17 Tier 2 types and `TRELLIS`/`MATRIX` sugar lowering; build `SpecToEChartsCompiler`. JSON columnar vectors serialize in `VisualManifest`.
- **Stage 2: Native C# SVG Static Export Backend:** Implement pure C# scale math (Linear, Band, Time, Log) and SkiaSharp text metrics in `SvgChartCompiler.cs`. Replaces `EChartsSsrRenderer.cs` and powers headless PDF and email exports without V8/Node.js.
- **Stage 3: `CUSTOM` Multi-Layer Syntax, `FACET` Operator & Vega-Lite Translator:** Add parser and AST support for `CREATE VISUAL ... AS CUSTOM (...)` with `SCALES (...)` block, orthogonal `FACET (...)` clause on any visual, and `CREATE VISUAL ... AS VEGA_LITE (SPEC = '...')`. *(Note: This stage establishes the `ComponentTooltipSpec` hook for the [Visual and Container Tooltips](#reporting--presentation--visual-and-container-tooltips-viz-in-tooltip) roadmap track).*
- **Stage 4: Native Vector SVG Micro-Renderer:** Implement lightweight browser SVG/DOM renderer for Tier 2 Cartesian & Circular charts (`BAR`, `LINE`, `SCATTER`, `PIE`, `COMBO`, `WATERFALL`, faceted panels). Conditionally omit ECharts for reports containing only Tier 1 & Tier 2 visuals.
- **Stage 5: Complete ECharts Retirement via D3:** Replace remaining Tier 3 complex visuals (`MAP` via `d3-geo`, `TREEMAP`/`SUNBURST` via `d3-hierarchy`, `SANKEY` via `d3-sankey`, `NETWORK` via `d3-force`) with specialized D3 micro-packages. Completely retire `echarts.min.js` from the repository, achieving a ~35 KB total standalone runtime footprint.
- **Stage 6: Advanced Composite Samples & Cookbook:** Create production-grade sample scripts for high-impact composite visuals (Dumbbell variance plots, Bullet graphs with qualitative zones, Ridgeline distribution plots, and Faceted small-multiples grids). Author comprehensive guides: *"How to Build a CUSTOM Chart"*, *"Enhancing Standard Charts with Overlay Layers & Faceting"*, and add 10+ recipes to the Reporting Cookbook.

---

### Reporting & Presentation — Visual & Container Tooltips (Viz-in-Tooltip)

**Candidate, not scheduled.** Modern analytical reporting frequently demands on-hover micro-visual drill-ins (e.g. hovering over a region bar or category slice reveals a filtered sparkline, subcategory breakdown bar chart, or a multi-metric KPI summary card).

**Architecture Decision (Presentation Surface Contract):**
Tooltips exist as first-class presentation targets rather than plain text strings:
- **Simple Text (Zero-Ceremony Sugar):** `TOOLTIP (TITLE = ..., CONTENT = ...)` remains concise for standard formatted expressions.
- **Visual & Container Tooltips:** `TOOLTIP (VISUAL = VisualName, PARAMETERS (...))` or `TOOLTIP (CONTAINER = ContainerName, PARAMETERS (...))` attaches a micro-visual or container to data points.
- **Instant Client-Side Vector Slicing:** Hovered row values bind directly to child visual parameters in memory over pre-staged columnar vectors, rendering rich micro-visuals in $< 1\text{ ms}$ with zero server round-trip latency.
- **Viewport Boundary Awareness:** Floating popover anchors to the hovered mark geometry with automatic flip/shift positioning to prevent viewport clipping.

**Target Syntax Specification:**
```sql
-- 1. Mini-chart definition (rendered only within tooltip popovers)
CREATE VISUAL SubcategoryBreakdown AS BAR (
  SOURCE   = #subcategory_sales,
  MAPPINGS (X = SubCategory, Y = Revenue),
  OPTIONS  (STYLE = COMPACT, HEIGHT = 160)
);

-- 2. Parent visual binds hover event to the mini-chart with parameter context
CREATE VISUAL CategorySales AS PIE (
  SOURCE   = #sales,
  MAPPINGS (CATEGORY = CategoryName, VALUE = TotalRevenue),
  TOOLTIP  (
    VISUAL     = SubcategoryBreakdown,
    PARAMETERS (@selectedCategory = CategoryName)
  )
);
```

**Delivery Stages:**
1. **Spec IR & AST Extension:** Extend `TooltipSpec` in `ETL-SQL.Core.Reporting.Spec` to support `ComponentTooltipSpec(TargetVisualOrContainer, ParameterBindings)`.
2. **Runtime Popover Host (`report-runtime.js`):** Implement floating tooltip portal mounting target visual/container with sub-pixel bounding-box collision detection.
3. **Local Vector Cross-Filtering:** Propagate hovered row parameters to child visual dataset vectors instantaneously in browser memory.

---

### Reporting & Presentation — Data-Bound HTML/SVG Templates & Micro-Charts

**Candidate, not scheduled.** (Architectural Decision Record: [`docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md`](docs/architecture/decisions/MicroChartsAndHtmlEmbedding.md)).

Modern analytical reporting frequently demands specialized escape hatches for bespoke infographic KPI cards, micro-visuals, status badges, and repeater cards, alongside in-cell table sparklines and card background charts, without sacrificing the script-first, Zero-Trust execution model.

**Design Principles and Boundaries:**
- **Zero-Trust Rendering:** Custom visual extensions remain purely declarative and safe across CLI Player, VS Code, and Portal runtimes. Untrusted JavaScript execution is prohibited; all HTML/SVG templates pass through an AST-based sanitizer (stripping `<script>`, `<iframe>`, inline `on*` handlers, and `javascript:` URIs).
- **Three-Tier Composability:** Zero-ceremony sugar for native widgets (`CARD` background sparklines, `TABLE` cell sparklines/progress bars), plus declarative Mustache macro helpers (`{{SPARKLINE(...)}}`, `{{PROGRESS_BAR(...)}}`, `{{BG_CHART(...)}}`) for freeform HTML.
- **GoG IR Headless SVG Generation:** All micro-visuals compile directly to lightweight `<svg viewBox="...">` markup on the server via `ChartSpec` presets, ensuring instant rendering and 100% parity across browser DOM, headless PDF exports, and HTML email digests.

**Target Syntax Specification:**
```sql
-- 1. KPI Card with subtle background trend chart (Zero-Ceremony Sugar)
CREATE VISUAL TotalRevenueCard AS CARD (
  SOURCE   = #current_kpi,
  MAPPINGS (
    VALUE      = TotalRevenue FORMAT '$#,##0',
    TARGET     = TargetRevenue FORMAT '$#,##0',
    COMPARISON = DeltaPercent FORMAT '+0.0%',
    SPARKLINE  = #daily_revenue (X = SaleDate, Y = Amount, TYPE = AREA)
  ),
  OPTIONS (
    SPARKLINE_POSITION = BACKGROUND,  -- BACKGROUND | BOTTOM | TOP | INLINE
    SPARKLINE_COLOR    = '#3b82f6',
    SPARKLINE_OPACITY  = 0.12
  )
);

-- 2. Data-Bound HTML Template with Embedded Micro-Chart Macros
CREATE VISUAL NodeClusterStatus AS HTML (
  SOURCE   = #cluster_nodes,
  MODE     = REPEATER,                 -- SINGLE_ROW (default) | REPEATER
  TEMPLATE = '
    <div class="node-card {{SeverityClass}}">
      <div class="node-header">
        <span class="node-name">{{HostName}}</span>
        <span class="node-badge {{Status}}">{{Status}}</span>
      </div>
      <div class="node-metrics">
        <div class="metric-row">
          <label>CPU Utilization</label>
          <span class="metric-val">{{CpuPercent}}%</span>
          <div class="spark-box">
            {{SPARKLINE(CpuHistory, TYPE="LINE", COLOR="#3b82f6", HEIGHT=20, WIDTH=70)}}
          </div>
        </div>
        <div class="metric-row">
          <label>Memory Pressure</label>
          <div class="prog-box">
            {{PROGRESS_BAR(MemoryPercent, MAX=100, COLOR="#f59e0b", HEIGHT=6)}}
          </div>
        </div>
      </div>
      <div class="node-bg-chart">
        {{BG_CHART(NetworkThroughputHistory, TYPE="AREA", COLOR="#64748b", OPACITY=0.10)}}
      </div>
      <div class="node-footer">
        <button class="action-btn" data-action="SET_PARAMETER" data-param="@selected_node" data-value="{{HostName}}">
          Inspect Diagnostics →
        </button>
      </div>
    </div>
  ',
  STYLE (
    CSS = '
      .node-card { position: relative; overflow: hidden; padding: 16px; border-radius: 8px; border: 1px solid #e2e8f0; }
      .node-card.critical { border-color: #ef4444; }
      .node-bg-chart { position: absolute; bottom: 0; left: 0; right: 0; height: 60%; pointer-events: none; z-index: 0; }
      .node-header, .node-metrics, .node-footer { position: relative; z-index: 1; }
      .metric-row { display: flex; align-items: center; justify-content: space-between; margin-top: 8px; font-size: 13px; }
      .spark-box { width: 70px; height: 20px; }
      .prog-box { width: 100px; height: 6px; }
    '
  )
);
```

**Delivery Stages:**
- **Stage 1 (Headless SVG Micro-Compiler):** Implement `SvgChartCompiler.GenerateSparklineSvg(...)` and progress bar generator in `ETL-SQL.Core`.
- **Stage 2 (`CARD` & `TABLE` Sugar Integration):** Add `SPARKLINE` mapping and `SPARKLINE_POSITION` option to `ReportParser.cs` for `CARD`; expand `TABLE` sparklines to support array columns and child sources.
- **Stage 3 (Template Macro Engine):** Implement Mustache macro parser in `ManifestBuilder.cs` and `report-runtime.js` supporting `{{SPARKLINE}}`, `{{PROGRESS_BAR}}`, `{{BG_CHART}}`, and `{{VISUAL}}`.
- **Stage 4 (PDF & Email Parity):** Verify pure SVG injection in `PdfExporter` and HTML email templates without external runtime dependencies.

---

### Reporting & Presentation — Full-Fidelity Terminal UI (TUI) Dashboard Parity & Semantic Fallbacks

**Candidate, not scheduled.** While web dashboards are standard for business analysts, terminal-native analytical reporting is having a resurgence across DevOps, SRE, and data engineering. Viewing rich dashboards over SSH, in air-gapped bastions, inside Docker containers, or in local Terminal IDEs without port-forwarding or launching a browser is a massive operational capability.

**Design Principles and Boundaries:**
- **GoG IR Mark-to-Terminal Lowering:** `TerminalRenderer` lowers neutral `ChartSpec` marks and scales directly to terminal primitives:
  - `RECT` / `BAR`: Sub-character fractional vertical resolution via Unicode eighth-blocks (` ▂▃▄▅▆▇█`).
  - `LINE` / `AREA`: High-density `BrailleCanvas` (`⠁⠂⠃⠄⠅⠆⠇⠈⠉⠊⠋⠤⠶⠛`) providing 4× resolution over standard character grids.
  - `POINT` / `SCATTER`: Color-coded ASCII/Unicode glyphs (`●`, `▲`, `◆`, `■`, `*`).
  - `RULE`: Box-drawing reference axes and target threshold labels (`─────── Target ($100k)`).
  - `ARC` (Pie / Donut / Gauge): Spectre `BreakdownChart` and segmented proportional gauges.
  - `FACET` (Trellis / Matrix): Multi-panel grid layout with coordinated axis scales.
- **High-Fidelity Form Controls:** Elevate input controls from generic tag lists into dedicated, beautiful Spectre/Unicode widgets:
  - `DATEPICKER` / `RELDATEPICKER`: Interactive calendar month grid (`Su Mo Tu We Th Fr Sa`) with highlighted selection.
  - `SLIDER`: Visual ASCII track with current/min/max readouts (`[────●──────────] $450k`).
  - `MULTISELECT` & `SLICER`: Interactive checkbox/radio pill buttons (`(•) All  ( ) West` / `[X] Enterprise  [ ] SMB`).
  - `SEARCH` & `TEXTBOX`: Form input frames with placeholders (`[ 🔍 Search SKU... ]`).
  - `BUTTON`: 3D/rounded push buttons (`╭──────────╮ \n │ Run ETL  │ \n ╰──────────╯`).
- **Intelligent Semantic Fallback Engine:** 28 of 36 visuals have full high-fidelity TUI representations. For the remaining 8 complex/spatial visuals that lack a direct terminal equivalent, the renderer provides structured, informative fallbacks rather than broken outputs:
  - `MAP` $\to$ Region/Country Top-N Bar Breakdown Table.
  - `SANKEY` $\to$ Stage Transition Flow Table with volume and drop-off deltas.
  - `TREEMAP` / `SUNBURST` $\to$ Indented Hierarchical Tree Table with proportional bars.
  - `NETWORK` $\to$ Node Degree & Edge Connection Table.
  - `IMAGE` $\to$ Filename/URL Asset Badge (`[🖼️ logo.png]`).

**Delivery Stages:**
- **Stage 1 (GoG IR Terminal Compiler):** Implement `TerminalChartCompiler` lowering `ChartSpec` mark records into Braille canvas, block elements, and Spectre renderables in `ETL-SQL.Reporting`.
- **Stage 2 (Form Controls Overhaul):** Build high-fidelity Spectre widgets for `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`, `CHECKBOX`, and `TEXTBOX`.
- **Stage 3 (Semantic Fallback Engine):** Implement automated fallback tables for `MAP`, `SANKEY`, `TREEMAP`, `SUNBURST`, `NETWORK`, and `IMAGE`.
- **Stage 4 (Keyboard Navigation & Live Filter Propagation):** Wire interactive keyboard focus, arrow-key selection, and parameter dispatch inside `ETL-SQL.TUI` and `ReportPreviewPanel.cs`.

---

### Reporting & Presentation — Cascading Parameter Defaults (Dependent Slicers)

**Candidate, not scheduled.** In interactive dashboards and parameterized paginated reports, filters often exhibit natural parent-child relationships (e.g. selecting `Country = 'US'` should constrain the `State` dropdown to US states, and picking a `Department` should restrict the `Manager` slicer). Currently, slicers evaluate their source datasets independently at initial report evaluation or require manually written dependent queries.

**Architecture Decision (Visual-Level Contract):**
Dependency rules belong on the **visual control** (`CREATE VISUAL ... AS SLICER / MULTISELECT`) rather than on `DECLARE @var` or `CREATE PAGE`. This preserves the clean separation between core engine scalar variable storage and presentation-tier dataset projection, while allowing offline `.etlsnap` runtimes to filter option sets client-side from Arrow tables without server round-trips.

**Target Syntax Specification:**
```sql
CREATE VISUAL StateSlicer AS SLICER (
  SOURCE     = #geo_hierarchy,
  MAPPINGS   (VALUE = StateName, FILTER_BY = CountryName),
  DEPENDS_ON = @country,
  ON_INVALID = RESET_DEFAULT,   -- RESET_DEFAULT | SELECT_FIRST | CLEAR
  DEFAULT    = 'All',
  ACTIONS    (ON_CHANGE = SET_PARAMETER(@state, StateName))
);
```

**Delivery Stages:**
1. **Parser & AST Extension:** Add `DEPENDS_ON = @param` and `ON_INVALID = RESET_DEFAULT | SELECT_FIRST | CLEAR` to `VisualDefinition` and `ReportParser.cs`, plus `FILTER_BY` support in `MAPPINGS`.
2. **Client-Side Dependency Propagation (`report-runtime.js`):** When `@country` changes, the browser runtime automatically filters the child visual's Arrow dataset by `CountryName == @country`. If the current value of `@state` is not in the filtered options, it evaluates the `ON_INVALID` action immediately with zero server latency.
3. **Server-Side Re-evaluation (`ReportInteractionRefresher.cs`):** In live query mode, dependent visual queries are refreshed in topological order based on the parameter dependency graph.
4. **Cycle Detection & Static Linting:** The LSP and AST compiler validate the parameter dependency DAG at authoring time to guarantee termination and prohibit circular parameter dependencies.

### Reporting & Presentation — Bookmarks & In-Report Saved Visual States

**Candidate, not scheduled.** While Portal administrators can save parameterized views at the catalog level via `CREATE SAVED VIEW`, report consumers and authors frequently need in-report bookmark presets to switch between curated analytical views, toggled container visibility states, and specific filter configurations during executive presentations or self-service exploration.

**Architecture Decision (Layered Composition):**
Rather than building a redundant parameter-snapshot engine, Bookmarks layer cleanly on top of ETL-SQL's existing `CREATE SETS !SetName` primitive:
- **Layer 1 (Data / Parameters):** `CREATE SETS !Name` encapsulates the raw parameter values in engine memory.
- **Layer 2 (Presentation Entity):** `CREATE BOOKMARK Name AS (...)` binds parameter sets to presentation metadata (`TITLE`, `DESCRIPTION`, `ICON`), target `PAGE`, and container visibility (`UI_STATE`).
- **Layer 3 (Triggers & Shell Integration):** Bookmarks automatically populate a dedicated "Bookmarks" dropdown menu in the Report Player / Portal navigation header without wasting 12-column grid canvas space, while in-canvas buttons can invoke `ACTIONS (ON_CLICK = APPLY_BOOKMARK(Name))` or compose multiple actions directly (`ON_CLICK = (Action1, Action2)`).

**Target Syntax Specification:**
```sql
-- 1. Parameter State (Engine Tier)
CREATE SETS !WestCoastRetail BEGIN
  @region    = 'West',
  @channel   = 'Retail',
  @timeframe = 'M-1'
END;

-- 2. Bookmark Presentation Object (Report Tier)
CREATE BOOKMARK WestCoastDeepDive AS (
  TITLE       = 'West Coast Retail Analysis',
  DESCRIPTION = 'Pre-filters for Q1 retail stores with anomaly drawer open.',
  SET         = !WestCoastRetail,
  PAGE        = StoreDetails,
  UI_STATE    = ('AnomalyDrawer' = VISIBLE, 'SummaryCard' = COLLAPSED)
);

-- 3. Optional In-Canvas Button Binding
CREATE BUTTON QuickSwitch AS (
  LABEL   = 'Switch to West Coast',
  ACTIONS (ON_CLICK = APPLY_BOOKMARK(WestCoastDeepDive))
);
```

**Delivery Stages:**
1. **Parser & AST Extension:** Implement `CreateBookmarkStatement`, `APPLY_BOOKMARK` action AST node, and multi-action composition tuples `(Action1, Action2, ...)` in `ReportParser.cs`.
2. **Runtime Execution Engine (`report-runtime.js`):** Implement `APPLY_BOOKMARK` to atomically apply parameter sets, transition active pages, update container/visual visibility, and synchronize with slicer controls.
3. **Portal & Shell Integration:** Automatically render a global "Bookmarks" menu in the Report Player / Portal header bar populated from the `ReportManifest.Bookmarks` catalog.
4. **URL Hash Synchronization:** Synchronize active bookmarks with browser URL hash fragments (`#bookmark=WestCoastDeepDive`) to enable one-click deep linking into specific report states.

---

### Governance & Infrastructure — Data Gateway: Interactive Viewer Identity & Row-Level Security Delegation

**Candidate, not scheduled.** When interactive report dashboards query on-premises data sources via the Data Gateway, queries currently execute under static database or service account credentials configured for the resource. In regulated enterprise environments, target databases (such as SQL Server, PostgreSQL, Snowflake, and Oracle) enforce Row-Level Security (RLS) policies based on the specific end-user viewing the report.

**Architecture Decision (Cross-Platform Context Propagation):**
Rather than relying solely on Windows-only Active Directory Kerberos Constrained Delegation (KCD), the Gateway protocol will support cross-platform **Caller Identity Context Injection**:
- **Context Envelope:** The Portal passes the authenticated viewer's verified identity (`Email`, `PrincipalId`, `Roles`, `Groups`) inside `GatewayOperationBounds.CallerContext`.
- **Database Session Setting:** Before executing queries on the target engine, the Gateway executes the appropriate database session context command:
  - *SQL Server:* `EXEC sp_set_session_context @key=N'UserId', @value=@viewerEmail;`
  - *PostgreSQL:* `SET LOCAL app.current_user = 'alice@corp.com';` (or `SET LOCAL ROLE ...`)
  - *Snowflake / Oracle:* Session tag injection / `DBMS_SESSION.SET_CONTEXT`.
- **Zero-Trust Audit:** All gateway outcome ledger entries and audit outbox records log both the executing service identity and the authenticated caller identity for end-to-end audit compliance.

**Delivery Stages:**
1. **Protocol Extension:** Add `CallerContext` (principal key, identity, roles, directory groups) to `GatewayOperation` and `GatewayFrame`.
2. **Session Context Injection:** Implement connector-specific session context setters in `IGatewayResourceExecutor` for MSSQL, PostgreSQL, Oracle, and Snowflake.
3. **Portal DirectQuery Binding:** Pass ambient session identity from Portal interactive report controllers through to the Gateway execution dispatcher.
4. **Optional Kerberos Constrained Delegation (Windows Overlay):** Add Windows-specific `WindowsIdentity.RunImpersonated` support for on-prem Active Directory Windows Integrated Authentication where Kerberos SPNs are configured.
