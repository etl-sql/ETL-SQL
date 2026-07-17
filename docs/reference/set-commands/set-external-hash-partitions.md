# SET EXTERNAL_HASH_PARTITIONS
Sets the number of partitions used for spilled hash operations.

## Syntax
```sql
SET EXTERNAL_HASH_PARTITIONS = <n>;
```

## Parameters
- **n** — Number of partitions. Default: 32.

## Example
```sql
-- Increase partitions for a very large hash join
SET EXTERNAL_HASH_PARTITIONS = 64;

SELECT a.*, b.value
FROM #large_left a
JOIN #large_right b ON a.key = b.key;
```

## Notes
- More partitions can reduce peak memory per partition during spill but increase I/O overhead.
- See also: `SET EXTERNAL_SORT_CHUNK_SIZE`, `SET JOIN_SPILL_THRESHOLD`.
- Default: 32.

## References
- [SET Commands](README.md)
