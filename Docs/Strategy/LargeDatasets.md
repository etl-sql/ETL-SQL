# Large Dataset Handling — Design Spike

**Status:** Design complete — implementation pending  
**Phase:** 8A  
**Date:** 2026-04-12  

---

## 1. Problem Statement

ETL-SQL currently materializes all query results into in-memory `DataTable` objects before writing them downstream. This works well for datasets up to a few million rows. Beyond that, three problems emerge:

| Problem | Symptom | Root cause |
|---|---|---|
| OOM on large source reads | Process killed, `OutOfMemoryException` | `DataTable` holds all rows in RAM simultaneously |
| Slow aggregations on `#temp` tables | `GROUP BY` on 10M-row temp table takes 30s+ | Row-by-row LINQ over unindexed `DataTable` |
| Spill-to-disk absent | No fallback when RAM is exhausted | No overflow mechanism exists |

**Target workload:** scripts operating on 50M+ row sources with available RAM of 8–32 GB.

---

## 2. Candidate Strategies

### 2.1 Streaming Execution (Recommended for connectors)

**What:** Connectors yield rows via `IAsyncEnumerable<Row>` rather than buffering the full result into a `DataTable`. The Evaluator pipeline processes row-by-row where the statement type allows it.

**Already partially implemented:** `IDataSource.ReadBatches()` returns `IAsyncEnumerable<DataTable>` in batches. The bottleneck is that batch results are then merged into a single `DataTable` at the handler level.

**Change required:**
- `SelectStatementHandler` must propagate batches downstream rather than merging them.
- `INSERT INTO ... SELECT` handlers must write batches, not accumulate.
- Statements that require a full scan before producing output (ORDER BY without LIMIT, scalar aggregations) will always require full materialization — document this as a known limitation.

**Risk:** Low. Batching already exists; this is a propagation change, not a new mechanism.

### 2.2 Chunked Processing for `FOR` Loops (Recommended)

**What:** `FOR @row IN (SELECT ...)` should push pagination (`OFFSET`/`FETCH`) down to the source connector when the source supports it, instead of loading all rows and iterating in-process.

**Change required:**
- `ForStatementHandler` detects if the source is a SQL connector that supports `OFFSET/FETCH`.
- If yes: re-issues the source query with `OFFSET @page * @batchSize FETCH NEXT @batchSize ROWS ONLY` per iteration batch.
- If no (flat file, in-memory): current behavior unchanged.

**Risk:** Medium. Requires pushdown detection logic and dialect-specific SQL generation.

### 2.3 Spill-to-Disk for `#temp` Tables (Recommended)

**What:** When a `#temp` table exceeds a configurable row threshold (`TempTable:SpillThresholdRows`, default 1,000,000), overflow pages are serialized to disk (GZip-compressed Parquet or newline-delimited JSON) instead of held in RAM.

**Design:**
- `TempTableInfo` gains a `SpillStore` field (nullable `string` file path).
- When `DataTable.AddRow()` exceeds the threshold, a background task flushes the overflow to a temp Parquet file and clears the in-RAM rows.
- Reads from a spilled `#temp` table transparently merge the in-memory pages with the on-disk pages.
- On `DROP TABLE` or session end, the spill files are deleted.

**Risk:** High. Transparent read merging is complex. Implement only after 2.1 and 2.2 are in production.

### 2.4 Columnar Format for `#temp` Tables (Future)

**What:** Replace `DataTable` (row-oriented, boxed `object[]`) with Apache Arrow columnar format for temp table storage.

**Benefits:** 10–50× faster for aggregation-heavy workloads; dramatically lower memory for numeric columns.

**Risk:** Very high. Requires replacing the core data representation used by all 40+ statement handlers. Not recommended until the other strategies have been validated. Scope this as a separate architectural migration project.

---

## 3. Recommended Implementation Order

| Priority | Strategy | Phase | Expected effort |
|---|---|---|---|
| 1 | **2.1 Streaming batch propagation** | 8A-impl | Medium |
| 2 | **2.3 Spill-to-disk for #temp** | 8A-impl | High |
| 3 | **2.2 Chunked FOR loop pushdown** | 8A-impl | Medium |
| 4 | **2.4 Arrow columnar format** | Future | Very High |

---

## 4. Detailed Design: Streaming Batch Propagation (Priority 1)

### 4.1 Current Architecture

