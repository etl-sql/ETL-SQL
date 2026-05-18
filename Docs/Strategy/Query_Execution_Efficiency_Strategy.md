# Strategy: ETL-SQL Query Execution Efficiency
### v0.7.0 Mitigations → v0.8.x Streaming and Query Planning

**Status:** Active performance strategy  
**Intent:** Stabilize large-query execution first, then reduce materialization and add a small optimizer in phases.  
**Honest summary:** This work will primarily improve memory scalability and long-run stability. It will often improve elapsed query time by reducing allocation and GC pressure, but small queries may see neutral or slightly worse runtime if streaming overhead is added without care.

## The Problem

ETL-SQL's query execution engine uses a **full-materialization model**: every stage of a SQL
pipeline (join, filter, aggregate, sort, project) buffers the entire intermediate result set into
a `List<Row>` before passing it to the next stage. A 5-stage query creates 5 full copies of data
in memory simultaneously.

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
current rows are dictionary-heavy and qualified-column cloning is expensive.

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

#### Row Representation Cost

Streaming reduces list copies, but each row is still a dictionary-like object and rows are cloned
and qualified often. After the first streaming pass, benchmark whether further wins require:

- column ordinals for hot paths,
- lightweight projected row views,
- avoiding duplicate qualified and unqualified keys where possible,
- pooling or reusing small buffers without leaking values across rows.

### Memory Impact

| Scenario | Current | After v0.8.0 |
|---|---|---|
| JOIN 100k × 100k, filter to 500 | Large joined intermediate plus later copies | Smaller build side + streaming probe + early predicates |
| ORDER BY LIMIT 10 | Full sort of result | Top-N heap when semantics permit |
| Projection over wide rows | Carries every source column | Carries only required columns |
| 20 000 SLT corpus queries | Monotonic working-set growth → guard failure | Working-set plateau after GC/spill cycles |
| Report/interactive final result | Potential full retained result | Capped retained rows plus total-row metadata |

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

#### Phase 1 — Streaming Non-Blocking Stages

- Add or reuse async enumerable helpers for `Where`, `Select`, `Take`, and `Concat`.
- Stream WHERE and projection when no later blocking operator requires full materialization.
- Preserve `BatchSize`, `RowsProcessed`, `LastStatementRowsProcessed`, and `OnResultSet` behavior.
- Keep final materialization bounded and explicit.

#### Phase 2 — Top-N Sort

- Implement `TopNAsync` only for ORDER BY forms where semantics are proven:
  - no TOP PERCENT,
  - no WITH TIES initially,
  - no ambiguous alias/ordinal behavior,
  - OFFSET handled as `N + offset` retained rows, then skip offset after final ordering.
- Add tests for ASC/DESC mixes, nulls, aliases, ordinal ORDER BY, LIMIT, TOP, and OFFSET.

#### Phase 3 — Logical Query Optimizer: Predicate Pushdown and Projection Pruning

- Add expression analysis that returns referenced aliases/columns.
- Push single-source predicates before joins.
- Build a required-column set before reading/cloning rows.
- Verify lineage/tag behavior still has the metadata it needs.

#### Phase 4 — Physical Query Optimizer and Streaming Join Pipeline

- Refactor join execution so the probe side streams through where possible.
- Choose smaller build side for inner hash joins.
- Preserve LEFT/RIGHT/FULL OUTER semantics.
- Keep external hash join as the fallback when either side exceeds memory thresholds.
- Add multi-join tests before enabling join reordering beyond simple inner joins.

#### Phase 5 — Aggregates, DISTINCT, Windows, and Result Retention

- Keep aggregates and DISTINCT explicit blocking operators.
- Make external aggregate/window paths return streams without immediate full materialization unless a downstream stage requires it.
- Define and enforce result-retention behavior for CLI, TUI, ReportPlayer, ReportPortal, tests, and SLT.

#### Phase 6 — Remove Temporary Compatibility Materialization

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
- ReportPlayer and ReportPortal render the same report data before and after the rewrite.

---

## Why This Is Non-Negotiable

ETL-SQL is positioned as a large-scale data-movement tool. If it cannot run a 10-table join
against 500k-row tables without exhausting RAM, it cannot compete with free tools like DuckDB,
SQLite, or a bash pipeline. The v0.8.0 streaming rewrite is not a nice-to-have — it is the
dividing line between "interesting prototype" and "production-ready ETL platform."

The encouraging news: the architecture already has the right skeleton. The external engines stream,
the simple SELECT path already streams, and the core interfaces can carry batches. The missing piece
is not just "remove `.ToListAsync()`"; it is to make the complex SELECT orchestrator understand
which operators stream, which operators block, which columns are needed, which predicates can move,
and how much final data each host is allowed to retain.

---

## Release Summary

| Version | Scope | Risk | Expected Outcome |
|---|---|---|---|
| **v0.7.0** | Threshold tuning, LOH-compacting GC, SLT failures fixed | Very low | Corpus tests complete without OOM |
| **v0.8.x** | Phased streaming rewrite, top-N, predicate pushdown, projection pruning, result-retention contract | Medium | Scalable complex SELECT execution with measured memory plateau |
