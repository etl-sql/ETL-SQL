# ETL-SQL Development Roadmap
## Bugs
### VS Code
 -[ ] When loading a new query the results frame doesn't clear like it should.  This was working in the past.  It should clear whenever a new query window, or a script is opened.
 -[ ] Expanding without an alias shows a fully qualified name for each column.  This wouldn't be an issue if it worked but it instead returns NULL.
    Either make the fully qualified name work or remove the m.FILE.  This needs to be tested it has broken multiple times.
```sql
 CREATE CONNECTION m ON FLATFILE('"C:\Users\chuck\scratch\ETL-SQL\TestData\test_categories.csv"');
 SELECT m.FILE.id, m.FILE.category_name FROM m.FILE;
```

### Reporting
 Using "C:\Users\chuck\scratch\ETL-SQL\samples\10_Kitchen_Sinks\report_kitchen_sink.rptsql"
 -[ ] Revenue Sunburst chart is blank
 -[ ] Clicking AdvancedCharts does not make the button turn the selected Blue
 -[ ] Revenue by Category -- Faceted by Region has all the pieces just nothing is showing for data.

## Up Next

## SQL Correctness & Performance Testing

Current state: 7 hand-authored SLT files covering basic SQL paths; TPC-H with Q1+Q6 only at SF=0.1 against a single `lineitem` table. The goal is to reach a state where a correctness regression in any major engine path is caught automatically by CI before merge.

### SLT — Import real corpus cases

The SQLite SLT suite represents decades of discovered edge cases. Prefer importing too many over too few.

- [x] **Import real SQLite SLT files** — `tests/slt_data/corpus/` contains `select1.test`–`select5.test` (10,706 query/statement cases total across ~144k lines), pulled from the SQLite logic test corpus. Well above the 5,000-case minimum.
- [x] **Add `skipif etlsql` guards** — Infrastructure is in place: `SltRunner.RunTestAsync` checks `record.Type == SltRecordType.SkipIf && record.EngineCondition == "etlsql"` at line 89 and returns early. The `select1–5` corpus files use only basic SQL (CREATE TABLE, INSERT, SELECT with arithmetic/CASE/ORDER BY) that ETL-SQL handles fully — no unsupported syntax guards needed for these files. Add `skipif etlsql` before any case in future imports that relies on syntax ETL-SQL deliberately excludes (e.g., `RETURNING`, `GENERATED ALWAYS AS`).
- [x] **Add column-type verification to `SltRunner.VerifyResults`** — the `query TIR` type declaration is currently ignored; validate that each cell's runtime type matches the declared type character (T=text, I=integer, R=real).

### Benchmark baseline and CI regression detection

- [x] **Establish a stored performance baseline** — `tests/tpch_data/baseline/benchmark_results.json` generated with BenchmarkDotNet JSON exporter (6 TPC-H benchmarks: Q1=29.7ms, Q3=41.4ms, Q5=40.8ms, Q6=20.9ms, Q12=35.0ms, Q14=41.3ms at SF=0.01). CI uses `Compare-Benchmarks.ps1` to flag >15% regressions. Note: BenchmarkDotNet 0.14+ produces `-report-full-compressed.json`; the script's glob updated to `*-report-full*.json`.
- [x] **Add a CI comparison step** — `scripts/Compare-Benchmarks.ps1` reads two BenchmarkDotNet JSON exports, prints a table, and exits 1 if any benchmark regresses by more than 15% of baseline mean. Usage in CI: run benchmarks with `--exporters json --filter Category!=LargeScale`, then `.\scripts\Compare-Benchmarks.ps1 -Baseline ... -Current ...`.
- [x] **Add `[Benchmark]` variants at SF=1** for local profiling — `TpcHBenchmarksLargeScale` wraps all six queries under `[BenchmarkCategory("LargeScale")]`; CI excludes them via `--filter Category!=LargeScale`.

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
