# Report-SQL Post-Launch Strategy

> [!IMPORTANT]
> **Historical roadmap/backlog.** Report-SQL has since shipped a large portion of this plan. Do not treat backlog tables in this file as current product truth without checking `docs/guides/report-sql.md`, `docs/cookbooks/report/`, `docs/syntax-index.md`, and the reporting source.

**Status:** Historical roadmap/backlog — reconcile before using for implementation
**Date:** 2026-04-14  
**Scope:** All post-Phase-9 enhancements to the Report-SQL subsystem (`.rptsql`, `ReportBuilder`, `ReportPlayer`, `ReportBuilder.CLI`)

This document covers the Phase 9 post-launch backlog: an honest assessment of each item, what is missing, and a phased delivery plan.

---

## 1. Assessment of the Backlog

---

### 1.1 VISUAL Syntax Variations

**Decision: Two canonical forms only.**

The original proposal introduced three interchangeable forms (`PARAM (value)`, `PARAM = 'literal'`, `PARAM = (value)`). Three forms for every clause creates a parser maintenance burden and prevents users from developing muscle memory.

Adopted forms:
- `PARAM (value)` — block form, for complex expressions, multiline content, and all structured clauses (MAPPING, OPTIONS, ACTIONS, STYLE)
- `PARAM = 'literal'` — scalar shorthand, for string and number literals only (TITLE, SUBTITLE, ORIENTATION)

`PARAM = (value)` is dropped. It adds nothing over `PARAM (value)` except an extra character and an ambiguous parse path. The linter will warn if the mixed form appears in scripts.

**SOURCE = #table** is a genuine new capability — bypassing the inline query when a temp table is already shaped correctly — and is added without modification.

---

### 1.2 STYLE Variable Type

**The idea:** `DECLARE @style STYLE = (...)` creates a reusable style bag. All layout objects accept `STYLE = @style` or inline `STYLE (...)`.

**What is good:** Shared styles are the right abstraction. Without them, every VISUAL in a large report carries duplicated `HEIGHT`, `BACKGROUND`, `BORDER` values that are painful to change globally. This is the dashboard equivalent of CSS classes.

**What needs to be resolved before implementation:** When a VISUAL has both `STYLE = @shared` and an inline property, which wins? Decision: **inline properties override the shared variable**, same as CSS inline styles. This cascade rule must be specified in the parser and documented before any implementation begins.

**THEME property:** The STYLE type gains a `THEME` property that names an ECharts built-in theme as the base. Additional STYLE properties are applied as overrides on top of that theme. This gives authors a professionally designed starting point without building every color and font from scratch.

```sql
-- Use a built-in theme as the base, override specific properties
DECLARE @style STYLE = (
    THEME = 'vintage'
    ,BACKGROUND = '#fafafa'
    ,BORDER = '1px solid #ddd'
);

-- Or inline on any object
CREATE VISUAL MyChart AS BAR (
    SOURCE = #data
    ,MAPPINGS (X = category, Y = value)
    ,STYLE (
        THEME = 'dark'
        ,HEIGHT = '400px'
    )
);
```

Bundled themes (stored in `wwwroot/themes/`): `light`, `dark`, `vintage`, `westeros`, `essos`, `wonderland`, `walden`, `chalk`, `infographic`, `macarons`. These are the standard ECharts community themes. If no THEME is specified the ECharts default (`light`) is used.

**Cascade order (lowest to highest priority):**
1. ECharts built-in theme (set by `THEME = '...'`)
2. Shared `@style` variable properties
3. Inline `STYLE (...)` properties on the object

**Implementation order:**
1. Inline `STYLE (...)` block scoped to the object — no variable, no type system changes, unblocks all layout work.
2. `DECLARE @style STYLE` variable type with dot-notation assignment (`@style.HEIGHT = '100px'`) — deferred to Phase 9.3, as it requires changes to `VariableScopeManager` and `ExpressionEvaluator`.

---

### 1.3 CONTAINER Object

A named layout object that holds VISUALs in a grid, nestable inside a PAGE. This unlocks complex layouts the flat `CREATE PAGE` grid cannot express — a KPI banner row alongside a detail area, a sidebar of cards next to a main chart.

**Types:** `BOX` (rectangular) for Phase 9.3. `SCROLL` (fixed-height scrollable column, useful for long tables alongside fixed charts) as a second type when the need arises. No other shapes.

---

### 1.4 Page Structure

`STRUCTURE` uses a CSS grid-template-areas string such as `'A A / B C'`. `CreatePageStatement.Structure` stores that string directly, and `report-runtime.js` emits matching `grid-template-areas` CSS for page layout.

