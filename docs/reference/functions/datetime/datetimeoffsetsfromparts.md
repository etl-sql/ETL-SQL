# DATETIMEOFFSETSFROMPARTS

Constructs a DATETIMEOFFSET value from individual date, time, and timezone offset components.

## Syntax

```sql
DATETIMEOFFSETSFROMPARTS(year, month, day, hour, minute, second, fractions, hour_offset, minute_offset, precision)
```

## Parameters

- **year, month, day** - Date components.
- **hour, minute, second** - Time components.
- **fractions** - Fractional seconds component.
- **hour_offset, minute_offset** - Time zone offset from UTC.
- **precision** - Fractional second precision.

## Returns

Returns a `DATETIMEOFFSET`.

## Null Behavior

Returns `NULL` when any required component is `NULL`.

## Examples

```sql
SELECT DATETIMEOFFSETSFROMPARTS(2026, 6, 12, 14, 30, 0, 0, -5, 0, 0) AS local_time;
```

## References

- [Functions](../README.md)
- [DATETIMEFROMPARTS](datetimefromparts.md)
- [TIMEFROMPARTS](timefromparts.md)
