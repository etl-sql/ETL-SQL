# BINARY_CHECKSUM

Returns a binary-sensitive checksum for one or more values.

## Syntax

```sql
BINARY_CHECKSUM(value [, ...])
```

## Parameters

- **value** - One or more values to include in the checksum.

## Returns

Returns an integer checksum value.

## Null Behavior

`NULL` values participate in the checksum using ETL-SQL checksum rules.

## Remarks

- `BINARY_CHECKSUM` is useful for change detection when case and binary representation matter.
- Do not use checksums for cryptographic integrity or security decisions. Use [`HASHBYTES`](hashbytes.md) for cryptographic hashes.
- Checksum collisions are possible.

## Examples

```sql
SELECT BINARY_CHECKSUM(customer_id, name, email) AS row_checksum
FROM #customers;
```

```sql
SELECT *
FROM #staging
WHERE BINARY_CHECKSUM(name) <> BINARY_CHECKSUM(previous_name);
```

## References

- [Functions](../README.md)
- [CHECKSUM](checksum.md)
- [HASHBYTES](hashbytes.md)