---

### 1.5 Multi-Page Dashboard with NAVIGATION

Multiple `CREATE PAGE` statements, then a `CREATE NAVIGATION` that maps page identifiers to nav slots. This is table stakes for any production dashboard.

**Navigation types:**

| Type | Description |
|------|-------------|
| `TAB` | Standard tab bar with active state indicator |
| `BUTTON` | Row of button controls that switch the active page |
| `LINK` | Horizontal bar of plain text page names separated by a divider: `Page1 \| Page2 \| Page3`. Each acts as a clickable hyperlink that switches the active page. Minimal styling, reads like an inline nav strip. |

**Orientations:** `HORIZONTAL` and `VERTICAL` only.

**Required addition:** A `DEFAULT = <page>` option specifying which page is active on load.

**Example:**

```sql
CREATE NAVIGATION MainNav AS LINK (
    STRUCTURE ('A B C')
    ,MAP (
        'A' = Overview,
        'B' = Detail,
        'C' = Summary
    )
    ,ORIENTATION = HORIZONTAL
    ,DEFAULT = Overview
);
```

---

### 1.6 VISUAL TEXT

Adding a text/markdown visual type is correct and simple. TEXT visuals are the standard way to add section headers, explanatory copy, and KPI annotations in dashboards.

Add `ALIGN = left|center|right` at the visual level (not requiring a full STYLE block) so authors can center a header without declaring a style variable.

---

### 1.7 CREATE DATASET Encryption

**Three modes:** machine-bound (default), password-protected (portable), key-file (portable).

Machine-bound as the default is the right choice — the current key-file-only model forces every user to manage a key file even when portability is not needed. Machine-bound uses DPAPI on Windows and the equivalent on Linux/Mac. Machine-bound datasets cannot move between OS types; the linter should warn when `ENCRYPT = MACHINE` is used on a dataset referenced by a network-served report.

---

### 1.8 Hosting Multiple Reports

`ReportPlayer` currently serves a single `.rptsql` file. Multi-report hosting requires:
- A catalog endpoint (`GET /`) listing available reports
- Each report served at `GET /reports/<name>`
- `DashboardService` becomes a factory — one instance per report, lazily constructed on first request

**Decision: `reports.json` manifest.** A single directory flag cannot cover reports spread across multiple folders, and a glob pattern on the CLI becomes unwieldy as the number of reports grows. A manifest file is explicit, version-controllable, and easy to validate at startup.

```json
{
  "reports": [
    { "name": "Sales",     "path": "reports/sales/sales.rptsql" },
    { "name": "Inventory", "path": "reports/ops/inventory.rptsql" },
    { "name": "HR",        "path": "/shared/hr/headcount.rptsql" }
  ]
}
```

The server is invoked as `etl-sql-report serve --manifest reports.json`. Paths in the manifest may be relative (to the manifest file) or absolute.

---

### 1.9 Format Values

`FORMAT()` on card values, axis labels, and table cells. Uses standard .NET format specifiers (`{0:N0}`, `{0:C2}`, `{0:P1}`). Applied server-side in `ManifestBuilder` before serialization — the client runtime already receives all data as strings (`Reporting.md §5.3`). High-value, low-cost.

---

### 1.10 Rendering Library Migration — Chart.js → Apache ECharts

**Decision: Switch to Apache ECharts before Phase 9.2 chart work begins.**

ECharts is a better long-term foundation for this project. The three chart types originally deferred to a library-evaluation phase (box plot, treemap, heat map) are all built into ECharts with no plugins required. ECharts also has a first-class theme system, built-in dark mode, and — critically — native SVG rendering that eliminates the headless browser requirement for image and PDF export.

**What changes:**

| Component | Current | After migration |
|-----------|---------|-----------------|
| `ChartJsRenderer.cs` | Produces Chart.js JSON config | Replaced by `EChartsRenderer.cs` producing ECharts option JSON |
| `report-runtime.js` | `new Chart(canvas, config)` | `echarts.init(div, config)` |
| `wwwroot/chart.js` | Chart.js bundle | `echarts.min.js` |
| `VisualManifest.ChartConfig` | Chart.js JSON string | ECharts option JSON string (same field, different schema) |
| Markdown image export | Deferred — required headless browser | SVG string serialized directly from ECharts renderer |
| PDF export | Deferred — required headless browser | SVG → PDF via QuestPDF; no browser dependency |

The migration is the first item in Phase 9.2 and is a hard prerequisite for all other Phase 9.2 chart work.

---

### 1.11 Legend Position

