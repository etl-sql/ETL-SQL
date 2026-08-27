# Architecture Decision Record: ETL-SQL Studio (Unified Dual-Projection Visual & Script Workbench)

**Status:** Accepted (Design Specification)  
**Authoritative Horizon:** Next  
**Target Delivery:** Phased across upcoming minor milestones  

---

## 1. Context & Reimagined Mental Model

ETL-SQL serves three core developer and analyst personas through four coordinated surfaces:

1. **Terminal IDE (`ETL-SQL.TUI`)**: Keyboard-driven, zero-GUI text environment optimized for DevOps, SSH sessions, air-gapped deployments, and headless servers.
2. **VS Code Extension (`etl-sql-vscode`)**: Code-first editor optimized for software and analytics engineers managing multi-file Git repositories, CI/CD branches, and custom automation.
3. **ETL-SQL Studio (Flagship Desktop & Web UI)**: The primary visual and interactive authoring environment, combining drag-and-drop WYSIWYG scaffolding with full-featured script editing.
   - **Desktop Edition (`WorkstationEditor` / `etlsql studio`)**: Local developer workstation application running over local loopback (`localhost:port`).
   - **Portal SaaS Edition (`Portal Studio` / `/studio/`)**: Zero-install web application hosted inside the enterprise security boundary, enforcing Zero-Trust connection access, Gateway routing, and Row-Level Security (RLS).

### 1.1 The Pivot: Dual Projections on the Active File (No Disconnected Tabs)

An initial exploration proposed separate top-level application tabs ("Script Tab vs. Report Tab vs. Pipeline Tab"). With fresh eyes, this model was rejected:
- In ETL-SQL, **every artifact is fundamentally a script file** (`.rptsql`, `.etlsql`, or `.sql`).
- Artificially forcing users to switch tabs or tools creates UX friction and breaks context.
- **The Chosen Design:** ETL-SQL Studio operates on the **active document**, providing **three seamless viewing projections** via header toolbar toggles:
  - `[ 🎨 Canvas View ]` (WYSIWYG layout stage for `.rptsql`, or Pipeline DAG for `.etlsql`).
  - `[ 🌓 Split View ]` (Visual canvas + live CodeMirror 6 code panel with bi-directional AST sync).
  - `[ ⌨️ Code View ]` (Full-screen script editor with real-time server lint diagnostics & results grid).

---

## 2. Modern Workbench Architecture & UI Layout

ETL-SQL Studio adopts the clean, minimalist vector icon design language established in `WorkstationEditor`:

```
┌───┬──────────────────────────────────────────────────────────────────────────────────────────────────┐
│   │ 📄 Sales_Overview.rptsql ×   📄 Ingest_Orders.etlsql ×   [ + ]             [ 🎨 Canvas | 🌓 Split | ⌨️ Code ] [ ▶ Run ]│
├───┼───────────────────────┬──────────────────────────────────────────┬───────────────────────────────┤
│ 📁│ 🔌 DATA & FIELDS      │ 🎨 VISUAL CANVAS (WYSIWYG)               │ 🛠️ PROPERTY & FILTER DOCK     │
│   │ ───────────────────── │ ──────────────────────────────────────── │ ───────────────────────────── │
│ 🔌│ ▾ ⚡ onprem_gw (MSSQL)│ [Page: Executive ▾]           [+ Visual] │ 🏷️ Selected: Visual `rev_kpi`  │
│   │   ▾ table: orders     │ ┌──────────────────┐ ┌─────────────────┐ │ 📊 Type: [ KPI Card    ▾]     │
│ 🔍│     # order_id        │ │ Total Revenue    │ │ Orders Count    │ │ 📈 Value: [ total_amount ▾]   │
│   │     📅 order_date     │ │ $1,429,800       │ │ 12,450          │ │    Aggregation: [ SUM   ▾]    │
│ 🌿│     💲 total_amount   │ └──────────────────┘ └─────────────────┘ │                               │
│   │     🔤 region         │ ┌──────────────────────────────────────┐ │ 🔍 FILTERS                    │
│ ⚙️│                       │ │ Regional Revenue (Bar Chart)         │ │ ▾ Visual Filter               │
│   │ 📊 VISUAL PALETTE     │ │   North ██████████ $540k             │ │   region: [x]North [x]West    │
│   │  [KPI]  [Bar]  [Line] │ │   South ██████ $320k                 │ │ ▾ Dataset Global              │
│   │  [Donut] [Table]      │ │   West  ████████ $410k               │ │   status = 'Completed'        │
│   │                       │ └──────────────────────────────────────┘ │ ⚡ [ Promote to Slicer ]      │
├───┴───────────────────────┴──────────────────────────────────────────┴───────────────────────────────┤
│ ⌨️ SCRIPT PROJECTION (Live CodeMirror 6 - Shown in Split View or Code View)              [ _ ][ □ ][ ✕ ]│
│ ─────────────────────────────────────────────────────────────────────────────────────────────────────│
│ 1  CREATE CONNECTION dw AS MSSQL('SHARED:corp_gw');                                                  │
│ 2  CREATE DATASET ds_orders AS SELECT order_date, total_amount, region FROM dw.orders                 │
│ 3    WHERE status = 'Completed';                                                                     │
│ 4  PAGE "Executive" {                                                                                │
│ 5      CONTAINER row { VISUAL rev_kpi TYPE 'KPI' MAPPINGS (VALUE = SUM(total_amount)); }            │
│ 6  }                                                                                                 │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Left Activity Rail (Clean Vector Icons)
- 📁 **File Explorer**: Browse local project directory (`.etlsql`, `.rptsql`, `.sql`) on desktop, or catalog folders/reports in SaaS Portal.
- 🔌 **Data Catalog & Connections**: Browse active connections, Gateway published resources, database tables, and draggable column pills (📅 Dates, 💲 Measures, 🔤 Categories).
- 🔍 **Filter Pane**: Integrated dataset global filters (`WHERE`), local visual filters (`FILTERS`), and 1-click slicer promotion.
- 🌿 **Source Control (Git)**: Active branch indicator, file change status, commit, and diff inspection.
- ⚙️ **Settings**: Light/Dark theme toggle, font scale, formatter options.

---

## 3. Dynamic Context-Aware Canvas Adaptation

The Studio automatically adapts its visual stage based on the file extension of the active tab:

```
               ┌────────────────────────────────────────────────────────┐
               │              Active Document in Studio                 │
               └──────────────┬──────────────────────────┬──────────────┘
                              │                          │
           When opening a `.rptsql` file       When opening a `.etlsql` file
                              │                          │
                              ▼                          ▼
               ┌────────────────────────┐ ┌─────────────────────────────┐
               │ 🎨 WYSIWYG Card Canvas │ │ ⚡ Pipeline DAG Visualizer  │
               │ (KPIs, Charts, Grids,  │ │ (Extract ➔ #temp ➔ Transform│
               │  Filters & Slicers)    │ │  ➔ Assert Check ➔ Load/S3)  │
               └────────────────────────┘ └─────────────────────────────┘
                              ▲                          ▲
                              └───────────┬──────────────┘
                                          │
                        Toggling [ ⌨️ Code ] or [ 🌓 Split ]
                                          │
                                          ▼
                       ┌─────────────────────────────────────┐
                       │  CodeMirror 6 Editor & Diagnostics  │
                       │  (Always the authoritative script)  │
                       └─────────────────────────────────────┘
