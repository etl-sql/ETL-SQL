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

### 1.2 Desktop project-host lifecycle

Desktop Studio uses one loopback host per project by default. Each host owns its workspace boundary,
port, execution state, connection metadata, and Git state. A local session record under the current
user's application-data directory stores the normalized workspace root, instance ID, PID, assigned
port, start time, and authentication metadata. CLI discovery accepts a record only when both its
process and authenticated health endpoint are live; failed probes remove stale crash records.

`etlsql studio <project>` reconnects to the existing healthy host or starts a host on an OS-assigned
port. `studio list`, `studio open`, and `studio stop` expose the same registry contract. Additional
browser windows share the project host. `--new-instance` deliberately creates an independent host
for the same project, with content-revision checks preventing one host from overwriting external
changes made by another.

Browser clients renew authenticated heartbeats. Tab-close signals are advisory; the host also
expires missed heartbeats and may apply a configured idle timeout once all clients are gone, no run
is active, and no server draft is pending. The explicit **Exit Studio** flow checks dirty documents
and active runs, requests graceful application shutdown, and polls for a bounded period so the UI
can report whether the host actually stopped.

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

### 2.2 Canonical browser module boundaries

The browser workbench remains canonical under
`src/ETL-SQL.ReportRuntime/Resources/Shared/designer/`, but it is composed from responsibility-owned
modules instead of one file owning UI, host, persistence, and query behavior:

- `studio.js` — workbench DOM rendering, interaction wiring, and composition.
- `studio-contracts.js` — shared, Portal-only, and desktop-only route tables plus starter scripts.
- `studio-state.js` — workbench state and isolated per-document session contexts.
- `studio-host.js` — host capability and authenticated-fetch normalization.
- `studio-data.js` — snapshot metadata, local filter projection, and sample requests.
- `studio-sql-mutations.js` — serialized parser/patcher mutations and filter persistence.
- `studio-security.js` — plaintext-secret detection and client-side credential encryption.
- `studio-lifecycle.js` — edit-lease renewal, page-hide release, and disposal.

These are all canonical shared assets. New behavior belongs in the narrowest module, and every new
file must be distributed with `node .\scripts\sync-assets.js`. Host copies remain generated output.

### 2.5 Progressive Disclosure & the Learning Path

ETL-SQL Studio is **script-first**. The visual surfaces exist so that an author who does not yet know
the language can produce correct script, and so that watching the script appear teaches them the
language. A successful Studio makes itself progressively unnecessary: the author graduates to the
script editor and returns to the visual helpers only when they are stuck.

The following are binding design constraints, not aspirations:

1. **The script is always visible and always authoritative.** The GUI is a *generator*. No visual
   state may exist that is not expressible in the saved `.rptsql` / `.etlsql` document.
2. **GUI mutations patch a range; they never replace the document.** Cursor position, scroll
   position, and selection survive every visual edit. A wholesale buffer replacement is a defect,
   because it destroys the author's place in the text and hides what actually changed.
3. **A host that lacks a capability says so.** Studio ships as one canonical asset across several
   hosts with different server surfaces. Any capability the active host cannot serve must be
   presented as explicitly unavailable, with a reason. Silently degrading to a no-op — an assist
   request that 404s, a control that appears to work and does nothing, a failure rendered as success
   — is prohibited. Editor assist routes are resolved from the host's declared capabilities, never
   hardcoded.
4. **Diagnostics are legible and actionable.** Author-facing messages explain the problem in the
   author's terms and carry a remedy line, following the Connection Wizard's `💡 Remedy:` pattern.
   Raw parser output or a raw HTTP body is not an acceptable author-facing message.
5. **The first session requires no database.** MOCKDB provides a zero-dependency cold start, so a new
   author can reach a working visual and its generated script without provisioning anything.
6. **Every GUI mutation can explain what it wrote.** The patched range is identifiable, so a
   changed-range highlight and a one-line plain-language explanation can be attached to it. The
   explanation text is sourced from the embedded language help corpus (`docs/reference`, embedded via
   `ETL-SQL.Core`) so that hover documentation, `HELP`, and Studio explanations never drift apart.

