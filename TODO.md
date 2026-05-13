# ETL-SQL Development Roadmap

## Up Next
- [ ] **Reporting and portal language/feature streamlining**  Work this before launch as one cohesive pass. Goal: make Report-SQL feel like normal ETL-SQL, make portal administration feel like SQL DDL/admin commands, and add the missing baseline BI portal behaviors while breaking syntax is still cheap.

### Phase 0 — Lock the mental model and canonical syntax
- [ ] Define the report object buckets and use them consistently everywhere:
    - `SOURCE` = data-producing query, table, or dataset reference.
    - `MAPPINGS` = visual data roles.
    - `LAYOUT` = page/container placement, structure, maps, gaps, responsive behavior.
    - `STYLE` = presentation/theme choices.
    - `OPTIONS` = renderer-specific settings only.
    - `ACTIONS` = outbound events emitted by visuals, controls, and buttons.
    - `INTERACTIONS` = cross-visual selection/filter/highlight behavior.
    - Portal commands = administrative DDL/operations such as users, folders, grants, publishing, subscriptions, and refresh jobs.
- [ ] Decide the final grammar contract in `Docs/Reference/Grammar.md` before implementation. Since the product has not gone live, prefer one canonical syntax over compatibility aliases.
- [ ] Update `Docs/Report_SQL_Guide.md`, editor help, samples, and tests only after the grammar direction is settled.

### Phase 1 — Report layout syntax
- [ ] Make `LAYOUT (...)` an explicit bucket for pages and containers.
- [ ] Change canonical page syntax to avoid repeating `PAGE` or forcing `AS LAYOUT`:
  ```sql
  CREATE PAGE Overview AS (
    TITLE = 'Executive Overview',
    LAYOUT (
      STRUCTURE = 'K K / A B / C C',
      MAP (
        'K' = KpiStrip,
        'A' = RevenueByRegion,
        'B' = MarginByProduct,
        'C' = OrderDetail
      ),
      GAP = '16px'
    ),
    STYLE (THEME = light)
  );
  ```
- [ ] Keep containers typed because container behavior matters:
  ```sql
  CREATE CONTAINER FilterDrawer AS DRAWER (
    TITLE = 'Filters',
    LAYOUT (
      STRUCTURE = 'A / B / C',
      MAP (
        'A' = RegionFilter,
        'B' = StatusFilter,
        'C' = ApplyWorkflow
      )
    ),
    OPTIONS (
      PINNABLE = ON,
      ICON = 'filter'
    )
  );
  ```
- [ ] Candidate container types: `BOX`, `SCROLL`, `DRAWER`, `SIDEBAR`, `TABS`, `ACCORDION`, `MODAL`, `POPOVER`. Avoid decorative/geometric container types unless there is a real reporting workflow need.
- [ ] Move layout-related settings such as `GAP`, responsive breakpoints, pinned panels, drawer placement, tabs, modals, and maximize behavior into `LAYOUT (...)` where possible.
- [ ] Update parser, AST, manifest builder, report runtime, VS Code preview, Report Portal renderer, docs, and samples together.

### Phase 2 — Actions, interactions, and buttons
- [ ] Replace `OPTIONS (CROSS_VISUAL_ACTION = HIGHLIGHT|FILTER|NONE)` with a dedicated interaction clause:
  ```sql
  INTERACTIONS (
    ON_SELECT = HIGHLIGHT,
    MATCHING = Region
  )
  ```
- [ ] Fix bidirectional cross-highlight behavior using `samples/kitchen_sinks/report_kitchen_sink.rptsql` as the reference. Current bug: clicking `BarByRegion` highlights `DrillRegionDetail`, but clicking `DrillRegionDetail` does not highlight `BarByRegion` after clearing the first selection.
- [ ] Decide and document valid triggers per object type:
    - Charts and tables: `ON_CLICK`.
    - Slicers/search/date/slider/textbox/numberbox/checkbox controls: `ON_CHANGE`.
    - Buttons: `ON_CLICK`.
    - Text/card/image visuals: no actions unless intentionally made clickable.
- [ ] Normalize button behavior so built-in buttons and custom buttons do not feel split-brained. Preferred direction: buttons are command emitters and `ACTIONS` defines behavior.
  ```sql
  CREATE BUTTON RefreshData AS BUTTON (
    TITLE = 'Refresh',
    ACTIONS (ON_CLICK = REFRESH_REPORT)
  );
  ```
