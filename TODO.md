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
- [ ] Decide the remaining final grammar contract in `Docs/Reference/Grammar.md` before implementation. Since the product has not gone live, prefer one canonical syntax over compatibility aliases.
- [x] Page syntax decision: canonical syntax is `CREATE PAGE <name> AS (...)`.
- [x] Lineage syntax decision: canonicalize lineage introspection to `SHOW LINEAGE ...`; remove or deprecate bare `LINEAGE` before launch so observational commands consistently use `SHOW <object/view>`.
- [x] Update grammar, docs, help, samples, and tests together for `SHOW LINEAGE` forms such as:
  ```sql
  SHOW LINEAGE;
  SHOW LINEAGE FOR REPORT SalesDashboard;
  SHOW LINEAGE FOR DATASET &CustomerMart;
  SHOW LINEAGE INTO #lineage;
  ```
- [ ] Update `Docs/Report_SQL_Guide.md`, editor help, samples, and tests after the remaining grammar direction is settled.

### Phase 1 — Report layout syntax
- [x] Make `LAYOUT (...)` an explicit bucket for containers; pages use the page body itself for layout placement.
- [x] Implement the canonical page syntax:
  ```sql
  CREATE PAGE overview AS (
    TITLE = 'Executive Overview',
    STRUCTURE = 'K K / A B / C C',
    MAP (
      'K' = KpiStrip,
      'A' = RevenueByRegion,
      'B' = MarginByProduct,
      'C' = OrderDetail
    ),
    GAP = '16px',
    STYLE (THEME = light)
  );
  ```
- [x] Keep containers typed because container behavior matters:
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
- [x] Candidate container types: `BOX`, `SCROLL`, `DRAWER`, `SIDEBAR`, `TABS`, `ACCORDION`, `MODAL`, `POPOVER`. Avoid decorative/geometric container types unless there is a real reporting workflow need.
- [ ] Move layout-related settings such as `GAP`, responsive breakpoints, pinned panels, drawer placement, tabs, modals, and maximize behavior into `LAYOUT (...)` where possible.
- [ ] Update parser, AST, manifest builder, report runtime, VS Code preview, Report Portal renderer, docs, and samples together.

### Phase 2 — Actions, interactions, and buttons
- [x] Replace `OPTIONS (CROSS_VISUAL_ACTION = HIGHLIGHT|FILTER|NONE)` with a dedicated interaction clause:
  ```sql
  INTERACTIONS (
    ON_SELECT = HIGHLIGHT,
    MATCHING = Region
  )
  ```
- [ ] Fix bidirectional cross-highlight behavior using `samples/kitchen_sinks/report_kitchen_sink.rptsql` as the reference. Current bug: clicking `BarByRegion` highlights `DrillRegionDetail`, but clicking `DrillRegionDetail` does not highlight `BarByRegion` after clearing the first selection.
- [x] Decide and document valid triggers per object type:
    - Charts and tables: `ON_CLICK`.
    - Slicers/search/date/slider/textbox/numberbox/checkbox controls: `ON_CHANGE`.
    - Buttons: `ON_CLICK`.
    - Text/card/image visuals: no actions unless intentionally made clickable.
