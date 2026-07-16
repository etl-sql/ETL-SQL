# BITSHIFTLEFT
Performs a bitwise left shift on an integer.

**Category:** Math / Bitwise

## Syntax
```sql
BITSHIFTLEFT(a, n)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The integer value to shift |
| `n` | `INT` | The number of bits to shift left |

## Returns
`BIGINT` — The shifted integer. Returns `NULL` if either argument is `NULL`.

## Example
```sql
SELECT BITSHIFTLEFT(4, 2);  -- → 16 (binary: 0100 << 2 = 10000)
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../guides/getting-started.md#54-bitwise)
- Related: [`BITSHIFTRIGHT`](bitshiftright.md)
