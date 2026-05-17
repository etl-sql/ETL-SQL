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
- See also [`RANDOM_INT`](RANDOM_INT.md) and [`RANDOM_DECIMAL`](RANDOM_DECIMAL.md) for range-bounded shortcuts.

## Example
```sql
SELECT RAND();             -- e.g. → 0.7342...
SELECT RAND(42);           -- reproducible starting value
SELECT FLOOR(RAND() * 100) AS random_pct;   -- 0–99
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`RANDOM_INT`](RANDOM_INT.md), [`RANDOM_DECIMAL`](RANDOM_DECIMAL.md), [`RANDOM`](RANDOM.md)
