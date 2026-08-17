# @@PARTITIONS_COUNT
Count of external spill partitions created during the most recently completed sort, hash-join, or aggregation operation. A value of 0 means the operation fit entirely in memory. Values greater than 0 indicate that the engine spilled to disk and split the workload across that many temporary partitions.

Set by: any statement that performs an in-memory sort, hash-join, or aggregation (SELECT with ORDER BY, GROUP BY, window functions, MERGE, etc.).
Scope: updated after each such statement — read it immediately after the statement you are profiling.

```sql
-- Check whether the last sort spilled
SELECT customer_id, SUM(amount) AS total
INTO #summary
FROM #orders
GROUP BY customer_id
ORDER BY total DESC;

IF @@PARTITIONS_COUNT > 0
BEGIN
  PRINT 'Spilled to ' + @@PARTITIONS_COUNT + ' partitions — consider increasing SET EXTERNAL_HASH_PARTITIONS or reducing the data volume.';
END;
```

References:
- [Variables and Parameters](README.md)
- [@@SORT_SPILLS](@@sort_spills.md)
- [@@TOTAL_SPILLED_BYTES](@@total_spilled_bytes.md)
- [@@PEAK_MEMORY_MB](@@peak_memory_mb.md)
