# MOD
Returns the remainder of integer division.

**Category:** Math

## Syntax
```sql
MOD(dividend, divisor)
dividend % divisor
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `dividend` | `INT` / `DECIMAL` | The number to divide |
| `divisor` | `INT` / `DECIMAL` | The divisor |

## Returns
`INT` / `DECIMAL` — The remainder after dividing `dividend` by `divisor`. Returns `NULL` if `divisor` is `0`.

## Example
```sql
SELECT MOD(10, 3);    -- → 1
SELECT MOD(9, 3);     -- → 0
SELECT 10 % 3;        -- → 1  (operator form)
SELECT id, MOD(id, 2) AS is_even FROM #items;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`QUOTIENT`](QUOTIENT.md)