Constraints 1–5 are required for Studio to be considered functionally complete. Constraint 6 is the
learning layer built on top of them; its delivery may follow, but the range-level patch dispatch it
depends on is part of constraint 2 and is not optional.

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

### 3.1 Dashboard and paginated report workflows

Studio Home exposes **New Dashboard** and **New Paginated Report** as separate creation paths. Both
produce standard `.rptsql` documents and use the same data catalog, dataset sampling, expression and
formatting controls, preview service, script editor, parser, and range patcher. The workflow is UI
state derived from the document; it is not a second file format or hidden manifest.

- **Dashboard** uses the responsive visual board and guides the author through data, visuals,
  cross-filters, layout, and formatting. Charts, cards, tables, slicers, and other visuals continue
  to use the shared designer.
- **Paginated Report** presents the canvas as a physical sheet and guides the author through data,
  input parameters, group/detail bands, totals, header/footer bands, `PRINT_LAYOUT`, visual page
  breaks, pagination preview, and export. Page size, orientation, margins, overflow, and break rules
  are carried by the shared authoring DTOs and changed through `DesignerScriptPatcher`.

When every explicit `CREATE PAGE` declaration has the same mode, Studio selects that workflow. A
mixed-mode report, or a script without an explicit page, opens a two-choice prompt. The choice does
not modify the script or dirty the document. A valid code edit may update the inferred workflow; an
invalid intermediate edit retains both the last valid canvas and its workflow while keeping the
editor bytes unchanged.

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

> **Implementation status.** Shipped today: categorical distinct-value checkboxes (capped at 12
> sampled values), numeric min/max bounds, and the date presets above. **Not yet shipped:** the
> categorical search and Select All / Invert controls, the numeric operator set (`between`, `>`, `<`,
> `is not null`) as distinct operators rather than min/max, and Top-N. Treat the unshipped items as
> planned scope, not as current behaviour.

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

## 6.1 The authoring component contract

Studio's guided wizards — the connection creator, the data wizard, the chart builder, and the guided
report steps — live in one canonical module, `studio-authoring.js`, and are composed into the
workbench by `studio.js`. They obey five rules. Each exists because it has already been broken once,
and each break was invisible until someone clicked the button and nothing happened.

1. **Host-neutral.** No `window`, no `localStorage`, no `document.querySelector` against the Studio
   shell, no knowledge of which host is running. Everything a surface needs arrives through
   `createStudioAuthoringSurfaces`. A surface that reaches for the shell works on the host it was
   written against and degrades silently on the other four.
2. **No network of its own.** All I/O goes through the injected `request`, the only thing that knows
   about `authFetch` and the API base. Literal `/api/...` paths are banned; routes come from the
   injected tables, so a route one host does not serve fails a contract test instead of a user's
   click. The single exception is `editorTransport`, handed straight to `createScriptEditor` because
   the embedded editor is a child component owning its own transport.
3. **No script writing of its own.** Every document change goes through the injected `mutate` — the
   canonical parse, mutate, patch round-trip — so a hand edit is never clobbered and an unparseable
   document is never overwritten. `USE DATASET` is the one statement form the patcher cannot express
   and is confined to a single helper.
4. **Preview before write.** A surface shows the exact Report-SQL it is about to write and writes only
   on an explicit confirm. A step that cannot run yet says what is missing and offers the control that
   fixes it, rather than writing something half-formed or failing into an unactionable toast.
5. **Read state from the parse.** A surface reads its starting state from the canonical parse of the
   current document, never from what it wrote last time. This is what makes a wizard safe to reopen
   after the author has hand-edited the script.

`StudioAuthoringContractTests` enforces rules 1 to 3 by inspection, and the tests are verified to fail
when each rule is violated. Rules 4 and 5 are behavioural and belong to the wizard test lane.