```

---

## 4. Live Data Ingestion via `__ETLSNAP__`

To give authors an authentic "live data feel" without remote database lag or latency:

1. **On-Select Sample Ingestion**:
   - When a table is chosen, `POST /api/designer/data-sample` executes a clamped `TOP 250` query under caller RLS and security context.
   - The response populates the Studio's in-memory `window.__ETLSNAP__` store with typed schema and sample rows.
2. **Zero-Latency In-Memory Aggregations**:
   - Visual cards compute their aggregations directly in JavaScript against the sample rows:
     - **KPI Card**: `reduce()` sum in ~0.1 ms.
     - **Bar / Donut**: Group-by category and aggregate values in ~0.5 ms.
     - **Line Chart**: Group-by chronological date bucket in ~0.8 ms.
     - **Data Table**: Tabulator/grid rendering with client-side sorting in ~1 ms.
   - Dragging fields, resizing cards, changing chart types, or altering palettes re-renders in real-time at 60 FPS without network calls.
3. **Debounced Query Sync**:
   - When the user edits dataset SQL in the Code Drawer, debounced parsing (`POST /api/designer/data-preview`) refreshes the sample rows, updating all canvas visuals immediately.

---

## 5. Visual Filter Pane & WHERE Clause Generation

The **Filter Pane** eliminates manual SQL boolean syntax while maintaining code-first transparency:

### 5.1 Filter Scopes
- **Dataset Filter (Global `WHERE`)**: Restricts data extracted by the dataset query. Injected into `CREATE DATASET ... WHERE ...`.
- **Visual Filter (Local `FILTERS`)**: Restricts data for a single visual card. Injected into `VISUAL ... OPTIONS (FILTERS = (...))`.
- **Interactive Viewer Slicer**: Generates runtime interactive parameters and slicer controls for report readers.

### 5.2 Type-Aware Filter Controls
- **Categorical (Strings)**: Distinct values populated from `__ETLSNAP__` sample rows with checkboxes, search, and "Select All / Invert". Emits `WHERE col IN ('A', 'B')`.
- **Numeric (Decimals, Integers)**: Operators (`between`, `>`, `<`, `is not null`) and range slider. Emits `WHERE amount >= 1000`.
- **Date/Time**: Relative presets (`Today`, `Last 7 Days`, `Last 30 Days`, `This Quarter`, `YTD`) or calendar range picker. Emits `WHERE order_date >= DATEADD(day, -30, CURRENT_DATE)`.
- **Top-N**: "Show Top [ N ] by [ Measure ]". Emits `ORDER BY measure DESC LIMIT N`.

### 5.3 1-Click "Promote to Viewer Slicer"
Clicking **"Promote to Slicer"** on any filter:
1. Declares a report parameter: `CREATE PARAMETER selected_region AS STRING('North') OPTIONS (LABEL = 'Select Region');`
2. Parameterizes the dataset query: `WHERE region = @selected_region OR @selected_region IS NULL`
3. Drops an interactive **Slicer Visual** onto the top of the canvas:
   `VISUAL reg_slicer TYPE 'SLICER' MAPPINGS (FIELD = region) OPTIONS (BIND = @selected_region);`

---

## 6. Bi-Directional AST Synchronization Mechanics

To prevent clobbering hand-crafted SQL queries, CTEs, or custom procedural logic when GUI edits occur, the Studio employs **Surgical AST Patching**:

1. **GUI-to-Code Direction**:
   - Modifying visual options (title, palette, sort, format, filter) or moving containers triggers `DesignerScriptPatcher`.
   - The patcher locates the specific statement span in the text buffer and replaces only that target clause, preserving surrounding SQL queries, CTEs, formatting, and comments.
2. **Code-to-GUI Direction**:
   - When the user types in the Code Drawer, debounced parsing (`POST /api/designer/parse`) updates the AST and refreshes the canvas visual cards and field bindings without resetting selected states.
3. **Safe Fallback**:
   - If manual SQL introduces a syntax error, lint diagnostics highlight the error in the Code Drawer gutter without crashing the visual canvas; the canvas maintains the last valid parsed state.

---

## 7. Pipeline Studio Foundational DAG (Visual ETL Placeholder)

When opening an `.etlsql` script, the Canvas View projects the pipeline's execution flow into an interactive node DAG:

- **Source Nodes (`EXTRACT / READ`)**: Connections, Gateway resources, files (`CSV`, `Parquet`, `JSON`), REST endpoints.
- **Staging Nodes (`#temp tables`)**: Memory-staged intermediate tables.
- **Transform Recipe Nodes (`TRANSFORM`)**: Visual step-builder for rolling averages, period-over-period delta %, pivots, date gap filling, and deduplication.
- **Data Quality Gate Nodes (`VALIDATE / CHECK`)**: Zero-Trust assertion rules (`EXPECT NOT NULL`, `ASSERT UNIQUE`, range limits, drift rules).
- **Destination Nodes (`MERGE / LOAD / EXPORT`)**: Target database tables, SFTP destinations, S3/Azure object storage buckets.

All nodes and connections map losslessly to statements in the underlying `.etlsql` file.

---

## 8. Performance Architecture: Web vs. Heavy Desktop Benchmark

