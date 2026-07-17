# RANDOM_DECIMAL

Returns a random DECIMAL within an inclusive range.

## Syntax

```sql
RANDOM_DECIMAL(min, max)
```

## Parameters

- **min** - Inclusive lower bound.
- **max** - Inclusive upper bound.

## Returns

Returns a random `DECIMAL` value where `min <= result <= max`.

## Null Behavior

Returns `NULL` when either bound is `NULL`.

## Remarks

Use this for generated sample data, randomized tests, and simulation scripts. Do not use it for cryptographic randomness.

## Examples

```sql
SELECT RANDOM_DECIMAL(0.0, 1.0) AS sample_fraction;
```

```sql
SELECT RANDOM_DECIMAL(9.99, 99.99) AS test_price;
```

## References

- [Functions](../README.md)
- [RAND](../math/rand.md)
- [RANDOM_INT](random_int.md)
