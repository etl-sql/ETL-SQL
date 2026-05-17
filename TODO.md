# ETL-SQL Development Roadmap

## Up Next

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
    - [x] **Fix Comma-Join Exponential Slowdown / Hang for Unqualified Predicates** (discovered in `select4.test` L29467 and L32930):
        - [ ] **Early Identifier Qualification**: Enhance the `SelectExecutionEngine` pipeline (or compile phase) to automatically qualify unqualified identifiers (e.g. `e8` -> `t8.e8`) against active connection/table schemas before running optimizer passes. This will enable `CrossJoinPredicatePushdown` to optimize the comma-joins correctly.
        - [x] **Filter Pushdown Optimization**: `JoinEngine.ApplyJoins` now pre-filters `allBufferedRows` (the FROM table) before the join loop and calls `PreFilterJoinTable` after `GetJoinRows` for each join table, applying predicates that reference only that table's own columns. Prevents single-table predicates like `765=b4` or `d6 IN (...)` from ever participating in a Cartesian product. Also fixed `TryExtractEqualityKeys` to use bare column names for unqualified hash keys (`c5=a9` → hash join in multi-join contexts).
        - [x] **Prune Resolved Progressive Predicates**: In `JoinEngine.ApplyJoins`, `ApplyResolvablePredicates` now removes applied predicates from `wherePredicates` after filtering; `TryEnrichCrossJoin` removes promoted predicates from the list when converting CROSS to INNER. Prevents redundant re-evaluation in subsequent join steps.
    - [x] **Correct Type-Checking Annotations in expressions.test**:
        - [x] Changed expected query types from `query I` (Integer) to `query T` (Text) in `tests/slt_data/expressions.test` on lines 18 (UPPER(s)), 32 (CASE returning High/Low), and 39 (CAST to string + '!').
- [x] **Optimize `Row` Materialization and Allocation Patterns**
    - [x] **Eliminate Redundant Dictionary Copies**: Added `Row.ForEachColumn(Action<string,object?>)` — iterates schema array then dynamic dict without allocating a `Dictionary`. Updated `JoinEngine.CombineRows`, the base-table qualification loop, and `GetJoinRowsAsyncEnumerable` to use `ForEachColumn` instead of `.Columns`. Eliminates 2 dict allocs per `CombineRows` call and 1 per right-side row setup.
    - [x] **Schema-based Combined Rows**: Added `JoinEngine.BuildCombinedSchema(leftSample, rightSample)` — called once per join step, shared across all `CombineRows` calls in that step. Combined rows now use array-based `Row(schema)` instead of `new Row()` with dynamic dict, reducing per-row storage ~5×. Applied to hash, merge, and nested-loop join paths.
    - [x] **Qualified Column Expansion**: `GetJoinRowsAsyncEnumerable` now builds a `TableSchema` once per join with bare names as canonical slots and qualified names (e.g. `t6.d6`) as aliases via `TableSchema.AddAlias` — pointing to the same value slot. `BuildCombinedSchema` calls `CopyAliasesTo` from both sides so qualified lookups continue to resolve in combined rows. Eliminates the per-row clone-and-add-dynamic pattern, halving value storage for each right-side table in a multi-join.
- [x] **Intermediate Pipeline Streaming (join-level)**
    - [x] `JoinEngine.ApplyResolvablePredicates`: after each join step, applies AND-flattened WHERE predicates whose columns are all present in the current result (e.g., `d6 IN (885,924,...)` is applied right after the join that introduces `d6`, before the next step). Skips outer-join steps. `InExpression.GetSourceColumns()` now delegates to `Left` so IN-list predicates are visible to the column detector.
    - [ ] **True streaming from pipeline to final projection**: `ExecuteHeavyPipeline` still buffers all rows into `allRows` after joins. For queries without ORDER BY or window functions, the final projection could stream directly from the join output without a full in-memory list.

## Engine Stability & Correctness (Code Review Findings)

- [x] **Fix Identifier Cache Collision for Dynamic Rows** — `ResolveIdentifierFallback` now skips `_identifierCache` when `context.Schema == null`. Schema-less rows have no stable identity so cache hits were stale across different dynamic row shapes. Schema-based combined rows (from the `BuildCombinedSchema` fix) are correctly keyed by their specific `TableSchema` instance.
- [x] **Fix Unsafe Spill Store Type Inference** — `SpillSerializationHelper.TryParseString` was coercing strings that look like numbers/dates. Removed the coercion: `JsonValueKind.String` elements now return the raw string, preserving type fidelity across spill boundaries.
- [x] **Enhance Join Equality Detection** — `TryExtractEqualityKeys` now accepts optional `leftCols`/`rightCols` bare-column sets. When both identifiers are unqualified and unambiguously in opposite sets, they're promoted to hash-join keys with the correct alias prefix. Callers in `ApplyJoins` pass the column sets from actual row data.
- [ ] **Robust External Join Partitioning** — `ExternalJoinEngine` should implement recursive partitioning if a single partition still exceeds memory. Currently, it assumes each partition of a large table will fit in a single `Dictionary<CompoundKey, List<Row>>`.
- [x] **Optimize Row property performance** — Added `Row.ForEachColumn` for zero-alloc iteration in hot paths (join engines). `Row.Columns` retained for compatibility but hot-path callers now use `ForEachColumn`.
- [x] **Fix DataTable.RemoveColumn Complexity** — `TableSchema.RemoveColumn` now only updates affected indices (those after the removed column) rather than clearing and rebuilding the full map. Added `RemoveColumns(IReadOnlyCollection<string>)` batch method for O(N) multi-column removal.

## JS/TS & VS Code Extension (Code Review Findings)

- [x] **Secure Password Passing** — Refactor `ReplManager` and `extension.ts` to pass the master password through `ETL_SQL_MASTER_PASSWORD` instead of command-line arguments, preventing exposure in process lists.
- [x] **Async File Discovery in Extension** — Replace synchronous file/process discovery in the extension activation path with async equivalents to prevent blocking the VS Code Extension Host.
- [x] **Dynamic Target Framework Detection** — Remove hardcoded `net10.0` paths from extension executable discovery; detect the newest `net*` target framework folder dynamically to support future .NET upgrades.
- [x] **Granular UI Updates in Report Runtime** — Replace the top-level `root.innerHTML = ''` rebuild clear with DOM `replaceChildren()` and keep refreshes on the postMessage path so parameter/UI state is preserved where the host can update in place.
- [x] **Cryptographic Nonces for CSP** — Update `ReportPreviewPanel` to use Node cryptographic random bytes for CSP nonces instead of `Math.random()`.
