# RAND
Returns a pseudo-random FLOAT in the range [0.0, 1.0).

**Category:** Math

## Syntax
```sql
RAND()
RAND(seed)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `seed` | `INT` | Optional: seed value for reproducible sequence |

## Returns
`FLOAT` — A random number ≥ 0.0 and < 1.0.

## Remarks
- Without a seed, each call returns a different value. With a seed, the same seed always starts the same sequence.
- To generate a random integer in a range: `FLOOR(RAND() * (max - min + 1)) + min`.
- See also [`RANDOM_INT`](../general/random_int.md) and [`RANDOM_DECIMAL`](../general/random_decimal.md) for range-bounded shortcuts.

## Example
```sql
SELECT RAND();             -- e.g. → 0.7342...
SELECT RAND(42);           -- reproducible starting value
SELECT FLOOR(RAND() * 100) AS random_pct;   -- 0–99
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`RANDOM_INT`](../general/random_int.md), [`RANDOM_DECIMAL`](../general/random_decimal.md), [`RANDOM`](../general/random.md)
