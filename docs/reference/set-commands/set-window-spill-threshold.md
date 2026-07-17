# SET WINDOW_SPILL_THRESHOLD
Sets the row count before window function operations spill intermediate results to disk.

## Syntax
```sql
SET WINDOW_SPILL_THRESHOLD = <n>;
```

## Parameters
- **n** — Row count threshold. Default: 100,000.

## Example
```sql
-- Raise threshold for a heavy window function query
SET WINDOW_SPILL_THRESHOLD = 300000;

SELECT *, ROW_NUMBER() OVER (PARTITION BY region ORDER BY amount DESC) AS rn
FROM #sales;
```

## Notes
- See also: `SET JOIN_SPILL_THRESHOLD`, `SET SORT_SPILL_THRESHOLD`, `SET TEMP_TABLE_SPILL_THRESHOLD`.
- Default: 100,000.

## References
- [SET Commands](README.md)
