# Strategy: ETL-SQL Query Execution Efficiency
### v0.7.0 Mitigations → v0.8.x Streaming and Query Planning

> [!IMPORTANT]
> **Active performance strategy.** The v0.7.0 mitigations (spill paths, external engines, batch thresholds) are shipped. The v0.8.x streaming and query-planning phases described below are future roadmap items — do not treat them as current product reference without checking the source tree.

**Status:** Active performance strategy  
**Intent:** Stabilize large-query execution first, then reduce materialization and add a small optimizer in phases.  
**Honest summary:** This work will primarily improve memory scalability and long-run stability. It will often improve elapsed query time by reducing allocation and GC pressure, but small queries may see neutral or slightly worse runtime if streaming overhead is added without care.

## The Problem

ETL-SQL's query execution engine uses a **full-materialization model**: every stage of a SQL
pipeline (join, filter, aggregate, sort, project) buffers the entire intermediate result set into
a `List<Row>` before passing it to the next stage. A 5-stage query creates 5 full copies of data
in memory simultaneously.

More precisely: the simple SELECT path already streams, and parts of `JoinEngine` already perform
early predicate filtering for some join shapes. The make-or-break risk is the **complex SELECT path**:
when a query has joins, aggregates, windows, DISTINCT, ORDER BY, OFFSET/LIMIT, QUALIFY, or a
combination of those features, execution still falls back to a materialized multi-pass model.

This is the single biggest technical risk in the project. It manifests as:

- **Memory exhaustion** during large corpus test runs (~20,000 sequential queries). The process
  working set grows monotonically and eventually hits the 75%-of-system-RAM memory guard.
- **Slow large queries** — a join of two 500k-row tables creates a 500k-row list before filtering
  even starts, when a streaming approach would filter during the join and never allocate that list.
- **Fragmented .NET heap** — tens of thousands of `List<Row>` allocations go to the Large Object
  Heap (LOH), which doesn't compact during normal GC. Committed virtual memory stays high even
  after live objects are collected. The memory guard fires on `WorkingSet64` (the high-water mark),
  not on live heap.

## Why SQL Server and Postgres Don't Have This Problem

They implement the **Volcano/Iterator model** (1994, still the foundation of every major RDBMS):

- Every operator exposes a single method: `GetNext()` — pull one row (or small batch) from its
  child operator. No operator knows how many rows its child will produce.
- **Non-blocking operators** (filter, project, nested-loop join probe, UNION ALL) use O(1) memory:
  they forward rows one at a time without accumulating anything.
- **Blocking operators** (hash join build phase, sort, GROUP BY hash aggregate) buffer only what
  they must. A hash join buffers only the *smaller* (build) side; the probe side streams through.
- When a blocking operator exceeds its memory grant, it **spills to disk automatically** and
  resumes from there. No query ever fails because of data size.
- Memory is measured in 8 KB pages tracked by a fixed buffer pool. When a query finishes, every
  page is returned to the pool immediately. No managed GC, no LOH fragmentation.

ETL-SQL's external engines (`ExternalJoinEngine`, `ExternalAggregateEngine`, etc.) already emit
`IAsyncEnumerable<Row>` — a streaming interface. The problem is the heavy SELECT orchestration path:
`SelectExecutionEngine.cs` often materializes streams into `List<Row>` immediately, then applies
later stages over the materialized list.

There is already a fast streaming path for simple SELECT statements. The performance risk is the
complex path: joins, aggregates, windows, DISTINCT, ORDER BY, OFFSET/LIMIT, QUALIFY, and combinations
of those features.

There is also already some optimizer behavior in `JoinEngine`:

- comma-join CROSS JOIN predicates can be promoted into INNER JOIN conditions,
- some single-table predicates are applied before joins,
- some post-join predicates are applied as soon as their referenced columns exist.

That is useful work, but it should be formalized into a real logical planning layer instead of
continuing to grow as one-off logic inside execution engines.

---

## v0.7.0 — Stabilization

**Scope:** Configuration tuning and GC improvements only. No engine architecture changes.  
**Goal:** Get the SLT corpus test suite to complete without hitting the memory guard.

### 1. Lower Spill Thresholds (`src/appsettings.json`)

Change:
```json
"Engine": {
  "JoinSpillThreshold":    10000,
  "WindowSpillThreshold":  10000,
  "ExternalSort": { "ChunkSize": 10000 }
}
```

