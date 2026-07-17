# RANDOM_INT

Returns a random integer within an inclusive range.

## Syntax

```sql
RANDOM_INT(min, max)
```

## Parameters

- **min** - Inclusive lower bound.
- **max** - Inclusive upper bound.

## Returns

Returns a random `INT` value where `min <= result <= max`.

## Null Behavior

Returns `NULL` when either bound is `NULL`.

## Remarks

Use this for generated sample data, randomized tests, and simulation scripts. Do not use it for cryptographic randomness.

## Examples

```sql
SELECT RANDOM_INT(1, 100) AS sample_value;
```

```sql
SELECT RANDOM_INT(1, 6) AS dice_roll;
```

## References

- [Functions](../README.md)
- [RAND](../math/rand.md)
- [RANDOM_DECIMAL](random_decimal.md)
