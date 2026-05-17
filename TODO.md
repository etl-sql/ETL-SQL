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

## Memory & Join Performance Optimization (Discovered in `select4.test`)

- [x] **Fix Multi-Join Cartesian Product Explosion** — `CrossJoinPredicatePushdown.Optimize` (called at the top of `SelectExecutionEngine.ExecuteHeavyPipeline`) extracts AND-connected WHERE predicates and pushes them into CROSS JOIN conditions, converting `CROSS JOIN (true)` → `INNER JOIN (predicate)`. The engine then uses a hash join instead of nested-loop Cartesian product. Also fixed `TryExtractEqualityKeys` in `JoinEngine` to handle multi-join left keys (previously only the original FROM-table alias was accepted, so the 2nd+ join always fell back to nested loop). Validated by `TestCommaJoin_PredicatePushdown_LargeData` (500×500×500 = 125M rows without pushdown → 500 rows with pushdown, no OOM).
    - [x] **Implement Join Predicate Pushdown**: Done — `CrossJoinPredicatePushdown.cs` in `ETL_SQL.Engine.Engines`. Handles chain joins, star joins, mixed explicit+comma joins, and unqualified predicates (conservative: stays in WHERE).
    - [x] **Progressive WHERE Pushdown for Unqualified Predicates**: `JoinEngine.ApplyJoins` now flattens the WHERE clause and, for each CROSS JOIN step, finds predicates whose `GetSourceColumns()` are all present in the combined left+right column set — then uses them as the join condition. This handles the `select4.test L29188` pattern (`WHERE a3=b9 AND c9=688 AND a1=d9`) where `CrossJoinPredicatePushdown` was helpless (unqualified names → `GetSourceTables()` returns empty). Validated by `TestCommaJoin_FiveTable_UnqualifiedPredicates`.
    - [x] **Spill-to-Disk for Nested Loops**: Done — `JoinEngine.PerformNestedLoopJoinSpilled` pages left side to disk via `SpillStore` when `allBufferedRows.Count > JoinSpillThreshold` and no equality keys. For each page of left rows read back, nested-loops against in-memory right side. RIGHT OUTER / FULL OUTER track matched right indices via `HashSet<int>`. Hash-join path already spills via `ExternalJoinEngine`.
    - [x] **Fix Comma-Join Exponential Slowdown / Hang for Unqualified Predicates** (discovered in `select4.test` L29467 and L32930):
        - [x] **Early Identifier Qualification**: Done — `IdentifierQualifier.QualifyAsync` (new class in `ETL_SQL.Engine.Engines`) is called before `CrossJoinPredicatePushdown.Optimize` in `ExecuteHeavyPipeline`. Resolves each FROM/JOIN table's column schema via `ResolveDataSourceAsync + GetColumnsAsync`, builds a bare→alias map (ambiguous names skipped), then rewrites unqualified `IdentifierExpression` nodes in the WHERE clause (handles Binary, Unary, FunctionCall, In, Between, IsNull, Like, Case).
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
    - [x] **True streaming from pipeline to final projection**: Done — `SelectExecutionEngine.ExecuteHeavyPipeline` now computes `canDeferWhere` when no post-WHERE stage (GROUP BY, WINDOW, QUALIFY, ORDER BY, LIMIT, DISTINCT) needs all rows upfront. When deferrable, skips materializing a second `List<Row>` filtered copy and instead applies the WHERE predicate inline in the projection loop.