Was 100 000 for all three. At 10 000 rows the external engines engage sooner, capping peak
per-query memory at roughly `10 000 rows × row_size_bytes` instead of `100 000 × row_size_bytes`.

### 2. Compacting GC in SltRunner (`tests/ETL-SQL.SqlLogicTests/SltRunner.cs`)

The current call is `GC.Collect(2, GCCollectionMode.Optimized, blocking: false)` every 500
queries. This does not compact the LOH, so committed memory never shrinks.

Replace with:
```csharp
if (_queryCount % 200 == 0)
{
    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    GC.WaitForPendingFinalizers();
}
```

- Compact LOH on every invocation (the `CompactOnce` flag resets after each collect).
- Run every 200 queries instead of 500 — a corpus of 20 000 queries wants tighter reclaim loops.
- Block until the collect finishes so the memory guard sees the true live heap, not stale pages.

### 3. Fix SLT Test Failures

Re-run the corpus with these mitigations. For any failures: identify the query, root-cause the
engine mismatch, fix or add a `skipif etlsql` marker. This is the primary v0.7.0 SLT work.

---

## v0.8.x — Streaming Pipeline and Query Planning

**Scope:** Refactor complex SELECT execution so non-blocking stages stay streaming, and add enough
query planning to avoid carrying unnecessary rows and columns through the pipeline.

**Goal:** Memory should be bounded by:

1. the largest necessary blocking operator,
2. the retained final result contract (`LastResult`, report manifest, CLI/TUI output), and
3. any intentional spill buffer or batch window.

This is stronger and more honest than saying "bounded by the largest blocking operator" alone,
because ETL-SQL still has host-facing result materialization and telemetry contracts.

### What Must Buffer vs. What Can Stream

| Stage | Buffer required? | Reason |
|---|---|---|
| Hash join — build side | Yes | Hash table needs all keys upfront |
| Hash join — probe side | **No** | Each probe row is independent |
| WHERE / HAVING filter | **No** | Stateless per-row |
| SELECT projection | **No** | Stateless per-row |
| GROUP BY aggregate | Yes | Must accumulate per-group |
| ORDER BY (no LIMIT) | Yes | Must see all rows to sort |
| ORDER BY + LIMIT N | Sometimes | Top-N heap can keep only N rows, but OFFSET, WITH TIES, TOP PERCENT, null ordering, aliases, and ordinal ORDER BY must be preserved |
| UNION ALL | **No** | Concatenate two streams |
| UNION / EXCEPT / INTERSECT | Yes | Dedup requires a hash set |
| Window functions | Yes | Per-partition frame calculation |
| Final `LastResult` / host display | Yes, bounded | CLI/TUI/tests/report hosts still need a materialized result, ideally capped by `MaxLastResultRows` |

### Core Change: `SelectExecutionEngine.cs`

Replace the current materialization pattern:
```csharp
var allRows = await joinEngine.ExecuteAsync(...).ToListAsync();
var filtered = allRows.Where(predicate).ToList();
var aggregated = await aggregateEngine.Apply(filtered).ToListAsync();
```

With a chained stream:
```csharp
IAsyncEnumerable<Row> stream = joinEngine.ExecuteAsync(...);      // streaming
stream = stream.Where(predicate);                                   // streaming (LINQ.Async)
stream = aggregateEngine.ApplyStreaming(stream);                    // buffers only per-group
var result = await MaterializeFinalResultAsync(stream);              // one bounded materialization at end
```

The final materialization must be intentional. It should update `LastResult`, `LastResultSets`,
`OnResultSet`, telemetry, report/session consumers, and tests through a single retention policy
instead of letting each caller accidentally force full materialization.

### New Helper: `AsyncEnumerableExtensions.cs`

`System.Linq.Async` (NuGet) provides `Where`, `Select`, `Take` over `IAsyncEnumerable<T>`.
One bespoke addition needed:

```csharp
// Keeps only the top N rows by comparator — O(n log N) time, O(N) memory.
public static IAsyncEnumerable<Row> TopNAsync(
    this IAsyncEnumerable<Row> source, int n, IComparer<Row> comparer);
```

ORDER BY + LIMIT N replaces `list.Sort().Take(N)` with `TopNAsync(N, comparer)`.  
UNION ALL replaces `listA.Concat(listB).ToList()` with `streamA.Concat(streamB)`.

