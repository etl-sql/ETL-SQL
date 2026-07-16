# STDEV

Returns the sample standard deviation of values in a group.

## Syntax

```sql
STDEV(expression)
STDDEV_SAMP(expression)
STDEV(expression) OVER (...)
```

## Parameters

- **expression** - Numeric column or expression to evaluate.

## Returns

Returns a `FLOAT` sample standard deviation.

## Null Behavior

Ignores `NULL` inputs. Returns `NULL` when fewer than two non-NULL values are available.

## Remarks

`STDDEV_SAMP` is an alias for `STDEV`.

## Examples

```sql
SELECT STDEV(score) AS score_stddev
FROM #exams;
```

```sql
SELECT region, AVG(revenue) AS avg_revenue, STDEV(revenue) AS volatility
FROM #sales
GROUP BY region;
```

```sql
SELECT product_id, category,
    STDEV(price) OVER (PARTITION BY category) AS category_spread
FROM #products;
```

## References

- [Standard Library](../standard-library.md)
- [STDEVP](stdevp.md)
- [VAR](var.md)
- [AVG](avg.md)
