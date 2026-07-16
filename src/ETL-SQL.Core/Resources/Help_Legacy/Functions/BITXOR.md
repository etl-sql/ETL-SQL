# BITXOR
Performs a bitwise XOR (exclusive OR) operation on two integers.

**Category:** Math / Bitwise

## Syntax
```sql
BITXOR(a, b)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The first integer value |
| `b` | `INT` / `BIGINT` | The second integer value |

## Returns
`BIGINT` — The bitwise XOR result. Returns `NULL` if either argument is `NULL`.

## Example
```sql
SELECT BITXOR(12, 9);   -- → 5 (binary: 1100 ^ 1001 = 0101)
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../../../Docs/Reference/Standard_Library.md#54-bitwise)
- Related: [`BITAND`](BITAND.md), [`BITOR`](BITOR.md), [`BITNOT`](BITNOT.md)
