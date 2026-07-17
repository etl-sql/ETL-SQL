# Window Functions

Window functions calculate values across a set of rows related to the current row without collapsing the result set.

## Ranking And Offset Functions

- [ROW_NUMBER](row_number.md) - unique row sequence within a window.
- [RANK](rank.md) - rank with gaps after ties.
- [DENSE_RANK](dense_rank.md) - rank without gaps after ties.
- [NTILE](ntile.md) - distribute rows into buckets.
- [LAG](lag.md) - read a prior row value.
- [LEAD](lead.md) - read a following row value.
- [FIRST_VALUE](first_value.md) - read the first value in a window frame.
- [LAST_VALUE](last_value.md) - read the last value in a window frame.

## Aggregate Functions Used As Windows

Aggregate functions can also be used with `OVER (...)` to produce running totals, partition-level metrics, moving averages, and framed calculations while preserving one output row per input row.

- [SUM](../aggregate/sum.md) - running or framed totals.
- [AVG](../aggregate/avg.md) - moving averages and partition averages.
- [COUNT](../aggregate/count.md) - row counts and partition counts.
- [MIN](../aggregate/min.md) - running or partition minimums.
- [MAX](../aggregate/max.md) - running or partition maximums.
- [STDEV](../aggregate/stdev.md) - partition or framed sample standard deviation.

## Example

```sql
SELECT sales_date, region, amount,
    SUM(amount) OVER (
        PARTITION BY region
        ORDER BY sales_date
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS running_region_total,
    AVG(amount) OVER (
        PARTITION BY region
        ORDER BY sales_date
        ROWS BETWEEN 6 PRECEDING AND CURRENT ROW
    ) AS moving_7_avg
FROM #daily_sales;
```

## References

- [Functions](../README.md)
- [Aggregate Functions](../aggregate/README.md)
