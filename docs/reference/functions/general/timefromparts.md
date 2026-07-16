# TIMEFROMPARTS

Constructs a TIME value from individual hour, minute, second, fractional, and precision components.

## Syntax

```sql
TIMEFROMPARTS(hour, minute, second, fractions, precision)
```

## Parameters

- **hour** - Hour from `0` through `23`.
- **minute** - Minute from `0` through `59`.
- **second** - Second from `0` through `59`.
- **fractions** - Fractional seconds value.
- **precision** - Decimal precision for `fractions`, from `0` through `7`.

## Returns

Returns the constructed `TIME`.

## Null Behavior

Returns `NULL` when any required component is `NULL`.

## Remarks

Out-of-range components raise an error.

## Examples

```sql
SELECT TIMEFROMPARTS(14, 30, 0, 0, 0) AS business_time;
```

```sql
SELECT TIMEFROMPARTS(14, 30, 45, 500, 3) AS precise_time;
```

## References

- [Standard Library](../standard-library.md)
- [DATETIMEFROMPARTS](datetimefromparts.md)
