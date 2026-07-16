# CHECKSUM

Returns a 64-bit integer hash of one or more values. Used for efficient change detection.

## Syntax

```sql
CHECKSUM(value1, value2, ...)
BINARY_CHECKSUM(value1, value2, ...)
```

## Parameters

- **value1** - First value to include in the checksum.
- **value2** - Additional values to include.
- **...** - Additional values.

## Returns

Returns a `BIGINT` checksum value.

## Null Behavior

`NULL` inputs participate in checksum calculation according to engine checksum semantics.

## Remarks

- `BINARY_CHECKSUM` produces a binary-compatible checksum variant.
- Faster than `HASHBYTES` for change detection.
- Not a cryptographic hash; collisions are possible. Use for CDC row matching, not security.

## Examples

```sql
SELECT id, CHECKSUM(name, status, updated_at) AS row_hash
INTO #staging
FROM source.Customers;
```

```sql
MERGE INTO target.Customers AS T
USING #staging AS S ON T.id = S.id
WHEN MATCHED AND T.row_hash <> S.row_hash THEN
    UPDATE SET T.row_hash = S.row_hash;
```

## References

- [Standard Library](../standard-library.md)
- [HASHBYTES](hashbytes.md)
