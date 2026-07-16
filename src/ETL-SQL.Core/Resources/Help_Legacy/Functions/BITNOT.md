# BITNOT
Performs a bitwise NOT (complement) operation on an integer.

**Category:** Math / Bitwise

## Syntax
```sql
BITNOT(a)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `a` | `INT` / `BIGINT` | The integer value |

## Returns
`BIGINT` — The bitwise NOT result. Returns `NULL` if the argument is `NULL`.

## Example
```sql
SELECT BITNOT(0);       -- → -1
```

## See Also
- [Standard Library — §5.4 Bitwise](../../../../../Docs/Reference/Standard_Library.md#54-bitwise)
- Related: [`BITAND`](BITAND.md), [`BITOR`](BITOR.md), [`BITXOR`](BITXOR.md)
