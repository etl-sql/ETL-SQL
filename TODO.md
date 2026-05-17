# ETL-SQL Development Roadmap

## Up Next
- [ ] **Reporting and portal language/feature streamlining**  Work this before launch as one cohesive pass. Goal: make Report-SQL feel like normal ETL-SQL, make portal administration feel like SQL DDL/admin commands, and add the missing baseline BI portal behaviors while breaking syntax is still cheap.

### Phase 0 — Lock the mental model and canonical syntax
- [x] Define the report object buckets and use them consistently everywhere:
    - `SOURCE` = data-producing query, table, or dataset reference.
    - `MAPPINGS` = visual data roles.
    - `LAYOUT` = page/container placement, structure, maps, gaps, responsive behavior.
    - `STYLE` = presentation/theme choices.
    - `OPTIONS` = renderer-specific settings only.
    - `ACTIONS` = outbound events emitted by visuals, controls, and buttons.
    - `INTERACTIONS` = cross-visual selection/filter/highlight behavior.
    - Portal commands = administrative DDL/operations such as users, folders, grants, publishing, subscriptions, and refresh jobs.
- [x] Decide the remaining final grammar contract in `Docs/Reference/Grammar.md` before implementation. Since the product has not gone live, prefer one canonical syntax over compatibility aliases.
- [x] Page syntax decision: canonical syntax is `CREATE PAGE <name> AS (...)`.
- [x] Lineage syntax decision: canonicalize lineage introspection to `SHOW LINEAGE ...`; remove or deprecate bare `LINEAGE` before launch so observational commands consistently use `SHOW <object/view>`.
- [x] Update grammar, docs, help, samples, and tests together for `SHOW LINEAGE` forms such as:
  ```sql
  SHOW LINEAGE;
  SHOW LINEAGE FOR REPORT SalesDashboard;
  SHOW LINEAGE FOR DATASET &CustomerMart;
  SHOW LINEAGE INTO #lineage;
  ```
- [x] Update `Docs/Report_SQL_Guide.md`, editor help, samples, and tests after the remaining grammar direction is settled.

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
- [x] Move layout-related settings such as `GAP`, responsive breakpoints, pinned panels, drawer placement, tabs, modals, and maximize behavior into `LAYOUT (...)` where possible.
- [x] Update parser, AST, manifest builder, report runtime, VS Code preview, Report Portal renderer, docs, and samples together.

### Phase 2 — Actions, interactions, and buttons
- [x] Replace `OPTIONS (CROSS_VISUAL_ACTION = HIGHLIGHT|FILTER|NONE)` with a dedicated interaction clause:
  ```sql
  INTERACTIONS (
    ON_SELECT = HIGHLIGHT,
    MATCHING = Region
  )
  ```
- [x] Fix bidirectional cross-highlight behavior using `samples/10_Kitchen_Sinks/report_kitchen_sink.rptsql` as the reference. Current bug: clicking `BarByRegion` highlights `DrillRegionDetail`, but clicking `DrillRegionDetail` does not highlight `BarByRegion` after clearing the first selection.
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
- [x] Add button/report actions for common workflow needs:
    - Show or hide `VISIBLE = OFF` visuals.
    - Refresh report or selected visuals.
    - Export CSV/Excel/PDF.
    - Navigate to page.
    - Open modal/drawer.
    - Clear filters.
- [x] Add portal/viewer support for maximizing a single visual. Treat this as a layout/viewer capability, not a chart-specific option.

### Phase 3 — Navigation, datasets, publishing, and portal admin grammar
- [x] Define navigation pages inside the `CREATE NAVIGATION` body:
  ```sql
  CREATE NAVIGATION MainNav AS TAB (
    ORIENTATION = HORIZONTAL,
    DEFAULT = Overview,
    PAGES (Overview, Details, Trends)
  );
  ```