- [ ] Add button/report actions for common workflow needs:
    - Show or hide `VISIBLE = OFF` visuals.
    - Refresh report or selected visuals.
    - Export CSV/Excel/PDF.
    - Navigate to page.
    - Open modal/drawer.
    - Clear filters.
- [ ] Add portal/viewer support for maximizing a single visual. Treat this as a layout/viewer capability, not a chart-specific option.

### Phase 3 — Navigation, datasets, publishing, and portal admin grammar
- [ ] Move `CREATE NAVIGATION ... WITH PAGES (...)` to one canonical body form:
  ```sql
  CREATE NAVIGATION MainNav AS TAB (
    ORIENTATION = HORIZONTAL,
    DEFAULT = Overview,
    PAGES (Overview, Details, Trends)
  );
  ```
- [ ] Review report datasets and portal datasets together. Keep `CREATE DATASET &name AS (...)` for report-owned reusable data, but make the naming story clear for `&dataset`, `#temp`, `USE DATASET`, `REFRESH DATASET`, and portal-registered datasets.
- [ ] Keep portal admin syntax as a separate command family:
    - Prefer `WITH (...)` for metadata/config on portal objects.
    - Prefer command verbs for operations: `PUBLISH REPORT`, `REFRESH REPORT`, `REBUILD SNAPSHOT`, `DROP SNAPSHOT`.
    - Decide whether paths are always string literals and names are always identifiers or strings; avoid mixing forms without a rule.
    - Keep secrets in expression positions so `ENC:` and future secret providers work consistently.
- [ ] Review subscription and refresh-job syntax for clarity. `CREATE REFRESH JOB FOR REPORT ... SCHEDULE ... AT ...` and `CREATE SUBSCRIPTION FOR REPORT ... DELIVER TO ...` are readable, but should be documented as portal commands rather than report-definition syntax.

### Phase 4 — Portal scriptability and baseline UX gaps
- [ ] Add Active Directory / LDAP / Windows-integrated identity support, or clearly define the first supported enterprise identity path.
- [ ] Treat every portal capability as script-first. If it can be done in the UI, it must have a SQL-like administrative syntax, and if the engine already has a primitive, prefer exposing that primitive coherently instead of inventing a second model.
- [ ] Polish and surface capabilities that already exist so they feel complete in the portal UI, docs, and scripting surface:
    - Group-based permissions and folder ACLs.
    - Publishing and republishing reports.
    - Subscriptions and subscription history.
    - Audit/activity log.
    - Dataset registry/refresh status.
    - Lineage/dependency data where available.
- [ ] Standardize report metadata. Owner/contact/tags can already come from script metadata comments such as `/* @owner: TeamName */`; define the canonical portal tags and decide how they flow into catalog fields.
- [ ] Standardize environment/deployment conventions. Dev/test/prod can already be handled with `CREATE SETS !DEV`, `CREATE SETS !TEST`, `CREATE SETS !PROD`, and `USE SETS !...`; define the portal/admin scripting pattern instead of adding a parallel deployment model too early.
- [ ] Fill catalog quality-of-life gaps expected in BI portals, with scriptable equivalents where useful:
    - Search reports/folders.
    - Favorites.
    - Recently viewed.
    - Tags/categories.
    - Last refreshed, last viewed, and failure status badges.
- [ ] Fill governance/admin gaps:
    - Effective permissions view for a user/report/folder.
    - Admin-facing usage metrics: views, unique viewers, refresh duration/failures, subscription delivery failures.
    - Content endorsement/certification or "trusted" marker.
- [ ] Fill lifecycle/publishing gaps:
    - Report version/history metadata.
    - Replace/republish flow with validation before publish.
    - Scripted promotion/deployment pattern built on `CREATE SETS` and portal `PUBLISH`/`ALTER REPORT` commands.
    - Dependency/lineage view showing report -> datasets -> source connections if the raw lineage is already available but not exposed as a portal experience.
- [ ] Fill sharing/consumption gaps:
    - Share link with permissions check.
    - Embed link/token story for internal apps.
    - Per-user saved parameter/filter views, similar to bookmarks.
    - Comments/annotations can wait unless collaboration becomes a target v1 feature.