Visual types are declared once, in `visual-preview.js`, beside the role definitions that say what each
type binds to. A palette entry with no roles cannot be configured, and roles with no palette entry
cannot be reached, so the two lists are not allowed to drift apart.

---

## 7. Pipeline execution-map projection

When opening an `.etlsql` script, Canvas View sends the current bytes to the host-neutral
`ScriptDagProjectionService` and renders its nodes and edges through the canonical `renderDag`
component. Both desktop and Portal expose the same authenticated `/api/designer/dag` contract.

- **Execution stages** — connections, statements, transforms, procedures, file movement, writes,
  exports, datasets, and report-authoring statements retain their parser-derived source line.
- **Control flow** — `IF` chains, `PARALLEL` blocks, loops, and `TRY`/`CATCH` produce labeled branch
  edges. Branch exits converge on the next sequential stage instead of being flattened.
- **Quality gates** — assertions, schema expectations, and validation commands use a distinct
  `validation` node type.
- **Navigation** — selecting a node reveals its source line. Search, stage-type filters, focus mode,
  pan, zoom, and fit-to-view are graph interactions only.

The script remains authoritative. Projection requests never patch it, and graph interactions that are
only graph interactions — search, filter, focus, pan, zoom, navigate — do not write to it. A parse
failure leaves the last valid graph visible with an error state while the editor keeps the exact
invalid intermediate bytes.

## 7.1 Pipeline task authoring

Editing on the canvas was gated on a canonical parser/patcher contract that proved byte preservation
first. That gate has been met, and the canvas can now author one specific thing: a **task**, which is
a top-level section label plus the single statement it introduces.

- **Identity is the label, never the node id.** Node ids are positional (`s0`, `s1`, …), so a hand
  edit above a task renumbers everything below it and a canvas tracking ids would follow the wrong
  box. The label is written into the script, is what the author sees, and is carried into the
  projection as `ScriptDagNode.Key`.
- **Every edit is a span replacement computed from the parse.** Add, move, relabel, repoint, remove,
  connect, and disconnect each rewrite one span; bytes outside it — line endings, comments,
  indentation, and every statement the canvas does not model — come through unchanged. The result is
  reparsed before it is returned, and an edit that would not parse is refused with its reason rather
  than applied or silently dropped.
- **Four task kinds**, each writing one statement: `EXECUTE <connection> BEGIN … END`, `COPY FILE`,
  `ASSERT`, and `SEND EMAIL`. A kind appears in the palette only once its emitted statement passes a
  focused parse, lint, formatter, and reference check (`PipelineTaskEmissionTests`). That gate is
  load-bearing: `SEND EMAIL` failed it, because the parser requires a `FROM` clause whatever the
  connector's `DEFAULT_FROM` says, so the notification task carries a sender field rather than
  emitting something that would not parse.
- **The execution task is authored in the shared query workbench**, so the SQL a task runs gets the
  same completions, hover, diagnostics, run, and results as the script pane.

### Dependencies

An explicit dependency is written as an `-- @after: a, b` tag above the task's label. The lexer reads
`-- @tag:` as a tag and the parser skips tags between statements, so the declaration is free at run
time, survives the canonical formatter, and keeps the graph in the file rather than in a sidecar the
script knows nothing about.

- Dragging a card's **connector handle** declares a dependency; dragging the **card body** reorders.
  Two gestures because they mean different things.
- **Several incoming edges are a join** — the task waits for all of them. They never imply
  concurrency: ETL-SQL expresses that only as a `PARALLEL` block, and the canvas writes none.
- ETL-SQL runs a script top to bottom, so a declared dependency that contradicted the physical order
  would be the canvas lying about the file. Connecting therefore also reorders when it must, and
  cycles and self-edges are refused.
- A task that declares dependencies has its implicit "runs after the statement above" edge replaced
  by the ones it named.

Statements the canvas does not model stay visible on the map and are deliberately not draggable: an
accurate read-only stage is better than an editable one that cannot round-trip.

### Conditional precedence edges