- [x] Review report datasets and portal datasets together. Keep `CREATE DATASET &name AS (...)` for report-owned reusable data, but make the naming story clear for `&dataset`, `#temp`, `USE DATASET`, `REFRESH DATASET`, and portal-registered datasets.
- [x] Keep portal admin syntax as a separate command family:
    - Prefer `WITH (...)` for metadata/config on portal objects.
    - Prefer command verbs for operations: `PUBLISH REPORT`, `REFRESH REPORT`, `REBUILD SNAPSHOT`, `DROP SNAPSHOT`.
    - Decide whether paths are always string literals and names are always identifiers or strings; avoid mixing forms without a rule.
    - Keep secrets in expression positions so `ENC:` and future secret providers work consistently.
- [x] Review subscription and refresh-job syntax for clarity. `CREATE REFRESH JOB FOR REPORT ... SCHEDULE ... AT ...` and `CREATE SUBSCRIPTION FOR REPORT ... DELIVER TO ...` are readable, but should be documented as portal commands rather than report-definition syntax.

### Phase 4 — Portal scriptability and baseline UX gaps
- [ ] Add Active Directory / LDAP / Windows-integrated identity support, or clearly define the first supported enterprise identity path.
- [ ] Treat every portal capability as script-first. If it can be done in the UI, it must have a SQL-like administrative syntax, and if the engine already has a primitive, prefer exposing that primitive coherently instead of inventing a second model.
    - [x] Add script syntax for portal dataset registry refresh, metadata updates, deletion, and dataset ACL grants/revokes.
    - [x] Add script syntax for favorites, report history, report dependencies, catalog search/recent lists, effective permissions, usage metrics, and report-script validation.
    - [ ] Add script syntax for share links, embed tokens, saved parameter/filter views, and alerts as those capabilities land.
- [ ] Polish and surface capabilities that already exist so they feel complete in the portal UI, docs, and scripting surface:
    - [x] Group-based permissions and folder ACLs.
    - [x] Publishing and republishing reports.
    - [x] Subscriptions and subscription history.
    - [x] Audit/activity log.
    - [x] Dataset registry/refresh status.
    - [x] Lineage/dependency data where available.
- [x] Standardize report metadata. Owner/contact/tags can already come from script metadata comments such as `/* @owner: TeamName */`; define the canonical portal tags and decide how they flow into catalog fields.
- [x] Standardize environment/deployment conventions. Dev/test/prod can already be handled with `CREATE SETS !DEV`, `CREATE SETS !TEST`, `CREATE SETS !PROD`, and `USE SETS !...`; define the portal/admin scripting pattern instead of adding a parallel deployment model too early.
- [ ] Fill catalog quality-of-life gaps expected in BI portals, with scriptable equivalents where useful:
    - [x] Search reports/folders.
    - [x] Favorites.
    - [x] Recently viewed.
    - [x] Tags/categories.
    - [x] Last refreshed, last viewed, and failure status badges.
- [ ] Fill governance/admin gaps:
    - [x] Effective permissions view for a user/report/folder.
    - [x] Admin-facing usage metrics: views, unique viewers, refresh duration/failures, subscription delivery failures.
    - [x] Content endorsement/certification or "trusted" marker.
- [ ] Fill lifecycle/publishing gaps:
    - [x] Report version/history metadata.
    - [x] Replace/republish flow with validation before publish.
    - [x] Scripted promotion/deployment pattern built on `CREATE SETS` and portal `PUBLISH`/`ALTER REPORT` commands.
    - [x] Dependency/lineage view showing report -> datasets -> source connections if the raw lineage is already available but not exposed as a portal experience.
- [ ] Fill sharing/consumption gaps:
    - [x] Share link with permissions check.
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

### Phase 6 — Advanced Visualization Capability Gaps (BI Parity)
- [x] **GANTT Visual**: Port the existing Orchestrator Portal Gantt implementation (ECharts 'custom' series) into the reporting engine.
- [x] **Pivot/Matrix Visual**: Cross-tab representation with collapsible row/column headers (Industry Standard: Power BI Matrix).
- [x] **Sankey/Sunburst**: Relational/Flow visualizations using ECharts native types.
- [x] **Small Multiples (Trellis)**: Repeat a visual across a grid for each category value.
- [x] **Selection Primitives**: Brush/Lasso selection on Scatter/Scatter3D to drive parameter filters (Industry Standard: Tableau Brush).
- [x] **Network Graph**: Force-directed graphs for lineage and relationship exploration.
- [x] **Maximize visual**: Maximize the space of the visual to the full screen and the chart fills the space, provide a minimize button to return to previous size and show other visuals.

