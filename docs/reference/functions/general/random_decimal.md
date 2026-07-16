# RANDOM_DECIMAL
Returns a random DECIMAL within an inclusive range.

**Category:** Math

## Syntax
```sql
RANDOM_DECIMAL(min, max)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `min` | `DECIMAL` | Inclusive lower bound |
| `max` | `DECIMAL` | Inclusive upper bound |

## Returns
`DECIMAL` — A random decimal number where `min` ≤ result ≤ `max`.

## Example
```sql
SELECT RANDOM_DECIMAL(0.0, 1.0);        -- e.g. → 0.6234
SELECT RANDOM_DECIMAL(9.99, 99.99) AS test_price;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`RAND`](../math/rand.md), [`RANDOM_INT`](random_int.md)
