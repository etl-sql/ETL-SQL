# RAND

Returns a pseudo-random FLOAT in the range [0.0, 1.0).

## Syntax

```sql
RAND()
RAND(seed)
```

## Parameters

- **seed** - Optional integer seed value for a reproducible sequence.

## Returns

Returns a `FLOAT` greater than or equal to `0.0` and less than `1.0`.

## Null Behavior

When `seed` is `NULL`, behavior is the same as calling `RAND()` without a seed.

## Remarks

- Without a seed, each call returns a different value. With a seed, the same seed always starts the same sequence.
- To generate a random integer in a range: `FLOOR(RAND() * (max - min + 1)) + min`.
- See also [`RANDOM_INT`](../random-guid/random_int.md) and [`RANDOM_DECIMAL`](../random-guid/random_decimal.md) for range-bounded shortcuts.

## Examples

```sql
SELECT RAND() AS random_value;
```

```sql
SELECT FLOOR(RAND() * 100) AS random_percent;
```

## References

- [Standard Library](../standard-library.md)
- [RANDOM_INT](../random-guid/random_int.md)
- [RANDOM_DECIMAL](../random-guid/random_decimal.md)
- [RANDOM](../random-guid/random.md)
