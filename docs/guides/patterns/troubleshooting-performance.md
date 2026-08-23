# Troubleshooting: Pipeline & Query Performance

This guide covers common bottlenecks, slow cross-source joins, large dataset streaming techniques, and memory spill optimization.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. Slow Cross-Source Joins (Database + Flat File)

### Problem
A query joining a remote SQL database table with a local CSV or Parquet file takes excessive time or exhausts available RAM.

### Cause
Cross-source joins require the ETL-SQL engine to pull rows from both systems into memory before executing the join algorithm. Joining an entire remote table with a file causes unnecessary network transfers.

### Solution: Pre-filter Remote Data
Push filtering conditions down to the remote database first, staging only the needed subset into an in-memory `#temp` table before joining:

```sql
-- ❌ SLOW: Pulls entire 10-million row customer table across the network
SELECT C.CustomerId, C.Name, O.Amount
FROM remote_db.Customers AS C
JOIN flatfile_src.Orders AS O ON C.CustomerId = O.CustomerId;

-- ✓ FAST: Pre-filter on the SQL side first
SELECT CustomerId, Name
INTO #active_customers
FROM remote_db.Customers
WHERE Status = 'Active' AND Region = 'North';

SELECT C.CustomerId, C.Name, O.Amount
FROM #active_customers AS C
JOIN flatfile_src.Orders AS O ON C.CustomerId = O.CustomerId;
```

---

## 2. Ingesting Large Datasets (>100M Rows) Without Memory Exhaustion

### Problem
Executing `SELECT * INTO #temp FROM huge_source` runs out of memory or causes severe disk thrashing.

### Cause
`SELECT ... INTO #temp` materializes the full result set into the engine's memory workspace.

### Solution: Direct Streaming Ingestion or `BULK INSERT`
Use direct streaming or `BULK INSERT` with a fixed batch size to stream rows in O(1) memory:

```sql
-- Stream large CSV in 50,000-row chunks
BULK INSERT dest_db.FactLogs
FROM 'C:\Data\huge_access_log.csv'
WITH (
    FORMAT    = 'CSV',
    FIRSTROW  = 2,
    BATCHSIZE = 50000
);
```

---

## 3. Acceleration with In-Memory Indexes

### Problem
Repeated joins or lookups against large `#temp` tables slow down downstream transformation stages.

### Solution
Create explicit indexes on `#temp` join keys:

```sql
SELECT OrderId, CustomerId, OrderDate, Amount 
INTO #orders 
FROM source_db.Orders;

-- Index the lookup key
CREATE INDEX idx_orders_customer ON #orders(CustomerId);

-- Downstream joins will utilize the in-memory hash/B-tree index
SELECT O.*, C.CustomerName
INTO #enriched
FROM #orders AS O
JOIN #customers AS C ON O.CustomerId = C.CustomerId;
```

---

## Related Topics

- [Tuning Pipeline Performance](../operations/tuning-pipeline-performance.md) — CLI flags and profiling.
- [Staged vs. Streaming Ingestion](../pipelines/staged-vs-streaming-ingestion.md) — Ingestion mechanics.
- [BULK INSERT Reference](../../reference/file-operations/bulk-insert.md) — Fast streaming load.