ECharts `legend.orient` / `legend.top|bottom|left|right`. One config property change in `EChartsRenderer` once the syntax option is exposed. Quick win.

---

### 1.12 More Visualization Types

With ECharts, the effort column changes significantly — box plot, treemap, and heat map are all built-in and drop from High to Low/Medium.

| Type | Effort | Notes |
|------|--------|-------|
| Donut | Low | ECharts `pie` with `radius: ['40%', '70%']` |
| Horizontal bar | Low | ECharts `bar` with `yAxis` as category axis |
| Combo (bar + line) | Low | ECharts mixed series — cleaner than Chart.js approach |
| Box plot | Low | ECharts `boxplot` type — built-in |
| Treemap | Low | ECharts `treemap` type — built-in |
| Heat map | Medium | ECharts `heatmap` type — built-in; requires calendar or grid setup |

All chart types ship in Phase 9.2 and 9.3. Phase 9.6 (previously the library evaluation phase) is removed from the plan.

---

### 1.13 Color Mapping

Map data values to specific colors (e.g., `'North America' → '#4e79a7'`). ECharts supports per-item color via `itemStyle.color` callbacks and explicit color arrays. Medium effort — the mapping is expressed in syntax, passed through the manifest, and applied in `EChartsRenderer`.

---

### 1.14 Axis Min/Max

ECharts `yAxis.min` / `yAxis.max` (and X equivalents). Useful for normalizing comparisons across pages. Low effort once the syntax is defined.

---

## 2. What Is Missing from the Backlog

These items are not in the TODO but belong in the plan.

---

### 2.1 Dashboard-Level Filter Controls

The current parameter system binds `@param` variables to `Slicer` visuals — dropdown populated by a query. This covers simple categorical filters only.

**Missing:** Date range pickers, numeric range sliders, free-text search boxes, multi-select checkboxes. These are the filter controls every real dashboard needs. Implementing them requires:
- New visual types: `DATEPICKER`, `SLIDER`, `SEARCH`, `MULTISELECT`
- Client-side UI controls that post to `/api/parameter`
- Batched parameter updates (multiple parameters changed at once, single rebuild)

This is a larger feature than it looks and should be its own tracked phase.

---

### 2.2 Drill-Down (Completing What Is Already Designed)

`DrillDownAction` is already defined in the AST (`Reporting.md §3.3`) but is documented as "not wired in client runtime." This is half-built work sitting in the codebase. The design is done — the plumbing just needs to be closed. It should be the first interactivity item completed.

---

### 2.3 Selective Re-Evaluation on Parameter Change (Rpt-1)

The current behavior re-executes the entire script on every parameter change (`Reporting.md §8.2`). For reports with multiple heavy `CREATE DATASET` statements, changing a region filter reruns every query including those that do not reference `@region`.

Already tracked as **Rpt-1**. This must be addressed before adding more interactivity features, because it directly limits the usability of anything built on top of the parameter system.

---

### 2.4 Auto-Refresh / Scheduled Report Refresh

`CREATE DATASET ... REFRESH EVERY` has been retired. Report refresh cadence belongs in the normalized Orchestrator catalog: create a report-targeted job, attach a named schedule, and let the trusted Portal refresh path re-materialize datasets and snapshots.

This matters for live operations dashboards where data changes every 5–15 minutes. Without scheduled report refresh, the user must manually call the refresh API. The scheduler integration is now tracked through `CREATE JOB ... FOR REPORT`, `CREATE SCHEDULE`, and `ALTER JOB ... ADD SCHEDULE`.

---

### 2.5 Snapshot Integrity (Rpt-2)

`SnapshotStore` writes are non-atomic and not locked (`Reporting.md §7`). A crash mid-write produces a corrupt `.snapshot.json`. Already tracked as **Rpt-2**. Must be resolved before building more features on top of it.

---

### 2.6 Report-Level TITLE and DESCRIPTION

There is no way to give a report a display name and description. When hosting multiple reports (§1.8), the catalog page needs something to show. Add a top-level statement:

```sql
SET REPORT TITLE = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional revenue and product performance.';
```

---

### 2.7 Responsive / Mobile Layout

The CSS grid-template-areas layout is desktop-first with no breakpoints. For reports served over a network this matters. This does not need to be a Report-SQL language feature — a CSS media query in the HTML template that falls back to single-column on small screens is sufficient. Needs to be in the plan even if the implementation is a template change.

---

### 2.8 Empty / Error States for Visuals

No designed behavior exists for when a visual's source query returns zero rows or throws. Production dashboards need graceful degradation:
- Zero rows → "No data available" placeholder
- Query error → "Error loading data" with optional detail toggle