ETL-SQL Studio avoids the memory bloat and latency common to Electron-based desktop wrappers by leveraging native web standards and lightweight server processes:

| Performance Metric | Traditional Electron / Heavy UI | ETL-SQL Studio (Vanilla JS + CM6 + Kestrel) |
| :--- | :--- | :--- |
| **Process Startup** | 2.5 – 5.0 s (Chromium boots whole process tree) | **< 80 ms** (ASP.NET Core Minimal API loopback) |
| **Working Set RAM** | 350 MB – 800 MB | **~35 MB** (Runs in existing browser or native webview) |
| **Keystroke Input Latency** | 20 – 50 ms (Virtual DOM reconciler) | **< 4 ms** (CodeMirror 6 chunked virtual line renderer) |
| **Visual Redraw Rate** | 100 – 300 ms (IPC serialization lag) | **1 ms @ 60 FPS** (In-memory `__ETLSNAP__` JS evaluation) |
| **Framework Overhead** | 2.5 MB React/Angular bundle + runtime | **Zero framework bloat** (Native ES Modules & CSS Grid) |

---

## 9. Cross-Platform Parity Strategy (Windows, macOS, Linux)

Running the UI in modern Chromium, WebKit (Safari), and Firefox guarantees identical visual fidelity across all three platforms:

1. **Unified Keybinding Abstraction (`Mod` Key)**:
   - `Mod+Enter` automatically maps to `Ctrl+Enter` on Windows/Linux and `Cmd+Enter` on macOS.
   - `Mod+S` saves, `Mod+Shift+P` opens the Command Palette, `Alt+C` toggles the Code Drawer.
2. **Native Typography & Display Scaling**:
   - Uses `--portal-font: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", sans-serif;`.
   - Native font smoothing (`-webkit-font-smoothing: antialiased`) ensures crisp rendering on Apple Retina displays, Linux FreeType, and Windows ClearType.
3. **Cross-Platform Path Normalization**:
   - Zero-Trust path resolution (`IExecutionContext.ResolvePath()`) normalizes path delimiters across Windows backslashes, Linux forward slashes (`/var/data`), and macOS (`/Users/...`).

---

## 10. Ergonomic Workflow, Usability & Fitts's Law Hierarchy

To ensure a seamless authoring workflow, the layout adheres to three core ergonomic principles:

1. **Left-to-Right Mental Model (Input ➔ Construct ➔ Refine)**:
   - **Left**: Data sources, tables, draggable field pills, visual palette.
   - **Center**: WYSIWYG canvas or CodeMirror script editor.
   - **Right**: Property inspector, formatters, and filter controls.
   - **Top-Right**: Primary projection modes (`[ 🎨 Canvas | 🌓 Split | ⌨️ Code ]`) and the high-priority `[ ▶ Run ]` button.
2. **"Zero-Mouse" Keyboard Navigation**:
   - Visual cards navigate via `Tab` / Arrow keys.
   - `Delete` removes active cards; `Enter` opens property inspector; `Escape` closes modals and restores focus (`dialog-a11y.js`).
3. **WCAG AA Accessibility**:
   - High-contrast ratio compliance across dark and light themes.
   - Tooltips on all icon buttons displaying keyboard shortcuts.

---

## 11. Agent-Driven UI Testing & Autonomous Usability Audits

ETL-SQL Studio leverages autonomous agent testing to continuously evaluate and polish usability without manual QA:

```
┌───────────────────────────┐      ┌─────────────────────────────┐      ┌───────────────────────────┐
│ AI Agent Driving Workflow │ ───▶ │ Playwright Headless Browser │ ───▶ │ `tools/ui-sandbox` Story  │
└───────────────────────────┘      └─────────────────────────────┘      └───────────────────────────┘
              ▲                                                                       │
              │                                                                       ▼
              │                     ┌─────────────────────────────┐     ┌───────────────────────────┐
              └──────────────────── │ Analyzes UX Friction Points │ ◀── │ Inspects Bounding Boxes,  │
                                    │ (Layout Shifts, Misaligns)  │     │ DOM Events, & Render Time │
                                    └─────────────────────────────┘     └───────────────────────────┘
```

