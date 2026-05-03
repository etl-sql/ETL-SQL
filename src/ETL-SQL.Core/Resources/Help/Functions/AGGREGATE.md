Aggregate functions operate on groups of rows. Use with GROUP BY, or with OVER() for window variants.

  SUM(col)                    — total of non-NULL values
  COUNT(*)                    — count all rows including NULLs
  COUNT(col)                  — count non-NULL values in col
  COUNT(DISTINCT col)         — distinct non-NULL values
  COUNT_DISTINCT(col)         — alias for COUNT(DISTINCT col)
  AVG(col)                    — arithmetic mean (NULLs excluded)
  MIN(col)                    — minimum value
  MAX(col)                    — maximum value

String aggregation:
  STRING_AGG(col, separator)  — join group values into one string
  LISTAGG(col, sep)           — alias (Oracle-style)

Statistical:
  STDEV(col)                  — sample standard deviation
  STDEVP(col)                 — population standard deviation
  VAR(col)                    — sample variance
  VARP(col)                   — population variance
  MEDIAN(col)                 — 50th percentile
  PERCENTILE_CONT(frac) WITHIN GROUP (ORDER BY col)
                              — continuous percentile; frac is 0.0–1.0
  PERCENTILE_DISC(frac) WITHIN GROUP (ORDER BY col)
                              — discrete percentile (nearest actual value)

Notes:
  - Aggregates ignore NULL values (except COUNT(*))
  - HAVING filters on aggregate results; WHERE filters before aggregation
  - All aggregates can be used as window functions with OVER — see HELP FUNCTIONS WINDOW

```sql
SELECT
    region,
    COUNT(*)                        AS orders,
    COUNT(DISTINCT customer_id)     AS customers,
    SUM(amount)                     AS revenue,
    AVG(amount)                     AS avg_order,
    MIN(order_date)                 AS first_order,
    MAX(order_date)                 AS last_order,
    STRING_AGG(product, ', ')       AS products
FROM #orders
GROUP BY region
HAVING SUM(amount) > 10000
ORDER BY revenue DESC;
```
