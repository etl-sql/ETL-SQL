# SET JOIN_SPILL_THRESHOLD
Sets the row count before a hash join spills intermediate results to disk.

## Syntax
```sql
SET JOIN_SPILL_THRESHOLD = <n>;
```

## Parameters
- **n** — Row count threshold. Default: 100,000.

## Example
```sql
-- Raise spill threshold before a known large join
SET JOIN_SPILL_THRESHOLD = 500000;

SELECT o.order_id, c.name, o.amount
FROM #orders o
JOIN #customers c ON o.customer_id = c.id;
```

## Notes
- Higher thresholds keep more data in memory and can improve join performance for large tables, but increase memory pressure.
- Corresponding `appsettings.json` key: `Engine:JoinSpillThreshold`.
- See also: `SET SORT_SPILL_THRESHOLD`, `SET WINDOW_SPILL_THRESHOLD`, `SET TEMP_TABLE_SPILL_THRESHOLD`.
- Default: 100,000.

## References
- [SET Commands](README.md)
