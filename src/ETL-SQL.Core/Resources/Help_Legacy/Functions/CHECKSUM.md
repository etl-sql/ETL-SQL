# CHECKSUM
Returns a 64-bit integer hash of one or more values. Used for efficient change detection.

**Category:** System

## Syntax
```sql
CHECKSUM(value1, value2, ...)
BINARY_CHECKSUM(value1, value2, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value1` | `ANY` | First value to include in hash |
| `value2` | `ANY` | Additional values (variadic) |

## Returns
`BIGINT` — A hash value. `BINARY_CHECKSUM` produces a binary-compatible hash.

## Remarks
- Faster than `HASHBYTES` for change detection — returns an INT, not binary.
- Not a cryptographic hash; collisions are possible. Use for CDC row matching, not security.

## Example
```sql
-- Detect changed rows
SELECT id, CHECKSUM(name, status, updated_at) AS row_hash
INTO #staging FROM source.Customers;

MERGE INTO target.Customers AS T USING #staging AS S ON T.id = S.id
WHEN MATCHED AND T.row_hash <> S.row_hash THEN UPDATE SET T.row_hash = S.row_hash;
```

## See Also
- [Standard Library — §9. Hashing & Checksums](../../../../../Docs/Reference/Standard_Library.md#9-hashing--checksums)
- Related: [`HASHBYTES`](HASHBYTES.md)
