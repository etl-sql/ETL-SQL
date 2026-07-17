# SET FOREACH_PAGE_SIZE
Sets the batch size when `FOREACH` iterates over a `#temp` table.

## Syntax
```sql
SET FOREACH_PAGE_SIZE = <n>;
```

## Parameters
- **n** — Rows per iteration batch.

## Example
```sql
-- Process temp table in larger batches
SET FOREACH_PAGE_SIZE = 5000;

FOREACH @row IN #customers
BEGIN
    PRINT @row.name;
END;
```

## Notes
- Controls the internal batching of FOREACH iteration over `#temp` tables.
- Tuning this can improve performance for large iteration workloads.

## References
- [SET Commands](README.md)
