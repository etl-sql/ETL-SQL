# Aggregate Functions

Aggregate functions summarize values across grouped rows. Most aggregate functions can also be used with `OVER (...)` as window functions when the query needs per-row output plus partition or frame-level metrics.

## Numeric Aggregates

- [SUM](sum.md) - total non-NULL numeric values.
- [AVG](avg.md) - arithmetic mean of non-NULL numeric values.
- [MIN](min.md) - smallest non-NULL value.
- [MAX](max.md) - largest non-NULL value.
- [COUNT](count.md) - row count or non-NULL value count.

## Statistical Aggregates

- [STDEV](stdev.md) - sample standard deviation.
- [STDEVP](stdevp.md) - population standard deviation.
- [VAR](var.md) - sample variance.
- [VARP](varp.md) - population variance.

## String Aggregates

- [STRING_AGG](string_agg.md) - concatenate grouped string values with a separator.
- [LISTAGG](../aggregate/listagg.md) - ordered list aggregation syntax.

## Window Usage

Use `OVER (...)` with aggregate functions for running totals, moving averages, partition counts, and framed min/max calculations.

```sql
SELECT sales_date, region, amount,
    SUM(amount) OVER (
        PARTITION BY region
        ORDER BY sales_date
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS running_region_total
FROM #daily_sales;
```

## References

- [Standard Library](../standard-library.md)
- [Window Functions](../window/README.md)