This is a runtime concern in `report-runtime.js` and `ManifestBuilder`, not a syntax concern.

---

## 3. Phased Delivery Plan

Work is sequenced by dependency, risk, and user impact. Items in the same phase can be parallelized.

---

### Phase 9.1 — Foundation Hardening
*Goal: Close known gaps in the current implementation before building new features on top of a fragile base.*

| Item | Source | Notes |
|------|--------|-------|
| `SnapshotStore` atomic writes + read/write lock (Rpt-2) | Architecture debt | `.tmp` rename pattern; `ReaderWriterLockSlim` |
| `STRUCTURE` string validation — every slot letter must appear in both `STRUCTURE` and `MAP` (Rpt-4) | Architecture debt | Linter rule |
| Linter warning when column aliases shadow Report-SQL keywords (Rpt-3) | Architecture debt | Parser-level check |
| Wire `DrillDownAction` in `report-runtime.js` | Half-built feature | AST already supports it |
| Selective re-evaluation on parameter change (Rpt-1) | Performance | Track changed `@params`; only re-run visuals whose source references them |
| Auto-refresh: schedule report refresh through Orchestrator jobs and named schedules | Incomplete feature | Use `CREATE JOB ... FOR REPORT` + `ALTER JOB ... ADD SCHEDULE`; `REFRESH EVERY` is retired |
| Empty / error state rendering in `report-runtime.js` | Missing behavior | Zero rows → placeholder; query error → error card |

**Exit criteria:** No data corruption risk, no half-built features, no silent failures on parameter change.

---

### Phase 9.2 — ECharts Migration + Visualization Polish
*Goal: Replace the rendering library, complete existing visual types, and restore export capabilities that ECharts makes tractable.*

The ECharts migration is a hard prerequisite for all other items in this phase. It is scoped and bounded — `ChartJsRenderer.cs`, `report-runtime.js`, and the bundled JS file — and must be complete before any chart-rendering work begins.

| Item | Source | Notes |
|------|--------|-------|
| **ECharts migration** — replace `ChartJsRenderer`, `report-runtime.js`, bundled JS | New decision | Prerequisite for all chart work in this phase |
| Legend position (`top`, `bottom`, `left`, `right`) | Backlog | `EChartsRenderer` option |
| Axis min/max values | Backlog | `X_AXIS`/`Y_AXIS` options in `EChartsRenderer` |
| `FORMAT()` on card values and axis labels | Backlog | Applied in `ManifestBuilder` before serialization |
| Color mapping (value → hex color) | Backlog | `COLORS(...)` clause in VISUAL OPTIONS; ECharts `itemStyle.color` |
| Donut chart | Backlog | ECharts `pie` with inner radius |
| Horizontal bar chart | Backlog | ECharts `bar` with category on Y axis |
| Box plot | Backlog | ECharts `boxplot` — built-in, no plugin |
| Treemap | Backlog | ECharts `treemap` — built-in, no plugin |
| Heat map | Backlog | ECharts `heatmap` — built-in, no plugin |
| Markdown export — chart as SVG image | Backlog | ECharts SVG renderer serialized server-side; no headless browser |
| VISUAL TEXT type | Backlog | New handler; `VALUE = '...'` or `VALUE (markdown)` |
| Source shorthand `SOURCE = #table` | Backlog | Parser accepts `#ident` directly in SOURCE |
| `PARAM = 'literal'` scalar shorthand for TITLE, SUBTITLE | Backlog | Parser only; `PARAM = (value)` third form is not implemented |
| Report-level TITLE / DESCRIPTION | Missing | `SET REPORT TITLE = '...'` — needed for catalog page |
| CREATE DATASET encryption modes (machine-bound, password, key-file) | Backlog | Machine-bound = DPAPI on Windows; document cross-OS limits |

**Exit criteria:** Rendering runs on ECharts. All current and new visual types work. Markdown export produces SVG images. A developer can build a complete, polished single-page dashboard without workarounds.

---

### Phase 9.3 — Layout Evolution
*Goal: Unlock complex multi-visual, multi-page layouts.*

