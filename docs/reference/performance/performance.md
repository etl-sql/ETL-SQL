# ETL-SQL Performance Reference

**Applies to ETL-SQL 0.18.0**

This document explains when the engine spills to disk, what each threshold controls, how to tune them, and what to expect from large-workload performance.

---

## 1. External engines and when they activate

ETL-SQL has four external engines that kick in automatically when in-memory row counts exceed configured thresholds. Below the threshold, the engine operates entirely in memory. Above it, it spills intermediate data to disk in compressed, optionally encrypted chunks.

| Operation | External engine | Activating threshold | Default |
| :--- | :--- | :--- | :--- |
| `ORDER BY` (and `DISTINCT`) | `ExternalSortEngine` | `ExternalSortChunkSize` rows per chunk | 50,000 |
| `GROUP BY` / aggregation | `ExternalAggregateEngine` | `OperatorMemoryGrantMB` MB of heap | 256 MB |
| `JOIN` (hash join) | `ExternalJoinEngine` | `JoinSpillThreshold` rows on build side | 100,000 |
| `PARTITION BY` / window | `ExternalWindowEngine` | `WindowSpillThreshold` rows per partition | 100,000 |
| `SELECT … INTO #temp` | spill-on-demand | `TempTableSpillThresholdRows` rows stored | 1,000,000 |

> These thresholds are independent. A query that does a JOIN followed by a window function can trigger both `ExternalJoinEngine` and `ExternalWindowEngine` in the same execution.

---

## 2. Configuring thresholds

### 2.1 Per-script (runtime)

Use `SET` statements to override thresholds for the current script execution only. Changes do not persist after the script ends.

```sql
-- Lower the join spill threshold to 5,000 rows (forces disk-spill for testing)
SET JOIN_SPILL_THRESHOLD = 5000;

-- Allow window partitions up to 500k rows in memory before spilling
SET WINDOW_SPILL_THRESHOLD = 500000;

-- Chunk size for external sort merge passes
SET EXTERNAL_SORT_CHUNK_SIZE = 25000;

-- Number of hash partitions for external join and aggregate engines
SET EXTERNAL_HASH_PARTITIONS = 32;

-- Memory budget per operator (MB) — applies to ExternalAggregateEngine
SET BATCHSIZE = 20000;

-- Temp table in-memory row cap before Arrow spill
SET TEMP_TABLE_SPILL_THRESHOLD = 500000;
```

### 2.2 Per-instance (appsettings.json)

Set in `src/appsettings.json` under the `Engine` key for persistent defaults across all runs on a host:

```json
{
  "Engine": {
    "BatchSize":                   10000,
    "JoinSpillThreshold":          100000,
    "ExternalHashPartitions":      16,
    "ExternalSortChunkSize":       50000,
    "WindowSpillThreshold":        100000,
    "OperatorMemoryGrantMB":       256,
    "TempTableSpillThresholdRows": 1000000
  }
}
```

The `SET` statement always overrides `appsettings.json` for the duration of the script.

---

## 3. Spill storage

Spill files are written to a temporary directory managed by `SpillStore`:

- **Session mode** (`--session <id>`): spill files live under `<SessionRoot>/<id>/spill/` and are retained between runs for checkpoint-resume workflows. They are cleaned up when the session is cleared.
- **Non-session mode**: spill files are written to `%TEMP%\ETL-SQL-Spill\<guid>\` and are deleted when the evaluator disposes.

Spill format is Arrow IPC by default (binary, columnar). JSON spill is available as a fallback via `SET SPILL_FORMAT = JSON`.

### 3.1 Encryption and compression

```sql
SET SPILL_ENCRYPTION ON;   -- AES-256-GCM, key derived from session key
SET SPILL_COMPRESSION ON;  -- GZip, applied before encryption
```

Both are `OFF` by default. Enabling compression typically reduces spill file size by 60–80% for text-heavy workloads at a small CPU cost.

---

## 4. Observing spill activity

### 4.1 Summary (end-of-run)

Run with `--perf` to see a summary table after execution:

```
╭──────────────────────────────────────────╮
│ Metric                │ Value            │
├──────────────────────────────────────────┤
│ Total Rows Processed  │ 2,450,000        │
│ Throughput (Rows/s)   │ 312,500          │
│ Approx. RAM Peak      │ 184.3 MB         │
│ Disk Spilled          │ 1,240.6 MB       │
│ Partitions Used       │ 32               │
╰──────────────────────────────────────────╯
```

"Disk Spilled" reflects `TotalSpilledBytes` across all external engines for the run.

### 4.2 Per-statement (profiling)

Enable profiling to track spill per statement:

```sql
SET PROFILE ON;

SELECT a.*, b.Revenue
INTO #Result
FROM #Orders a
JOIN #Transactions b ON a.ID = b.OrderID;

