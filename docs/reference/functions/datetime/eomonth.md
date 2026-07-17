# EOMONTH

Returns the last day of the month for a given date, optionally offset by a number of months.

## Syntax

```sql
EOMONTH(date)
EOMONTH(date, months)
```

## Parameters

- **date** - Reference date.
- **months** - Optional month offset before computing end-of-month. Use negative values for prior months.

## Returns

Returns the last calendar day of the selected month as a `DATE`.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT EOMONTH('2026-02-01') AS month_end;
```

```sql
SELECT EOMONTH(GETDATE(), -1) AS previous_month_end;
```

```sql
SELECT invoice_id, EOMONTH(invoice_date) AS invoice_month_end
FROM #invoices;
```

## References

- [Functions](../README.md)
- [DATEADD](../datetime/dateadd.md)
- [DATETRUNC](datetrunc.md)