- [x] Normalize button behavior so built-in buttons and custom buttons do not feel split-brained. Preferred direction: buttons are command emitters and `ACTIONS` defines behavior.
  ```sql
  CREATE BUTTON RefreshData AS (
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
- [x] Define navigation pages inside the `CREATE NAVIGATION` body:
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
- [ ] Update `AGENTS.md`, `Docs/Report_SQL_Guide.md`, `Docs/Reference/Grammar.md`, `Docs/Strategy/ReportPortal_Strategy.md`, editor help, and sample guide so all agents and users generate the same syntax.  Make sure very container type, action, style, etc is documented.
- [ ] Remove old docs/examples for replaced syntax before launch unless a deliberate compatibility decision is made.

- [ ] **Phase 6 — Advanced Visualization Capability Gaps (BI Parity)**
    - [x] **GANTT Visual**: Port the existing Orchestrator Portal Gantt implementation (ECharts 'custom' series) into the reporting engine.
    - [x] **Pivot/Matrix Visual**: Cross-tab representation with collapsible row/column headers (Industry Standard: Power BI Matrix).
    - [x] **Sankey/Sunburst**: Relational/Flow visualizations using ECharts native types.
    - [x] **Small Multiples (Trellis)**: Repeat a visual across a grid for each category value.
    - [x] **Selection Primitives**: Brush/Lasso selection on Scatter/Scatter3D to drive parameter filters (Industry Standard: Tableau Brush).
    - [x] **Network Graph**: Force-directed graphs for lineage and relationship exploration.
    - [ ] **Maximize visual** Maximize the space of the visual to the full screen and the chart fills the space, provide a minimize button to return to previous size and show other visuals.

---

## SQL Correctness & Performance Testing

Current state: 7 hand-authored SLT files covering basic SQL paths; TPC-H with Q1+Q6 only at SF=0.1 against a single `lineitem` table. The goal is to reach a state where a correctness regression in any major engine path is caught automatically by CI before merge.

### SLT — Import real corpus cases

The SQLite SLT suite represents decades of discovered edge cases. Prefer importing too many over too few.

- [ ] **Import real SQLite SLT files** — Pull from the [SQLite logic test corpus](https://www.sqlite.org/sqllogictest/doc/trunk/about.wiki) or [CockroachDB's curated port](https://github.com/cockroachdb/cockroach/tree/master/pkg/sql/logictest/testdata/logic_test). Target minimum 5,000 cases; no arbitrary ceiling. Place under `tests/slt_data/corpus/`.
- [ ] **Add `skipif etlsql` guards** for features we deliberately do not support (e.g., recursive CTEs, `RETURNING`, `GENERATED ALWAYS`) so the corpus files run clean without masking real failures.  UPDATE: We do have recursive CTEs
- [ ] **Add column-type verification to `SltRunner.VerifyResults`** — the `query TIR` type declaration is currently ignored; validate that each cell's runtime type matches the declared type character (T=text, I=integer, R=real).

### SLT — Cover dark engine paths (hand-authored files)

These paths are currently completely untested in the SLT suite:

- [x] `tests/slt_data/cte.test` — Simple, chained, aggregate, and subquery-using CTEs.
- [x] `tests/slt_data/subquery.test` — Scalar, correlated, `EXISTS`/`NOT EXISTS`, `IN (SELECT ...)`, subquery in `FROM`.
- [x] `tests/slt_data/window.test` — `ROW_NUMBER()`, `RANK()`, `LAG()`/`LEAD()`, `SUM() OVER (PARTITION BY ... ORDER BY ...)`.
- [x] `tests/slt_data/case.test` — Searched `CASE WHEN`, `CASE` in `WHERE`/`ORDER BY`, nested `CASE`.
- [x] `tests/slt_data/string_functions.test` — `UPPER`/`LOWER`, `TRIM`/`LTRIM`/`RTRIM`, `SUBSTRING`, `CHARINDEX`, `REPLACE`, `CONCAT`, `LEFT`/`RIGHT`, `REVERSE`, `REPLICATE`.
- [x] `tests/slt_data/date_functions.test` — `DATEADD`, `DATEDIFF`, `DATEPART` for year/month/day.
- [x] `tests/slt_data/null_edge_cases.test` — NULL comparison, arithmetic propagation, `GROUP BY` with NULLs, `COUNT(*)`/`COUNT(col)`, `NULLIF`, `BETWEEN` with NULLs, all-NULL aggregates.
- [x] `tests/slt_data/type_coercion.test` — Division behavior, `CAST` string/decimal/null, decimal arithmetic, case-insensitive string comparison.
- [x] `tests/slt_data/distinct.test` — `SELECT DISTINCT`, `COUNT(DISTINCT ...)`, `SUM(DISTINCT ...)`, NULL deduplication.

### TPC-H — Seeder and query coverage

- [x] **Bump default scale factor to SF=0.1** (60,000 lineitem rows) — SF=0.01 is too small for meaningful performance signal; results at that scale measure framework overhead not engine behavior.
- [ ] **Document engine behaviors discovered by SLT** — add to `Docs/Architecture/ExpressionEvaluation.md`: division always returns decimal (no int truncation), aggregates nested inside CASE/scalar functions are not detected by `hasAgg` (engine limitation). *(String comparison case-sensitivity is now documented in Adminstrators_Guide.md §8.1 and controlled by `SET CASE_SENSITIVE`.)*

### Engine correctness bugs discovered during SLT authoring

These were identified when writing the dark-path SLT files. Each has a failing or missing test case that proves the bug.

- [x] **`CASE expr WHEN val THEN ...` (simple CASE) support** — the parser only handled the searched form (`CASE WHEN condition THEN ...`). Simple CASE raised "Expected END at the conclusion of CASE statement". Added parser support in `ExpressionParser` and evaluation logic in `ExpressionEvaluator`. Added `InputExpression` to `CaseExpression` AST.
- [ ] **`CAST(3.9 AS INT)` does not truncate** — `CAST(x AS INT)` does not strip the fractional part; `CAST(3.9 AS INT)` returns `3.9` not `3`. Fix in `TypeConverter.Cast` — when target type is INT/BIGINT/TINYINT, apply `Math.Truncate` before converting.
- [ ] **`NOT (condition)` returns 0 rows** — `WHERE NOT (a = 1)` excluded all rows in testing. Root cause not confirmed; likely a precedence or parenthesis-handling bug in the NOT operator evaluation. `WHERE a <> 1` works correctly as a workaround. Needs a targeted test and parser/evaluator fix.
- [ ] **`hasAgg` does not detect aggregates nested inside `CASE` or scalar functions** — `CASE WHEN SUM(x) IS NULL THEN 0 ELSE SUM(x) END` and `COALESCE(SUM(x), 0)` are not recognized as aggregate expressions by `AggregateEngine.hasAgg`. The engine falls through to row-mode and returns multiple rows instead of one. Fix: extend `IsAggregate()` in `AggregateEngine` to recurse into `CaseExpression` branches and `FunctionCallExpression` arguments. This also blocks TPC-H Q12 and Q14.
- [ ] **Integer division does not truncate** — `7 / 2` returns `3.5` instead of `3`. The engine treats all division as decimal. Fix: in `BinaryOperatorFactory` (or `ExpressionEvaluator`), detect when both operands are integers and return a truncated integer result. Add SLT cases to `type_coercion.test`.
- [ ] **Add seeder tables for multi-join queries** — Extend `TpcHMockDataSeeder` to seed `orders`, `customer`, `part`, `supplier` with proportional row counts at the configured SF. This unblocks Q3, Q5, Q12, Q14.
- [ ] **Add TPC-H Q3** (Shipping Priority) — three-table join `customer ⋈ orders ⋈ lineitem`, GROUP BY, ORDER BY. This is the first query to stress the JoinEngine rather than just AggregateEngine.
- [ ] **Add TPC-H Q5** (Local Supplier Volume) — six-table join; meaningful load on the hash-join path.
- [ ] **Add TPC-H Q12** (Shipping Modes and Order Priority) — two-table join + conditional aggregation with `CASE`; tests `ExpressionEvaluator` inside aggregates.
- [ ] **Add TPC-H Q14** (Promotion Effect) — `CASE` inside `SUM`; commonly used as a regression canary for aggregate correctness.
- [ ] **Verify Q1 output against known TPC-H answers** — At SF=0.1 the Q1 result is deterministic if the seeder uses a fixed seed. Check in `tests/tpch_data/expected/q1_sf01.json` with the expected group counts and aggregate sums; assert in `BenchSetupTest`.

### Benchmark baseline and CI regression detection

- [ ] **Establish a stored performance baseline** — After the first clean benchmark run at SF=0.1, export results to `tests/tpch_data/baseline/benchmark_results.json` using BenchmarkDotNet's JSON exporter. Check this file in.
- [ ] **Add a CI comparison step** — On each PR, re-run benchmarks and compare against the baseline. Fail CI if any benchmark regresses by more than 15% (mean time). A simple PowerShell script diffing the two JSON files is enough; no need for a dedicated tool.
- [ ] **Add `[Benchmark]` variants at SF=1** for local profiling — mark them `[BenchmarkCategory("LargeScale")]` and exclude from CI with `--filter Category!=LargeScale` so they only run on demand.


### Correctness regressions discovered in SLT corpus
- [ ] **Value count mismatch (41 vs 42)** — Seen at Line 3229 in corpus runs. Indicates a materialization or row-count tracking error, possibly in the `SelectStatementHandler` or `AggregateEngine`.
- [ ] **Non-deterministic Hash Mismatches** — Multiple corpus tests (e.g., Lines 2273, 94) reporting inconsistent results. Needs investigation into transient state leakage or race conditions in parallel execution paths.
- [ ] **Memory Pressure & Performance Crawl** — Performance degrades during large SLT files. Even with lineage/telemetry off, memory pressure accumulates. Initial fixes (cache clearing, static cache removal) implemented, but needs further profiling of `DataTable`/`Row` allocation patterns.
- [ ] **Persistent vs Transient parity** — Verify that `IsPersistentSession = false` (now default for SLT) does not diverge from disk-spill results.
