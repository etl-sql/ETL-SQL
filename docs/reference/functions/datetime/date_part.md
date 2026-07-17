# DATE_PART

Extracts a specified date part component from a date as an integer value.

## Syntax

```sql
DATE_PART(datepart, date)
```

## Parameters

- **datepart** - Date part to extract. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **date** - Source date or datetime value.

## Returns

Returns an integer value for the specified date part.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Accepted Values for `datepart`

`YEAR`, `QUARTER`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND`, `DOW`, `DOY`

## Examples

```sql
SELECT DATE_PART(MONTH, '2026-05-17') AS month_number;
```

```sql
SELECT DATE_PART(QUARTER, order_date) AS order_quarter
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATEPART](../datetime/datepart.md)
- [EXTRACT](../datetime/extract.md)
