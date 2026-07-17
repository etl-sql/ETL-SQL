# DATETIMEFROMPARTS

Constructs a DATETIME value from individual year, month, day, hour, minute, second, and millisecond components.

## Syntax

```sql
DATETIMEFROMPARTS(year, month, day, hour, minute, second, millisecond)
```

## Parameters

- **year** - Four-digit year.
- **month** - Month number from `1` through `12`.
- **day** - Day of month.
- **hour** - Hour from `0` through `23`.
- **minute** - Minute from `0` through `59`.
- **second** - Second from `0` through `59`.
- **millisecond** - Millisecond from `0` through `999`.

## Returns

Returns the constructed `DATETIME`.

## Null Behavior

Returns `NULL` when any required component is `NULL`.

## Remarks

Out-of-range components raise an error.

## Examples

```sql
SELECT DATETIMEFROMPARTS(2026, 4, 1, 8, 0, 0, 0) AS business_start;
```

```sql
SELECT DATETIMEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1, 0, 0, 0, 0) AS first_of_month;
```

## References

- [Functions](../README.md)
- [TIMEFROMPARTS](timefromparts.md)
- [DATEPART](../datetime/datepart.md)
