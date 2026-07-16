# BITAND
Performs a bitwise AND operation on two integers.

**Category:** Math / Bitwise

## Syntax
```sql
BITAND(a, b)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The first integer value |
| `b` | `INT` / `BIGINT` | The second integer value |

## Returns
`BIGINT` — The bitwise AND result. Returns `NULL` if either argument is `NULL`.

## Example
```sql
SELECT BITAND(12, 9);   -- → 8 (binary: 1100 & 1001 = 1000)
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../guides/getting-started.md#54-bitwise)
- Related: [`BITOR`](bitor.md), [`BITXOR`](bitxor.md), [`BITNOT`](bitnot.md)