| Item | Source | Notes |
|------|--------|-------|
| Page structure rework — `'A A / B C'` CSS grid-template-areas | Backlog | Parser already stores the string; runtime emits real CSS |
| CONTAINER object (BOX type) | Backlog | New `CreateContainerStatement`; nests inside PAGE |
| CONTAINER AS SCROLL type | Missing | Fixed-height scrollable column for long tables alongside fixed charts |
| Inline `STYLE (...)` block support on VISUAL, PAGE, CONTAINER | Backlog | Phase 1 of STYLE — scoped to object, no variable; `THEME = '<name>'` supported |
| `DECLARE @style STYLE` variable type | Backlog | Phase 2 — dot-notation assignment; requires `VariableScopeManager` extension |
| Multi-page support — multiple `CREATE PAGE` statements | Backlog | `ReportManifest.Pages` already exists |
| `CREATE NAVIGATION` (TAB, BUTTON, LINK types) | Backlog | HORIZONTAL/VERTICAL orientations; `DEFAULT = <page>` option required |
| Combo chart (bar + line) | Backlog | ECharts mixed series |

**Exit criteria:** A developer can build a multi-page, complex-grid dashboard with shared styles and themed visuals.

---

### Phase 9.4 — Export
*Goal: Complete the output format story now that ECharts SVG makes PDF tractable without a browser dependency.*

| Item | Source | Notes |
|------|--------|-------|
| PDF export (`--format pdf`) | Backlog | ECharts SVG → QuestPDF; no headless browser; `export` sub-command |

**Exit criteria:** Reports can be exported to PDF from the CLI without installing a browser runtime.

---

### Phase 9.5 — Interactivity & Live Data
*Goal: Make dashboards genuinely interactive beyond simple categorical slicers.*

| Item | Source | Notes |
|------|--------|-------|
| New filter visual types: DATEPICKER, SLIDER, MULTISELECT, SEARCH | Missing | Client-side controls posting to `/api/parameter`; batched parameter updates |
| Responsive layout (CSS media query fallback) | Missing | HTML template change; single-column on small screens |
| Theming / dark mode | Missing | ECharts theme system; `THEME = 'dark'` on PAGE or NAVIGATION applies globally |

**Exit criteria:** A dashboard author can expose date range filters, multi-select dropdowns, and sliders. The dashboard is usable on mobile viewports.

---

### Phase 9.6 — Hosting
*Goal: Serve multiple reports from a single server instance.*

| Item | Source | Notes |
|------|--------|-------|
| Multi-report hosting — `--reports-dir` flag, catalog at `GET /` | Backlog | `DashboardService` factory; lazy-load per report |

**Pre-work required:** Decide the report discovery mechanism (directory flag vs. manifest vs. glob) before implementation starts.

**Exit criteria:** Multiple reports are discoverable and accessible at a single server address.

---

## 4. Summary of Decisions

| Decision | Resolution |
|----------|------------|
| Rendering library | Apache ECharts — replaces Chart.js in Phase 9.2 |
| PARAM syntax forms | Two forms only: `PARAM (value)` block form and `PARAM = 'literal'` scalar shorthand. `PARAM = (value)` is not implemented. |
| CONTAINER types | BOX for Phase 9.3; SCROLL when the need arises. No geometric shapes. |
| NAVIGATION types | TAB, BUTTON, LINK. LINK renders as `Page1 \| Page2 \| Page3` inline text hyperlinks. |
| NAVIGATION orientations | HORIZONTAL and VERTICAL only. |
| NAVIGATION default page | `DEFAULT = <page>` option required on all NAVIGATION objects. |
| STYLE implementation order | Inline block first (Phase 9.3), `DECLARE @style STYLE` variable type second. |
| STYLE cascade rule | ECharts base theme → shared `@style` variable → inline properties. Inline wins. |
| STYLE THEME property | `THEME = '<name>'` sets the ECharts base theme. Additional STYLE properties override it. |
| Markdown image export | SVG from ECharts renderer — no headless browser required. Ships in Phase 9.2. |
| PDF export approach | ECharts SVG → QuestPDF. No browser dependency. Ships in Phase 9.4. |
| Advanced chart types | Box plot, treemap, heat map all built-in to ECharts. Ship in Phase 9.2 alongside standard types. |

---

## 5. Outstanding Architecture Work (Pre-Phase 9.1)

These items are from `Reporting.md` and are not optional cleanup — they block reliable feature work:

| Item | Urgency | Blocks |
|------|---------|--------|
| Rpt-1: Selective re-evaluation on parameter change | High | Usable interactivity at scale |
| Rpt-2: SnapshotStore atomic writes + locking | High | Data integrity for any hosted deployment |
| Rpt-3: Keyword alias linter warning | Medium | Correctness in complex reports |
| Rpt-4: STRUCTURE slot letter validation | Medium | Catching layout errors at parse time |
| DrillDownAction not wired in client runtime | High | Half-built feature in shipped code |

All five are resolved in Phase 9.1 before any new language features are added.
