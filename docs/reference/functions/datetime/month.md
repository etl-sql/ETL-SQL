# MONTH

Returns the month component of a date as an integer (1–12).

## Syntax

```sql
MONTH(date)
```

## Parameters

- **date** - Source date or datetime value.

## Returns

Returns an `INT` month number from `1` through `12`.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT MONTH('2026-05-17') AS order_month;
```

```sql
SELECT MONTH(GETDATE()) AS current_month;
```

```sql
SELECT DATENAME(MONTH, GETDATE()) AS current_month_name;
```

## References

- [Standard Library](../standard-library.md)
- [YEAR](year.md)
- [DAY](day.md)
- [DATENAME](datename.md)
