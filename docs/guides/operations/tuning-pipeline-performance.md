# Tuning Pipeline Performance and Profiling

ETL-SQL is optimized for high-throughput batch processing and in-memory transformations. You can fine-tune streaming buffer sizes, inspect execution phase metrics, and profile individual SQL statement timings to optimize slow scripts.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. Batch Size Tuning (`--batch-size`)

The `--batch-size` flag controls how many rows are buffered in memory per streaming chunk (default: `10,000` rows).

```bash
# Narrow rows with fast disk/network I/O (increase batch size for throughput)
etl-sql run ingest_simple_csv.etlsql --batch-size 50000

# Wide rows with large text/JSON blobs (decrease batch size to conserve RAM)
etl-sql run ingest_wide_payloads.etlsql --batch-size 2000
```

---

## 2. Performance Breakdown (`--perf`)

Pass `--perf` on the command line to display execution phase timings, memory peaks, and throughput statistics upon script completion:

```bash
etl-sql run nightly_load.etlsql --perf
```

### Sample Performance Output

```text
======================================================================
  ETL-SQL Performance Profile
======================================================================
  Lexer / Parsing:          12 ms
  Script Execution:       1,842 ms
  Total Rows Processed: 500,000 rows
  Throughput:           271,444 rows/sec
  Peak Memory (RAM):         48 MB
  Spill Storage:           0 MB (In-memory aggregate)
======================================================================
```

---

## 3. Statement-Level Profiling (`SET PROFILING ON`)

To identify slow queries or bottlenecks within a script, enable statement profiling and inspect the `eng.profile` catalog view.

```sql
SET PROFILING ON;

-- Stage 1: Extract
SELECT OrderId, Region, Amount 
INTO #staged_orders 
FROM remote_db.Orders;

-- Stage 2: Aggregate
SELECT Region, SUM(Amount) AS TotalRevenue
INTO #summary
FROM #staged_orders
GROUP BY Region;

-- Query performance timings for all preceding statements
SELECT 
    StatementIndex,
    StatementType,
    DurationMs,
    RowsAffected
FROM eng.profile
ORDER BY DurationMs DESC;

SET PROFILING OFF;
```

---

## Common Pitfalls

- **Unnecessarily large batch sizes**: Setting `--batch-size 500000` on wide rows (e.g. 50+ columns containing large strings) can cause sudden memory spikes and trigger engine disk spilling.

---

## Related Topics

- [Staged vs. Streaming Ingestion](../pipelines/staged-vs-streaming-ingestion.md) — Ingestion architecture.
- [Configuring Script Logging](configuring-script-logging.md) — Output logging options.
- [CLI Reference](../../reference/cli/README.md) — Complete CLI options.
