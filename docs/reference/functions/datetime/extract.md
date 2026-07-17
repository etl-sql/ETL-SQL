# EXTRACT

Extracts a specified date part component from a date or time expression.

## Syntax

```sql
EXTRACT(field FROM source)
```

## Parameters

- **field** - Date/time field to extract, such as `YEAR` or `EPOCH`.
- **source** - Date or time expression to extract from.

## Returns

Returns the extracted component as a numeric value.

## Null Behavior

Returns `NULL` when `source` is `NULL`.

## Accepted Values for `field`

`YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND`, `DOW`, `DOY`, `EPOCH`, `QUARTER`, `WEEK`, `ISODOW`, `DECADE`, `CENTURY`, `MILLENNIUM`

## Examples

```sql
SELECT EXTRACT(YEAR FROM '2026-05-28') AS order_year;
```

```sql
SELECT event_id, EXTRACT(EPOCH FROM event_time) AS event_epoch_seconds
FROM #events;
```

## References

- [Standard Library](../standard-library.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATEPART](datepart.md)
- [DATE_PART](../datetime/date_part.md)