- [ ] **Phase 7 — SQL Dialect Parity & Modern Standards (Cross-Engine Compatibility)**
    - [ ] **VALUES as a standalone table constructor**: Support `SELECT * FROM (VALUES (1, 'A'), (2, 'B')) AS t(id, name)`.
    - [ ] **APPROX_COUNT_DISTINCT**: Implement HyperLogLog-based approximate distinct count for large-scale datasets.
    - [ ] **PostgreSQL Operators**: Support `ILIKE` (case-insensitive LIKE), `~` (regex match), and `~*` (regex case-insensitive match).
    - [ ] **Filtered Aggregates**: Fully implement the `FILTER (WHERE ...)` clause in `AggregateEngine` (currently parsed but ignored).
    - [ ] **Standard JSON_TABLE**: Implement the full SQL:2016 `JSON_TABLE` with `COLUMNS` clause (currently supports a simplified 2-arg TVF).
    - [ ] **Standard SQL:2008 OFFSET/FETCH**: Support `FETCH FIRST n ROWS ONLY` as an alternative to `LIMIT`.
    - [ ] **Advanced Window Frames**: Support `GROUPS` mode and frame exclusion clauses (`EXCLUDE CURRENT ROW`, etc.).
    - [ ] **Temporal Queries**: Support `FOR SYSTEM_TIME AS OF` for system-versioned tables.
    - [ ] **Row Pattern Matching**: Implement `MATCH_RECOGNIZE` for pattern matching in sequences.
    - [ ] **Generated Columns**: Support `GENERATED ALWAYS AS (expr)` in `CREATE TABLE`.
    - [ ] **Standard Aggregates**: Support `EVERY`, `ANY`, and `SOME` as aggregate functions.

---

## SQL Correctness & Performance Testing

Current state: 7 hand-authored SLT files covering basic SQL paths; TPC-H with Q1+Q6 only at SF=0.1 against a single `lineitem` table. The goal is to reach a state where a correctness regression in any major engine path is caught automatically by CI before merge.

### SLT — Import real corpus cases

The SQLite SLT suite represents decades of discovered edge cases. Prefer importing too many over too few.

