# BITOR
Performs a bitwise OR operation on two integers.

**Category:** Math / Bitwise

## Syntax
```sql
BITOR(a, b)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The first integer value |
| `b` | `INT` / `BIGINT` | The second integer value |

## Returns
`BIGINT` — The bitwise OR result. Returns `NULL` if either argument is `NULL`.

## Example
```sql
SELECT BITOR(12, 9);    -- → 13 (binary: 1100 | 1001 = 1101)
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../../../Docs/Reference/Standard_Library.md#54-bitwise)
- Related: [`BITAND`](BITAND.md), [`BITXOR`](BITXOR.md), [`BITNOT`](BITNOT.md)