### Query Planning Gaps to Add

Streaming alone is not enough. Without basic planning, the engine may still stream far more data
than necessary or build the wrong side of a hash join.

The optimizer should become explicit:

```text
SelectStatement
  → LogicalPlan        -- relational operators, aliases, predicates, required columns
  → OptimizedPlan      -- pushed predicates, pruned columns, simplified joins
  → PhysicalPlan       -- streaming vs blocking operators, join algorithms, spill decisions
  → ExecutionPipeline  -- IAsyncEnumerable/DataTable batches consumed by hosts
```

Do not add a broad cost-based optimizer first. Start with deterministic rule-based rewrites that
are easy to test and preserve semantics.

#### Predicate Pushdown Inside the Engine

Split `WHERE` predicates by referenced aliases:

- predicates that reference only the left source can run before the join,
- predicates that reference only the right source can run while reading the right side,
- predicates that reference both sides remain post-join,
- predicates with subqueries or non-deterministic functions need conservative handling.

Example:

```sql
SELECT *
FROM customers c
JOIN orders o ON c.Id = o.CustomerId
WHERE c.Region = 'North'
  AND o.OrderDate >= '2026-01-01'
  AND o.Amount > c.CreditLimit;
```

`c.Region = 'North'` can filter customers before the join. `o.OrderDate >= '2026-01-01'` can
filter orders before building/probing. `o.Amount > c.CreditLimit` must stay after row combination.

#### Projection Pruning

Carry only columns needed by:

- join predicates,
- WHERE/HAVING/QUALIFY predicates,
- GROUP BY keys,
- aggregate/window expressions,
- ORDER BY keys,
- final SELECT projection,
- lineage/tag metadata required by the statement.

This reduces `Row` dictionary size and clone cost. It may matter as much as streaming because
current rows can accumulate schema-backed values plus dynamic qualified aliases, and clone/qualification
work happens in hot query paths.

Projection pruning should be treated as an early performance phase, not a late polish item. Reducing
row width lowers memory, spill bytes, hash-table size, sort key payloads, and final result pressure.

#### Join Side Selection

For hash joins, choose the smaller estimated side as the build side when semantics allow it.
The current syntactic left/right shape is not always the best physical plan.

Needed inputs:

- table or batch row-count estimates where available,
- temp-table row counts,
- connector metadata estimates when cheap,
- fallback heuristics for unknown sources,
- outer-join constraints so LEFT/RIGHT/FULL semantics are preserved.

#### Multi-Join Planning

Chained joins need an explicit plan. Optimizing only the first join still allows later stages to
explode into large intermediates. Start conservatively:

1. keep declared join order for outer joins and APPLY,
2. allow inner-join reordering only when predicates are simple equality predicates,
3. choose build/probe sides per join,
4. apply single-source predicates before each join.

#### Result Retention Policy

The rewrite must not silently break callers that expect `LastResult` or `LastResultSets`.
Define one product-level contract:

- interactive hosts retain up to `MaxLastResultRows`,
- automation tracks total matched rows separately from retained rows,
- report execution may materialize the rows needed by the report manifest,
- tests can opt into full retention for exact-result assertions when row counts are small,
- large retained results should be marked capped with `IsCapped` and `TotalRowsMatched`.

Without this contract, streaming work can be undone by host display code.

This should be one of the first implementation steps. If final results are still retained
unbounded, a streaming execution pipeline can still exhaust memory after doing the hard part
correctly.

#### Memory Grants and Adaptive Spill

Row-count thresholds are too blunt for a production ETL engine. Ten thousand narrow rows and ten
thousand wide rows have very different memory footprints.

Add a small memory-grant model:

- estimate row width from schema, sampled values, or actual first batches,
- assign per-operator memory budgets,
- spill based on estimated or actual bytes, not just row count,
- record actual rows, estimated bytes, spill bytes, and spill count in profile output,
- let operators adapt when actual row width or row count is much higher than estimated.

Keep the first version simple. The goal is not a full database buffer manager; the goal is to avoid
hard-coded row thresholds being the only defense against large/wide data.

Example decision rule:

```text
estimatedBytes = estimatedRows × estimatedRowWidth
if estimatedBytes > operatorMemoryGrantBytes:
    choose external/spilling operator
else:
    choose in-memory operator
```

