# BITSHIFTRIGHT
Performs a bitwise right shift on an integer.

**Category:** Math / Bitwise

## Syntax
```sql
BITSHIFTRIGHT(a, n)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The integer value to shift |
| `n` | `INT` | The number of bits to shift right |

## Returns
`BIGINT` — The shifted integer. Returns `NULL` if either argument is `NULL`.

## Example
```sql
SELECT BITSHIFTRIGHT(16, 2); -- → 4 (binary: 10000 >> 2 = 0100)
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../../../Docs/Reference/Standard_Library.md#54-bitwise)
- Related: [`BITSHIFTLEFT`](BITSHIFTLEFT.md)
