# SET BATCHSIZE
Sets the number of rows per remote fetch batch for `SELECT ... FROM connection`.

## Syntax
```sql
SET BATCHSIZE = <n>;
```

## Parameters
- **n** — Number of rows per batch. Default: 10,000.

## Example
```sql
-- Increase batch size for a large remote fetch
SET BATCHSIZE = 50000;
SELECT * FROM SalesDB.dbo.Orders INTO #orders;

-- Reset to default
SET BATCHSIZE = 10000;
```

## Notes
- Larger batch sizes can improve throughput for large result sets but increase memory pressure.
- Smaller batch sizes reduce memory usage but increase round-trip overhead.
- Default: 10,000.

## References
- [SET Commands](README.md)
