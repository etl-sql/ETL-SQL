# SET TEMP_TABLE_SPILL_THRESHOLD
Sets the row count before a `#temp` table spills its data to disk.

## Syntax
```sql
SET TEMP_TABLE_SPILL_THRESHOLD = <n>;
```

## Parameters
- **n** — Row count threshold. Default: 1,000,000.

## Example
```sql
-- Allow larger in-memory temp tables before spilling
SET TEMP_TABLE_SPILL_THRESHOLD = 5000000;

SELECT * FROM SalesDB.dbo.AllTransactions INTO #all_txns;
```

## Notes
- Higher thresholds keep more data in memory, improving query performance for large staging tables.
- See also: `SET JOIN_SPILL_THRESHOLD`, `SET SORT_SPILL_THRESHOLD`, `SET EXTERNAL_SORT_CHUNK_SIZE`.
- Default: 1,000,000.

## References
- [SET Commands](README.md)
