# YEAR

Returns the year component of a date as an integer.

## Syntax

```sql
YEAR(date)
```

## Parameters

- **date** - Source date or datetime value.

## Returns

Returns an `INT` year value.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Remarks

- **Dialect Translation**: In pushdown queries, the engine transpiles `YEAR`, `MONTH`, and `DAY` to `EXTRACT(YEAR/MONTH/DAY FROM date)` for **Postgres** and **Oracle** targets.

## Examples

```sql
SELECT YEAR('2026-05-17') AS order_year;
```

```sql
SELECT YEAR(GETDATE()) AS current_year;
```

```sql
SELECT YEAR(order_date) AS order_year, SUM(amount) AS order_total
FROM #orders
GROUP BY YEAR(order_date);
```

## References

- [Functions](../README.md)
- [MONTH](month.md)
- [DAY](day.md)
- [DATEPART](datepart.md)
