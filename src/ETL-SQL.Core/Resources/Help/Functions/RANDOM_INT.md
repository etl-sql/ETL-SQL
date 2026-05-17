# RANDOM_INT
Returns a random integer within an inclusive range.

**Category:** Math

## Syntax
```sql
RANDOM_INT(min, max)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `min` | `INT` | Inclusive lower bound |
| `max` | `INT` | Inclusive upper bound |

## Returns
`INT` — A random integer where `min` ≤ result ≤ `max`.

## Example
```sql
SELECT RANDOM_INT(1, 100);      -- e.g. → 47
SELECT RANDOM_INT(1, 6) AS dice_roll;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`RAND`](RAND.md), [`RANDOM_DECIMAL`](RANDOM_DECIMAL.md)