```
IDataSource.ReadBatches()  →  IAsyncEnumerable<DataTable> (batched ✓)
SelectStatementHandler     →  Merges ALL batches into one DataTable  ✗
INSERT INTO handler        →  Writes batched IAsyncEnumerable ✓
```

### 4.2 Target Architecture

```
IDataSource.ReadBatches()  →  IAsyncEnumerable<DataTable> (batched ✓)
SelectStatementHandler     →  Streams batches to destination without merging ✓
#temp table writes         →  WriteBatches() per batch ✓
context.LastResult         →  Remains a single DataTable for REPL display (cap at preview limit)
```

### 4.3 Key invariant

`context.LastResult` (the result shown to the user or returned to the REPL) always remains a capped `DataTable`. For streaming selects with no `INTO` clause, the handler writes the first `N` rows (configurable `Display:MaxResultRows`, default 10,000) to `context.LastResult` and discards the rest after logging a "results truncated" message.

### 4.4 New configuration keys

```json
"Execution": {
  "BatchSize": 10000,
  "Display": {
    "MaxResultRows": 10000
  },
  "TempTable": {
    "SpillThresholdRows": 1000000,
    "SpillDirectory": ""
  }
}
```

---

## 5. Detailed Design: Spill-to-Disk (Priority 2)

### 5.1 Spill file format

**Primary:** Parquet (compressed by default). Requires `ETL-SQL.Connectors.Parquet` (already in the connector library).

**Fallback:** GZip-compressed newline-delimited JSON (`.ndjson.gz`). Used when the Parquet connector is unavailable or for columns with complex types that Parquet cannot serialize.

### 5.2 SpillStore lifecycle

```
AddRow() called on DataTable
  → in-memory row count > SpillThresholdRows
  → flush in-memory rows to SpillStore file (append mode)
  → clear in-memory rows (keep schema)
  → continue accepting new rows into fresh in-memory buffer

ReadRows() on a spilled DataTable
  → read spill file pages, yield in order
  → then yield in-memory buffer rows (the most recent partial batch)
```

### 5.3 Spill directory

Default: `%TEMP%\etlsql-spill\{sessionId}\` (Windows) / `/tmp/etlsql-spill/{sessionId}/` (Linux).

Cleaned on:
- Explicit `DROP TABLE` statement
- Normal session end (Evaluator.Dispose / ExecutionSession cleanup)
- Service startup (scan for orphaned spill directories older than 24h)

---

## 6. Profiling Targets

Before implementing, profile against these benchmarks to establish before/after numbers:

| Test | Script | Expected bottleneck |
|---|---|---|
| Large flat file read | `SELECT * INTO #t FROM FLATFILE('50m-rows.csv')` | `FlatFileDataSource.ReadBatches()` |
| In-memory aggregation | `SELECT Region, SUM(Revenue) FROM #t GROUP BY Region` | `AggregateEngine`, `DataTable` scan |
| Cross-join | `SELECT a.*, b.* FROM #a JOIN #b ON a.id = b.id` | `JoinEngine`, RAM |
| Sorted export | `SELECT * FROM #t ORDER BY Date INTO ...` | `ExternalSortEngine` |

Run each test at 1M, 10M, 50M rows. Record: peak RAM (Process.WorkingSet64), wall time, rows/sec.

Findings go in `Docs/LargeDatasets-Profiling.md` after measurement.

---

## 7. Non-Goals

- Real-time streaming data (Kafka, CDC) — out of scope for this phase.
- Changing the public `IDataSource` contract in a breaking way.
- Removing `DataTable` entirely — too broad; Arrow migration is a separate future project.
- Distributed execution — each script runs on one node.

---

## 8. Acceptance Criteria

After Priority 1 and Priority 2 are implemented:

- [ ] `SELECT * INTO #SalesData FROM FactSales` (50M rows) completes without OOM on an 8 GB VM.
- [ ] Peak RAM during that test is < 2× the `BatchSize × RowSizeBytes` (i.e., not the full dataset).
- [ ] `SELECT Region, SUM(Revenue) FROM #SalesData GROUP BY Region` on the 50M-row spilled table returns correct results.
- [ ] `SHOW TABLES` and `DROP TABLE #SalesData` work correctly on spilled tables.
- [ ] Spill files are cleaned up on session end.
- [ ] All existing tests continue to pass — no regression from the streaming changes.