SELECT * FROM eng.profile;
```

`eng.profile` includes `SpilledBytes` per statement row.

### 4.3 Verbose/JSON mode

With `--verbose` or when running in JSON mode (`--json`), the `performance` telemetry packet includes:

```json
{
  "type": "performance",
  "metrics": {
    "spilledMb": 1240.6,
    "partitions": 32,
    "rowsProcessed": 2450000
  }
}
```

### 4.4 Inline engine messages

`ExternalWindowEngine` logs a message directly to the console when it switches a partition to deep-spill mode:

```
* DEEP-SPILL: Partition has 450,000 rows (threshold: 100,000). Processing via streaming.
```

---

## 5. Memory model

ETL-SQL does not use a global memory pool. Each external engine has its own memory budget:

- `ExternalAggregateEngine`: uses `OperatorMemoryGrantMB` as a soft cap before triggering hash-partition spill.
- `ExternalJoinEngine`: triggers when the build-side (smaller) table exceeds `JoinSpillThreshold` rows. It partitions both sides and recursively re-partitions if a partition still exceeds the threshold.
- `ExternalWindowEngine`: triggers per-partition when a partition exceeds `WindowSpillThreshold` rows. Partitions that fit in memory are processed in-memory; only oversized partitions spill.
- Single `PIVOT` operators switch to spill-backed filtered aggregation after `JoinSpillThreshold`; chained table operators retain the in-memory compatibility path. `MATCH_RECOGNIZE` remains partition-materialized and emits a warning after the same threshold, so large sources should be pre-filtered.
- `ExternalSortEngine`: divides the input into `ExternalSortChunkSize`-row chunks, sorts each chunk in memory, writes it to disk, then merges. Total disk usage is roughly `input_rows × avg_row_bytes`.

There is no global spill coordinator — if a query triggers multiple external engines simultaneously, each one manages its own spill independently.

---

## 6. Performance tuning guidance

| Situation | Recommended adjustment |
| :--- | :--- |
| Join of two very large tables | Increase `JoinSpillThreshold` if you have RAM; reduce `ExternalHashPartitions` to increase partition size and reduce merge I/O |
| Window functions on large time-series | Increase `WindowSpillThreshold` if partitions fit in memory; enable `SPILL_COMPRESSION` to reduce I/O for string-heavy rows |
| Slow `ORDER BY` on large result set | Increase `ExternalSortChunkSize` to reduce the number of merge passes |
| High memory pressure on aggregate | Decrease `OperatorMemoryGrantMB` to trigger earlier spill with smaller partition size |
| Persistent session with checkpoint-resume | Spill files survive across runs; set a large `TempTableSpillThresholdRows` to keep temp tables on-disk between labels |
| SSD vs HDD | External engines default to sequential write patterns — spill performance is primarily bottlenecked by sequential write speed, not random I/O |

---

## 7. Scale certification baseline

The `scripts/Test-ScaleCertification.ps1` script provides repeatable certification runs at four tiers:

| Tier | Row scale factor | Typical Row Count | Memory Ceiling | Target use |
| :--- | :--- | :--- | :--- | :--- |
| **Smoke** | 1× | 50k–100k rows | 1 GB | CI gate, fast feedback |
| **Standard** | 10× | 500k–1M rows | 4 GB | Pre-release validation |
| **Stress** | 100× | 5M–10M rows | 8 GB | Capacity planning |
| **Huge** | 500× | 25M–50M rows | 16 GB | Maximum volume certification |

Run certification locally:

```powershell
# Smoke (included in default pre-release validation)
.\scripts\Test-ScaleCertification.ps1 -Tier Smoke

# Standard (add -IncludeStandardScale to pre-release)
.\scripts\Test-PreRelease.ps1 -IncludeStandardScale

# Results are written to certification-results/cert-report.md and cert-report.json
```

Committed baseline results live in `certification-results/`. Compare a new run's JSON output against the baseline before tagging a release.

The billion-row operator certification remains operator-run. After
`.\scripts\Test-BillionRowCertification.ps1` completes, validate that the captured
`certification-results/billion-row-operator-certification/gate-f-report.json` belongs to the current
commit before citing billion-row performance results:

```powershell
.\scripts\Test-BillionRowEvidence.ps1

# Compare an operator-run report saved outside the checked-in baseline folder
.\scripts\Test-BillionRowEvidence.ps1 -Report .\certification-results\billion-row-candidate\gate-f-report.json `
  -Baseline .\certification-results\gate-f-1b\gate-f-report.json
```

To run the operator scenarios explicitly on a suitable machine:

```powershell
.\scripts\Test-BillionRowCertification.ps1 -Scenario ExternalSort
.\scripts\Test-BillionRowEvidence.ps1 -RequiredScenario ExternalSort

.\scripts\Test-BillionRowCertification.ps1 -Scenario ExternalJoin
.\scripts\Test-BillionRowEvidence.ps1 -RequiredScenario ExternalJoin

.\scripts\Test-BillionRowCertification.ps1 -Scenario HighCardinalityGrouping
.\scripts\Test-BillionRowEvidence.ps1 -RequiredScenario HighCardinalityGrouping

.\scripts\Test-BillionRowCertification.ps1 -Scenario EligibleWindowRowNumber
.\scripts\Test-BillionRowEvidence.ps1 -RequiredScenario EligibleWindowRowNumber
```

## References
- [User Manual](../../guides/onboarding/getting-started.md)
