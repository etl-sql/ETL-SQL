# SET EXTERNAL_SORT_CHUNK_SIZE
Sets the number of rows per sort chunk when sort operations spill to disk.

## Syntax
```sql
SET EXTERNAL_SORT_CHUNK_SIZE = <n>;
```

## Parameters
- **n** — Rows per sort chunk. Default: 50,000.

## Example
```sql
-- Use larger chunks for a massive sort
SET EXTERNAL_SORT_CHUNK_SIZE = 100000;

SELECT * INTO #sorted_logs FROM #logs ORDER BY timestamp;
```

## Notes
- Larger chunks reduce the number of merge passes but require more memory per chunk.
- See also: `SET SORT_SPILL_THRESHOLD`, `SET EXTERNAL_HASH_PARTITIONS`.
- Default: 50,000.

## References
- [SET Commands](README.md)
