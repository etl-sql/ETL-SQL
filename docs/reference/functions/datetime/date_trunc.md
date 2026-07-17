# DATE_TRUNC

Truncates a datetime to the beginning of the specified date part boundary.

## Syntax

```sql
DATE_TRUNC(datepart, date)
```

## Parameters

- **datepart** - Boundary to truncate to. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **date** - Date or datetime value to truncate.

## Returns

Returns a `DATETIME` with all components below `datepart` zeroed out.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Accepted Values for `datepart`

`YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`, `HOUR`, `MINUTE`, `SECOND`

## Examples

```sql
SELECT DATE_TRUNC(MONTH, '2026-05-17 12:30:00') AS month_start;
```

```sql
SELECT DATE_TRUNC(HOUR, event_time) AS event_hour, COUNT(*) AS event_count
FROM #events
GROUP BY DATE_TRUNC(HOUR, event_time);
```

## References

- [Standard Library](../standard-library.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATETRUNC](../datetime/datetrunc.md)
- [TRUNC](../datetime/trunc.md)