Over time, profile actuals can feed better defaults.

#### Row Representation Cost

Streaming reduces list copies, but each row is still a dictionary-like object and rows are cloned
and qualified often. The current `Row` type can use schema-backed arrays, which is good, but dynamic
columns are still used for qualified aliases, aggregate/window synthetic columns, and ad hoc values.
After the first streaming pass, benchmark whether further wins require:

- column ordinals for hot paths,
- lightweight projected row views,
- avoiding duplicate qualified and unqualified keys where possible,
- pooling or reusing small buffers without leaking values across rows.

### Memory Impact

| Scenario | Current | After v0.8.x |
|---|---|---|
| JOIN 100k × 100k, filter to 500 | Large joined intermediate plus later copies | Smaller build side + streaming probe + early predicates |
| ORDER BY LIMIT 10 | Full sort of result | Top-N heap when semantics permit |
| Projection over wide rows | Carries every source column | Carries only required columns |
| Wide rows with qualified aliases | Duplicate dynamic keys increase clone/hash/sort cost | Required-column plan avoids unnecessary aliases and payload |
| 20 000 SLT corpus queries | Monotonic working-set growth → guard failure | Working-set plateau after GC/spill cycles |
| Report/interactive final result | Potential full retained result | Capped retained rows plus total-row metadata |
| Wide aggregate/sort workload | Spills by row count only | Spills by estimated/actual bytes under memory grants |

### Phased Implementation Order

Do not attempt a single large rewrite. Each phase should ship behind focused tests and benchmarks.

#### Phase 0 — Measurements and Guardrails

- Add repeatable benchmarks for representative SELECT shapes:
  - simple projection/filter,
  - cross-source join,
  - hash join with selective predicates,
  - GROUP BY high/low cardinality,
  - ORDER BY LIMIT,
  - DISTINCT,
  - window + QUALIFY,
  - report query materialization.
- Capture elapsed time, allocated bytes, working set, Gen2/LOH collections, spill bytes, retained rows, and total matched rows.
- Add correctness fixtures for edge cases before changing the execution engine.
- Add a semantic matrix covering aliases, ordinal ORDER BY, NULL behavior, TOP/OFFSET/LIMIT,
  DISTINCT, grouping sets, outer joins, window frames, QUALIFY, and report materialization.

#### Phase 1 — Result Retention and Row-Width Reduction

- Define the retained-result contract for `LastResult`, `LastResultSets`, `OnResultSet`, CLI/TUI,
  ReportPlayer, Portal, tests, and SLT.
- Enforce capped retention consistently while preserving `TotalRowsMatched`.
- Add required-column analysis for SELECT, WHERE, JOIN, GROUP BY, HAVING, QUALIFY, ORDER BY,
  aggregate/window expressions, lineage, and final projection.
- Stop carrying unnecessary source columns through hot paths where semantics allow it.
- Avoid creating duplicate qualified aliases unless an expression actually needs them.

This phase comes before major streaming join work because it reduces memory pressure everywhere
and prevents final result retention from erasing streaming wins.

#### Phase 2 — Streaming Non-Blocking Stages

- Add or reuse async enumerable helpers for `Where`, `Select`, `Take`, and `Concat`.
- Stream WHERE and projection when no later blocking operator requires full materialization.
- Preserve `BatchSize`, `RowsProcessed`, `LastStatementRowsProcessed`, and `OnResultSet` behavior.
- Keep final materialization bounded and explicit.

#### Phase 3 — Top-N Sort

- Implement `TopNAsync` only for ORDER BY forms where semantics are proven:
  - no TOP PERCENT,
  - no WITH TIES initially,
  - no ambiguous alias/ordinal behavior,
  - OFFSET handled as `N + offset` retained rows, then skip offset after final ordering.
- Add tests for ASC/DESC mixes, nulls, aliases, ordinal ORDER BY, LIMIT, TOP, and OFFSET.

#### Phase 4 — Logical Query Optimizer: Predicate Pushdown and Plan Normalization

- Add expression analysis that returns referenced aliases/columns.
- Move existing join predicate-pushdown behavior toward a reusable logical optimizer instead of
  leaving it embedded only in `JoinEngine`.