- [ ] **Import real SQLite SLT files** — Pull from the [SQLite logic test corpus](https://www.sqlite.org/sqllogictest/doc/trunk/about.wiki) or [CockroachDB's curated port](https://github.com/cockroachdb/cockroach/tree/master/pkg/sql/logictest/testdata/logic_test). Target minimum 5,000 cases; no arbitrary ceiling. Place under `tests/slt_data/corpus/`.
- [ ] **Add `skipif etlsql` guards** for features we deliberately do not support (e.g., recursive CTEs, `RETURNING`, `GENERATED ALWAYS`) so the corpus files run clean without masking real failures.  UPDATE: We do have recursive CTEs
- [x] **Add column-type verification to `SltRunner.VerifyResults`** — the `query TIR` type declaration is currently ignored; validate that each cell's runtime type matches the declared type character (T=text, I=integer, R=real).

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
- [x] `tests/slt_data/aggregates.test` — `SUM`/`AVG`/`COUNT`/`MIN`/`MAX`, `COUNT(DISTINCT ...)`, `SUM(DISTINCT ...)`, aggregates nested inside `CASE`/`COALESCE` (regression for `hasAgg` fix), empty-table behavior.

### TPC-H — Seeder and query coverage

- [x] **Bump default scale factor to SF=0.1** (60,000 lineitem rows) — SF=0.01 is too small for meaningful performance signal; results at that scale measure framework overhead not engine behavior.
- [x] **Document engine behaviors discovered by SLT** — added `§15 Known Behaviors and Engine Quirks` to `Docs/Architecture/ExpressionEvaluation.md`: division truncation semantics, CAST truncation, NULL three-valued logic table, string case sensitivity.

### Engine correctness bugs discovered during SLT authoring

These were identified when writing the dark-path SLT files. Each has a failing or missing test case that proves the bug.

- [x] **`CASE expr WHEN val THEN ...` (simple CASE) support** — the parser only handled the searched form (`CASE WHEN condition THEN ...`). Simple CASE raised "Expected END at the conclusion of CASE statement". Added parser support in `ExpressionParser` and evaluation logic in `ExpressionEvaluator`. Added `InputExpression` to `CaseExpression` AST.
- [x] **`CAST(3.9 AS INT)` does not truncate** — Fixed in `TypeConverter.Cast`: INT/INTEGER/TINYINT/SMALLINT/BIGINT converters now apply `Math.Truncate` before `Convert.ToDecimal`, matching SQL truncation-toward-zero semantics.
- [x] **`NOT (condition)` returns 0 rows** — Fixed: `UnaryExpression` was missing from the `EvaluateInternalAsync` switch so it fell through to the default `null` return. Added `EvaluateUnary` which correctly propagates NULL (UNKNOWN) and flips booleans. Also fixed comparison operators to return NULL (not false) when either operand is NULL, implementing SQL three-valued logic.
- [x] **`hasAgg` does not detect aggregates nested inside `CASE` or scalar functions** — Fixed in `AggregateEngine.ApplyAggregation`: the `aggregateSpecs` population loop now also calls `CollectAggregates` for columns whose top-level expression is not itself an aggregate (CASE, COALESCE, etc.). Nested aggregate states are tracked with `ColumnIndex=-1` and their finalized values are written to the `AGG_<expr>` key so the expression evaluator resolves them correctly during re-evaluation of the wrapper expression.
- [x] **Integer division does not truncate** — Fixed in `BinaryOperatorFactory.MathOp`: division now checks if both operands have decimal scale 0 (integer-valued literals and `Convert.ToDecimal(int)` results); if so, applies `Math.Truncate`. Decimal-scaled operands (e.g. `7.0`) still produce real results. Updated `type_coercion.test`.
- [x] **Add seeder tables for multi-join queries** — Added `region`, `nation`, `customer`, `supplier`, `part`, `orders` to `TpcHMockDataSeeder` with TPC-H-proportional row counts. Fixed `l_orderkey`/`l_partkey`/`l_suppkey` key ranges to reference seeded table sizes. Also filled in previously null lineitem columns: `l_commitdate`, `l_receiptdate`, `l_shipmode`, `l_shipinstruct`, `l_comment`.
- [x] **Add TPC-H Q3** (Shipping Priority) — three-table join `customer ⋈ orders ⋈ lineitem`, GROUP BY, ORDER BY. Added benchmark method and passing test in `BenchSetupTest`. Uses explicit `INNER JOIN` syntax (comma joins not yet supported).
- [x] **Add TPC-H Q5** (Local Supplier Volume) — six-table join; meaningful load on the hash-join path. Added benchmark method and passing test (asserts correct columns; may return 0 rows at SF=0.01 if no customer/supplier share an Asian nation in 1994 — valid).
- [x] **Add TPC-H Q12** (Shipping Modes and Order Priority) — two-table join + conditional aggregation with `CASE`; tests `ExpressionEvaluator` inside aggregates. Added benchmark method and passing test asserting MAIL/SHIP rows.
- [x] **Add TPC-H Q14** (Promotion Effect) — `CASE` inside `SUM`; commonly used as a regression canary for aggregate correctness. Added benchmark method and passing test asserting single scalar row.
- [x] **Verify Q1 output against known TPC-H answers** — Snapshot captured at SF=0.1 (seed 42) in `tests/tpch_data/expected/q1_sf01.json`. `TestQ1DeterministicAtSF01` in `BenchSetupTest` creates the file on first run and asserts exact JSON match on subsequent runs.
- [x] **Comma joins already work** — Parser converts `FROM t1, t2` to `CROSS JOIN` at line 408 of `Parser.cs`; the WHERE predicate then acts as the equi-join filter. Added `TestCommaJoin_TwoTables` and `TestCommaJoin_ThreeTables` in `StmtJoinTests` to confirm both 2- and 3-table comma syntax. The original TODO example had a stray comma in the WHERE clause (`WHERE t1.id = t2.id, AND ...`) which would be a syntax error — the underlying feature was not broken.

### Benchmark baseline and CI regression detection

- [ ] **Establish a stored performance baseline** — After the first clean benchmark run at SF=0.1, export results to `tests/tpch_data/baseline/benchmark_results.json` using BenchmarkDotNet's JSON exporter. Check this file in.
- [x] **Add a CI comparison step** — `scripts/Compare-Benchmarks.ps1` reads two BenchmarkDotNet JSON exports, prints a table, and exits 1 if any benchmark regresses by more than 15% of baseline mean. Usage in CI: run benchmarks with `--exporters json --filter Category!=LargeScale`, then `.\scripts\Compare-Benchmarks.ps1 -Baseline ... -Current ...`.
- [x] **Add `[Benchmark]` variants at SF=1** for local profiling — `TpcHBenchmarksLargeScale` wraps all six queries under `[BenchmarkCategory("LargeScale")]`; CI excludes them via `--filter Category!=LargeScale`.


### Correctness regressions discovered in SLT corpus
- [x] **Value count mismatch (41 vs 42)** — Was at select1.test Line 3229 (90-value triple-column CASE query). Root cause: `hasAgg` missed aggregates nested inside `CASE`; fixed in `AggregateEngine`. `Line3221_TripleColumnCaseAndArithmetic` in `CorpusRegressionTests` confirms correct hash.
- [x] **Non-deterministic Hash Mismatches** — Lines 94 and 2273 (CASE with scalar subquery, BETWEEN/NOT BETWEEN). Root causes: `NOT` operator returned null; comparison operators returned false instead of NULL for NULL operands. Both fixed. `Line94_CaseWithScalarSubquery_CountAndHash` and `Line2270_NotBetweenAndBetweenWithArithmetic` confirm correct hashes.
- [x] **Memory Pressure & Performance Crawl** — Memory guard (75% working-set limit) aborts corpus runs before OOM; lineage and telemetry disabled by default in SLT mode. `DataTable`/`Row` allocation profiling under sustained load is future work but not a correctness issue.
- [x] **Persistent vs Transient parity** — `SpillParity_TransientVsPersistentGivesSameHash` in `CorpusRegressionTests` forces spilling (threshold=5 rows) in both `IsPersistentSession=false` and `IsPersistentSession=true` modes and confirms identical MD5 hash for the same 30-row result set.

## Memory & Join Performance Optimization (Discovered in `select4.test`)

- [x] **Fix Multi-Join Cartesian Product Explosion** — `CrossJoinPredicatePushdown.Optimize` (called at the top of `SelectExecutionEngine.ExecuteHeavyPipeline`) extracts AND-connected WHERE predicates and pushes them into CROSS JOIN conditions, converting `CROSS JOIN (true)` → `INNER JOIN (predicate)`. The engine then uses a hash join instead of nested-loop Cartesian product. Also fixed `TryExtractEqualityKeys` in `JoinEngine` to handle multi-join left keys (previously only the original FROM-table alias was accepted, so the 2nd+ join always fell back to nested loop). Validated by `TestCommaJoin_PredicatePushdown_LargeData` (500×500×500 = 125M rows without pushdown → 500 rows with pushdown, no OOM).
    - [x] **Implement Join Predicate Pushdown**: Done — `CrossJoinPredicatePushdown.cs` in `ETL_SQL.Engine.Engines`. Handles chain joins, star joins, mixed explicit+comma joins, and unqualified predicates (conservative: stays in WHERE).
    - [x] **Progressive WHERE Pushdown for Unqualified Predicates**: `JoinEngine.ApplyJoins` now flattens the WHERE clause and, for each CROSS JOIN step, finds predicates whose `GetSourceColumns()` are all present in the combined left+right column set — then uses them as the join condition. This handles the `select4.test L29188` pattern (`WHERE a3=b9 AND c9=688 AND a1=d9`) where `CrossJoinPredicatePushdown` was helpless (unqualified names → `GetSourceTables()` returns empty). Validated by `TestCommaJoin_FiveTable_UnqualifiedPredicates`.
    - [ ] **Spill-to-Disk for Nested Loops**: Still open. Comma-joins with complex non-equality predicates (OR conditions, subqueries) still use nested-loop and do not spill. Hash-join path already spills via `ExternalJoinEngine`.
- [x] **Optimize `Row` Materialization and Allocation Patterns**
    - [x] **Eliminate Redundant Dictionary Copies**: Added `Row.ForEachColumn(Action<string,object?>)` — iterates schema array then dynamic dict without allocating a `Dictionary`. Updated `JoinEngine.CombineRows`, the base-table qualification loop, and `GetJoinRowsAsyncEnumerable` to use `ForEachColumn` instead of `.Columns`. Eliminates 2 dict allocs per `CombineRows` call and 1 per right-side row setup.
    - [x] **Schema-based Combined Rows**: Added `JoinEngine.BuildCombinedSchema(leftSample, rightSample)` — called once per join step, shared across all `CombineRows` calls in that step. Combined rows now use array-based `Row(schema)` instead of `new Row()` with dynamic dict, reducing per-row storage ~5×. Applied to hash, merge, and nested-loop join paths.
    - [ ] **Qualified Column Expansion**: Qualified names (e.g., `t1.col`) are still added as dynamic columns on each right-side row clone. A `SchemaMapping` approach that strips the qualifier at expression-eval time instead of pre-expanding would eliminate this overhead.
- [ ] **Intermediate Pipeline Streaming**
    - [ ] Refactor `SelectExecutionEngine.ExecuteHeavyPipeline` to apply `WHERE` filters in a streaming fashion *immediately* after each join step. Materializing 10B rows just to filter them down to 100 rows in the next step is the primary cause of OOM and GC thrashing.
    - [ ] Add `allRows.Clear()` or similar hints to ensure the GC can reclaim intermediate lists as soon as a pipeline stage completes.

## Engine Stability & Correctness (Code Review Findings)

- [ ] **Fix Identifier Cache Collision for Dynamic Rows** — `ExpressionEvaluator._identifierCache` currently keys on `(null, name)` for all schema-less rows. In sessions with multiple dynamic row sets (like multi-join results), this causes cache hits that return incorrect column indices from previous rows.
- [ ] **Fix Unsafe Spill Store Type Inference** — `SpillSerializationHelper.TryParseString` automatically coerces strings that look like numbers/dates when reading from disk. This leads to non-deterministic type changes between in-memory (preserved strings) and spilled (coerced types) execution.
- [ ] **Enhance Join Equality Detection** — Update `JoinEngine.TryExtractEqualityKeys` to resolve unqualified identifiers against join participants. Currently, `WHERE id1 = id2` falls back to nested loops while `WHERE t1.id1 = t2.id2` uses hash joins.
- [ ] **Robust External Join Partitioning** — `ExternalJoinEngine` should implement recursive partitioning if a single partition still exceeds memory. Currently, it assumes each partition of a large table will fit in a single `Dictionary<CompoundKey, List<Row>>`.
- [x] **Optimize Row property performance** — Added `Row.ForEachColumn` for zero-alloc iteration in hot paths (join engines). `Row.Columns` retained for compatibility but hot-path callers now use `ForEachColumn`.
- [ ] **Fix DataTable.RemoveColumn Complexity** — Current $O(N^2)$ implementation (rebuilding the index map) should be replaced with a more efficient schema update pattern.

## JS/TS & VS Code Extension (Code Review Findings)

- [ ] **Secure Password Passing** — Refactor `ReplManager` and `extension.ts` to pass the `--pass` argument via stdin or environment variables instead of command-line arguments to prevent exposure in process lists.
- [ ] **Async File Discovery in Extension** — Replace synchronous `fs.existsSync` and `fs.readFileSync` calls in the extension activation path with async equivalents to prevent blocking the VS Code Extension Host.
- [ ] **Dynamic Target Framework Detection** — Remove hardcoded `net10.0` paths in `extension.ts`; detect the target framework folder dynamically to support future .NET upgrades.
- [ ] **Granular UI Updates in Report Runtime** — Investigate replacing `root.innerHTML = ''` in `report-runtime.js` with a lightweight diffing approach (e.g., Preact or manual DOM patching) to preserve UI state and prevent flickering.
- [ ] **Cryptographic Nonces for CSP** — Update `ReportPreviewPanel` to use `crypto.getRandomValues()` for generating CSP nonces instead of `Math.random()`.
