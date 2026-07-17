# DATEDIFF

Returns the count of date/time part boundaries crossed between two dates.

## Syntax

```sql
DATEDIFF(datepart, start_date, end_date)
```

## Parameters

- **datepart** - Unit to measure. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **start_date** - Starting date.
- **end_date** - Ending date.

## Returns

Returns an `INT` count of `datepart` boundaries crossed. The result is positive when `end_date` is after `start_date` and negative when reversed.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- Counts **boundaries crossed**, not elapsed time. `DATEDIFF(MONTH, '2026-01-31', '2026-02-01')` returns `1` even though only 1 day elapsed.

## Examples

```sql
SELECT DATEDIFF(DAY, '2026-01-01', '2026-05-17') AS days_between;
```

```sql
SELECT employee_id, DATEDIFF(MONTH, hire_date, GETDATE()) AS months_employed
FROM #employees;
```

```sql
SELECT job_id, DATEDIFF(SECOND, start_time, end_time) AS duration_seconds
FROM #jobs;
```

## References

- [Functions](../README.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATEADD](dateadd.md)
- [DATEPART](datepart.md)