An edge can hand over on success, on failure, on completion, or on the author's own expression. The
condition is declared in the same tag — `-- @after: extract on failure`, or
`-- @after: extract when @@ROWCOUNT > 0` on a line of its own so a comma inside the expression is not
read as the next prerequisite — and is **lowered into the script**, because an edge that only coloured
the diagram would be the canvas describing a pipeline the engine does not run.

The lowering is two wrappers, both derived from the declaration:

- The task being watched is wrapped in `BEGIN TRY` / `BEGIN CATCH` and records its outcome into
  `@<label>_status`, declared `INT = 0` directly above it.
- The task that waits is wrapped in `IF <condition>`, reading those variables.

The status variable is **three-valued** — `0` never ran, `1` succeeded, `-1` threw — and the guard is
written *outside* the gate. A task whose own gate was false therefore stays at `0`, so a downstream
`on failure` edge does not fire for a task that was skipped: skipped is not failed.

Deriving the wrappers from the declaration rather than tracking them beside it is what keeps the two
from drifting. A hand-edited tag produces the control flow it describes, a removed edge takes its
wrapper with it, and a rename carries both. Only a wrapper carrying that bookkeeping — a `TRY` body
that sets `@<label>_status`, or an `IF` whose conjuncts are all status terms or conditions the task
still declares — is treated as the canvas's. Anything else is the author's control flow and is left
exactly as written; the task simply stays outside the editable set.

`on completion` writes no gate at all. What it asks for is the guard on the task above it, without
which an error there would end the run before the dependent was reached.

Every conditional edge is drawn with its own colour **and** its own stroke pattern, and keeps the
words on its badge — colour alone would make on-success and on-failure indistinguishable to a
red/green colour-blind reader or on a printed map. The style is derived from the edge label in the
shared renderer, so the same script reads the same way in every host.

---

## 8. Measured Performance Contract

Studio performance is governed by the reproducible Chromium fixture and OS-specific ceilings in
[`docs/benchmarks/studio-performance-budgets.md`](../../benchmarks/studio-performance-budgets.md).
The gate measures full workbench mount, post-GC JavaScript heap, real CodeMirror input-to-frame p95,
250-row visual aggregation/rendering, and full-canvas redraw/layout p95 on Windows, Linux, and macOS.

The former Kestrel-only startup estimate, process-memory estimate, sub-4 ms keystroke claim, and
unqualified sustained-60-FPS claim were not comparable measurements and are no longer product
claims. Canvas work instead has a checked 16.7 ms p95 budget: that proves Studio's redraw work fits
inside one 60 Hz frame on the fixture without pretending a headless runner controls display cadence.

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
   - The Studio performance matrix publishes startup, heap, keystroke, aggregation, and canvas-redraw measurements from the same named fixture on every supported desktop OS.

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
- **Slice 7 — Pipeline Studio Foundational DAG**: Parser-derived execution-map canvas for multi-stage
  ETL pipelines, plus the task authoring described in §7.1 — add, edit, reorder, connect, and
  disconnect labelled tasks through a lossless span patcher.
- **Slice 8 — Stabilization & Legacy Retirement**: Full parity certification, deprecation notices, and clean retirement of legacy `ReportBuilder` and `WorkstationEditor`.

---

## 14. Acceptance Evidence

1. **Lossless Round-Trip Testing**: 100% byte-for-byte AST round-trip preservation across complex production `.rptsql` and `.etlsql` scripts with custom CTEs, WHERE filters, and `#temp` tables.
2. **UI Sandbox Coverage**: Interactive sandbox stories in `tools/ui-sandbox` covering full drag-and-drop lifecycle, live sample filtering, theme switching, and code drawer toggling.
3. **Browser Automation Tests**: Playwright integration tests verifying zero-code report scaffolding, field mapping, filter pane changes, slicer promotion, and AST code drawer updates.
4. **Pre-Push Gates**: Strict compliance with `scripts/Test-PrePush.ps1`, asset sync checks, and doc hub audits.