1. **Headless Browser Journeys (`tests/ETL-SQL.Portal.BrowserTests`)**:
   - Autonomous agents drive Playwright in headless Chromium to test realistic user journeys: connect to Gateway → select table → drag cards → configure filters → toggle split code → run script.
2. **DOM Geometry & Bounding Box Audits**:
   - Automated checks measure element bounding boxes (`getBoundingClientRect()`) to detect overlapping elements, text clipping, cramped margins, or broken responsive breakpoints across 1024x768 to 4K resolutions.
3. **Performance Profiling**:
   - Measures sub-millisecond timings for keystrokes, filter clicks, and canvas redraws to prevent performance regressions.

---

## 12. Parallel Development & Safe Transition Policy

To ensure zero regression risk and maintain business continuity:

1. **Independent Parallel Construction**:
   - `ETL-SQL Studio` is built as an independent, side-by-side component (prototyped in `tools/ui-sandbox` stories and hosted via canonical assets).
   - Existing `ReportBuilder` (`ETL-SQL.ReportBuilder`) and `WorkstationEditor` (`ETL-SQL.WorkstationEditor`) remain fully operational, tested, and untouched during Studio development.
2. **Side-by-Side Availability**:
   - During milestone stabilization, `etlsql studio` and `/studio/` will be available alongside legacy CLI commands and Portal designer routes.
3. **Graceful Retirement Post-Stabilization**:
   - Only after ETL-SQL Studio passes all Playwright browser test journeys, achieves 100% AST round-trip preservation, and completes user acceptance will `ReportBuilder` and legacy editor surfaces be deprecated and retired.

---

## 13. Vertical Delivery Slices

- **Slice 1 — Unified Studio Shell & Left Activity Rail (in UI Sandbox)**: Modern tabbed workbench, Activity Rail icons (Files, Connections, Filters, Git, Settings), and view projection toggles (`[ 🎨 Canvas | 🌓 Split | ⌨️ Code ]`).
- **Slice 2 — Live Data `__ETLSNAP__` Ingestion & 60 FPS Visual Canvas**: Fast sample query ingestion (`TOP 250`), in-memory browser aggregation, drag-and-drop visual card placement, and responsive container layout (`CONTAINER row { ... }`).
- **Slice 3 — Type-Aware Filter Pane & Slicer Promotion**: Global dataset `WHERE` filters, local visual `FILTERS`, distinct value checklist from `__ETLSNAP__`, relative date presets, and 1-click "Promote to Slicer".
- **Slice 4 — Property Inspector & Smart Defaults**: Field role assignment (Measure/Category/Breakdown), aggregation selectors (`SUM`, `AVG`, `COUNT`, `MIN`, `MAX`), 1-click number formatting (currency, percent, compact), and design token themes.
- **Slice 5 — Collapsible Code Drawer & Bi-Directional Sync**: Slide-up CodeMirror 6 editor (`Alt+C`), surgical AST text patching, debounced code-to-canvas synchronization, and inline lint diagnostics.
- **Slice 6 — Governed Data Preview & Multi-Surface Packaging**: Interactive preview execution under caller RLS and memory arbiters; shared asset packaging across Portal Studio (`/studio/index.html`), VS Code Webview, and Workstation Editor.
- **Slice 7 — Pipeline Studio Foundational DAG (Future)**: Visual node-graph canvas for multi-stage ETL pipelines emitting `.etlsql` scripts.
- **Slice 8 — Stabilization & Legacy Retirement**: Full parity certification, deprecation notices, and clean retirement of legacy `ReportBuilder` and `WorkstationEditor`.

---

## 14. Acceptance Evidence

1. **Lossless Round-Trip Testing**: 100% byte-for-byte AST round-trip preservation across complex production `.rptsql` and `.etlsql` scripts with custom CTEs, WHERE filters, and `#temp` tables.
2. **UI Sandbox Coverage**: Interactive sandbox stories in `tools/ui-sandbox` covering full drag-and-drop lifecycle, live sample filtering, theme switching, and code drawer toggling.
3. **Browser Automation Tests**: Playwright integration tests verifying zero-code report scaffolding, field mapping, filter pane changes, slicer promotion, and AST code drawer updates.
4. **Pre-Push Gates**: Strict compliance with `scripts/Test-PrePush.ps1`, asset sync checks, and doc hub audits.
