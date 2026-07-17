# SUM

Returns the sum of all non-NULL values in a group or window.

## Syntax

```sql
SUM(expression)
SUM(expression) OVER (...)
```

## Parameters

- **expression** - Numeric column or expression to sum.

## Returns

Returns the total of all non-NULL input values.

## Null Behavior

Ignores `NULL` inputs. Returns `NULL` when all input values are `NULL`.

## Examples

```sql
SELECT SUM(amount) AS total_amount
FROM #orders;
```

```sql
SELECT region, SUM(revenue) AS region_total
FROM #sales
GROUP BY region;
```

```sql
SELECT sales_date, revenue,
    SUM(revenue) OVER (
        ORDER BY sales_date
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS cumulative_revenue
FROM #daily_sales;
```

## References

- [Functions](../README.md)
- [COUNT](count.md)
- [AVG](avg.md)
- [MIN](min.md)
- [MAX](max.md)
