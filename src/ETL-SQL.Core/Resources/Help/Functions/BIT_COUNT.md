# BIT_COUNT
Returns the number of set bits (popcount) in the binary representation of an integer.

**Category:** Math / Bitwise

## Syntax
```sql
BIT_COUNT(a)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The integer value |

## Returns
`INT` — The number of bits set to 1. Returns `NULL` if the argument is `NULL`.

## Example
```sql
SELECT BIT_COUNT(9);    -- → 2 (binary: 1001 contains two 1s)
SELECT BIT_COUNT(-1);   -- → 64 (binary: all 64 bits set in two's complement)
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../../../Docs/Reference/Standard_Library.md#54-bitwise)
- Related: [`BITAND`](BITAND.md), [`BITOR`](BITOR.md)