- [ ] Add alerting after subscriptions are solid:
    - Threshold alerts on KPI/card/gauge visuals.
    - Alert ownership and visibility rules.
    - Alert delivery through the same notification/subscription infrastructure.

### Phase 5 — Documentation, samples, and release readiness
- [ ] Update the golden workflow and kitchen sink reports to the new canonical syntax.
- [ ] Add parser tests for every changed statement form.
- [ ] Add report runtime tests for interactions, buttons, layout containers, navigation, and maximize.
- [ ] Add portal integration tests for publish, permissions, subscriptions, refresh, export, audit, and catalog search.
- [ ] Update `AGENTS.md`, `Docs/Report_SQL_Guide.md`, `Docs/Reference/Grammar.md`, `Docs/Strategy/ReportPortal_Strategy.md`, editor help, and sample guide so all agents and users generate the same syntax.
- [ ] Remove old docs/examples for replaced syntax before launch unless a deliberate compatibility decision is made.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).

### Phase 6 — VS Code Notebook Integration (`.etlnb`)
*Brainstorming & Implementation Plan for Interactive Data Engineering*

**Overview & Architectural Advantage**
The Notebook format (Jupyter-style) is a natural fit for iterative ETL (Extract → Inspect → Clean → Load). Because the VS Code extension already runs `ETL-SQL.exe` as a persistent background REPL daemon, the engine is **natively stateful**. If Cell 1 executes `SELECT INTO #Staging`, Cell 2 can immediately query `#Staging` without any changes to the core C# engine.

- [x] **Define the `.etlnb` format (NotebookSerializer)**
    - Register a custom Notebook Serializer in `package.json` for the `.etlnb` extension.
    - Map VS Code's `NotebookData` to a clean JSON structure representing Markdown and ETL-SQL code cells.
- [x] **Implement the ETL-SQL Kernel (NotebookController)**
    - Register a VS Code `NotebookController` that declares support for the `etlsql` language.
    - Wire the `executeHandler` to send cell text payloads directly to the existing `ReplManager.execute()` queue.
    - Capture the asynchronous REPL outputs (`results`, `message`, `variables`).
- [x] **Output Rendering & MIME Types**
    - Map REPL string messages to `text/plain` cell outputs (e.g., `PRINT` statements and logs).
    - Map REPL `results` (data grids) to standard VS Code rich table outputs.
- [ ] **IntelliSense & LSP Integration**
    - Configure the `vscode-languageclient` to recognize notebook cells as part of the same virtual document. This ensures that variables declared in Cell 1 provide auto-complete suggestions in Cell 2.
- [x] **Advanced Features (Interactive Mode)**
    - [x] **Inline Visuals**: If a cell contains a `CREATE VISUAL`, capture the JSON manifest and render the ECharts widget directly below the cell.
    - [ ] **Cell-Level Lineage**: Display mini-lineage graphs showing exactly how data mutated within that specific cell.
    - [ ] **Productionize Command**: Add a `Export to .etlsql` button that concatenates the notebook cells into a single, production-ready pipeline script.

**Gotchas & Required Engine Tweaks**
To fully support the out-of-order execution style of notebooks, the engine REPL will need a few minor capability upgrades:
- [x] **Idempotency & Re-execution**: Running `SELECT INTO #Staging` twice currently throws an error. The REPL needs a mode to silently drop/replace existing `#temp` tables to support users re-running the same cell without restarting the kernel.
- [x] **Graceful Cancellation**: Currently, aborting a query (`ReplManager.stop()`) kills the whole `ETL-SQL.exe` process, losing all `#temp` tables and variables. We need a `{"action": "cancel"}` REPL command to abort the current operation but keep the session alive.
- [x] **REPL Visual Emission**: `CREATE VISUAL` currently registers silently in memory. The engine must be updated to emit `{"type": "visual", "manifest": {...}}` over stdout so the Notebook Controller can render it.
- [ ] **LSP Virtual Document Paths**: VS Code passes notebook cells to the Language Server as `vscode-notebook-cell://...` URIs. The LSP and engine must correctly resolve relative paths (e.g., `FROM FLATFILE('data.csv')`) using the physical folder path of the `.etlnb` file.