- Push single-source predicates before joins.
- Keep conservative behavior for outer joins, APPLY, subqueries, and non-deterministic functions.
- Verify lineage/tag behavior still has the metadata it needs.

#### Phase 5 — Memory Grants and Adaptive Spill

- Estimate row width and operator input sizes.
- Replace or supplement row-count spill thresholds with byte-based grants.
- Add profile output for estimated bytes, actual bytes, and spill decisions.
- Keep row-count thresholds as a backstop while the byte model matures.

#### Phase 6 — Physical Query Optimizer and Streaming Join Pipeline

- Refactor join execution so the probe side streams through where possible.
- Choose smaller build side for inner hash joins.
- Preserve LEFT/RIGHT/FULL OUTER semantics.
- Keep external hash join as the fallback when either side exceeds memory thresholds.
- Add multi-join tests before enabling join reordering beyond simple inner joins.

#### Phase 7 — Aggregates, DISTINCT, and Windows

- Keep aggregates and DISTINCT explicit blocking operators.
- Make external aggregate/window paths return streams without immediate full materialization unless a downstream stage requires it.
- Use memory grants to decide in-memory vs external aggregate/window execution.

#### Phase 8 — Remove Temporary Compatibility Materialization

- Search for remaining `ToListAsync()` and `ToList()` calls in hot query paths.
- Keep intentional materialization documented with comments and tests.
- Update architecture docs and `EXPLAIN` output to show streaming vs blocking operators.

### Risk Assessment

**Medium to high if attempted as one rewrite. Medium if phased.**

The external engines already produce `IAsyncEnumerable<Row>`, which is encouraging, but the heavy
SELECT path is also where subtle SQL correctness bugs live:

- alias resolution,
- ORDER BY ordinal references,
- DISTINCT null semantics,
- TOP PERCENT and WITH TIES,
- OFFSET/LIMIT interaction,
- HAVING and QUALIFY timing,
- grouping sets,
- outer join preservation,
- window partitions and frames,
- result-set consumers that expect repeatable materialized tables.

**Caller audit required:** Any caller that consumes `LastResult`, `LastResultSets`, `OnResultSet`,
or report/session results must be checked before changing final materialization. A global search
for those members identifies the compatibility surface.

**Important limitation:** SLT coverage is necessary but not sufficient. SLT catches broad SQL
correctness regressions, but ETL-SQL also needs product-specific tests for reports, portal
execution, lineage, dialect pushdown, connector batching, capped results, and host display behavior.

### Acceptance Criteria

The rewrite is successful only when all of these are true:

- Existing fast SELECT tests still pass.
- Full xUnit suite passes without SLT by default.
- Full SLT suite passes when explicitly enabled for deployment/release validation.
- Large-query benchmarks show lower peak retained rows and lower allocated bytes.
- Working set plateaus across long sequential query runs.
- `LastResult` remains correct for small results and clearly capped for large results.
- `EXPLAIN` or profile output identifies blocking operators and spill events.
- Profile output includes row estimates, row-width estimates or actuals, and spill bytes for heavy operators.
- ReportPlayer and Portal render the same report data before and after the rewrite.

---

## Why This Is Non-Negotiable

ETL-SQL is positioned as a large-scale data-movement tool. If it cannot run a 10-table join
against 500k-row tables without exhausting RAM, it cannot compete with free tools like DuckDB,
SQLite, or a bash pipeline. The v0.8.x streaming and query-planning work is not a nice-to-have — it is the
dividing line between "interesting prototype" and "production-ready ETL platform."

The encouraging news: the architecture already has the right skeleton. The external engines stream,
the simple SELECT path already streams, and the core interfaces can carry batches. The missing piece
is not just "remove `.ToListAsync()`"; it is to make the complex SELECT orchestrator understand
which operators stream, which operators block, which columns are needed, which predicates can move,
how much memory each operator is allowed to use, when an operator should spill, and how much final
data each host is allowed to retain.

---

## Release Summary

| Version | Scope | Risk | Expected Outcome |
|---|---|---|---|
| **v0.7.0** | Threshold tuning, LOH-compacting GC, SLT failures fixed | Very low | Corpus tests complete without OOM |
| **v0.8.x** | Result-retention contract, row-width reduction, phased streaming rewrite, top-N, logical/physical optimizer, memory grants | Medium | Scalable complex SELECT execution with measured memory plateau |
