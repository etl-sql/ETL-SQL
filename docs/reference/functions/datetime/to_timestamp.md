# TO_TIMESTAMP

Converts a Unix epoch timestamp (number of seconds since `1970-01-01 00:00:00 UTC`) to a standard date/time representation.

## Syntax

```sql
TO_TIMESTAMP(seconds)
```

## Parameters

- **seconds** - Seconds elapsed since `1970-01-01 00:00:00 UTC`. Decimal fractions represent sub-second precision.

## Returns

Returns the corresponding `DATETIME`.

## Null Behavior

Returns `NULL` when `seconds` is `NULL`.

## Examples

```sql
SELECT TO_TIMESTAMP(0) AS epoch_start;
```

```sql
SELECT TO_TIMESTAMP(event_epoch_seconds) AS event_time
FROM #events;
```

## References

- [Functions](../README.md)
- [EXTRACT](../datetime/extract.md)
- [DATEADD](../datetime/dateadd.md)
