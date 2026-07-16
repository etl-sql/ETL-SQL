# AVG

Returns the arithmetic mean of non-NULL values in a group or window.

## Syntax

```sql
AVG(expression)
AVG(expression) OVER (...)
```

## Parameters

- **expression** - Numeric column or expression to average.

## Returns

Returns the mean value as a decimal or floating-point result.

## Null Behavior

Ignores `NULL` inputs. Returns `NULL` when all input values are `NULL`.

## Remarks

- Integer division is not applied; `AVG` returns a decimal-style result.
- Use `AVG(...) OVER (...)` for moving averages and partition-level averages without collapsing rows.

## Examples

```sql
SELECT AVG(score) AS avg_score
FROM #tests;
```

```sql
SELECT category, AVG(price) AS avg_price
FROM #catalog
GROUP BY category;
```

```sql
SELECT region, sales_date, amount,
    AVG(amount) OVER (
        PARTITION BY region
        ORDER BY sales_date
        ROWS BETWEEN 6 PRECEDING AND CURRENT ROW
    ) AS rolling_7_avg
FROM #daily_sales;
```

## References

- [Standard Library](../standard-library.md)
- [SUM](sum.md)
- [STDEV](stdev.md)
- [MEDIAN](../general/median.md)
