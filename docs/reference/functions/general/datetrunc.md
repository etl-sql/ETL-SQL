# DATETRUNC

Truncates a date to the beginning of the specified date part boundary.

## Syntax

```sql
DATETRUNC(datepart, date)
```

## Parameters

- **datepart** - Boundary to truncate to. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **date** - Source date or datetime value.

## Returns

Returns a `DATETIME` with all parts below `datepart` zeroed out.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Accepted Values for `datepart`

`YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`, `HOUR`, `MINUTE`, `SECOND`

## Examples

```sql
SELECT DATETRUNC(MONTH, '2026-05-17') AS month_start;
```

```sql
SELECT DATETRUNC(HOUR, event_time) AS event_hour, COUNT(*) AS event_count
FROM #events
GROUP BY DATETRUNC(HOUR, event_time);
```

## References

- [Standard Library](../standard-library.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [TRUNC](trunc.md)
- [DATEPART](../datetime/datepart.md)
- [DATEADD](../datetime/dateadd.md)
