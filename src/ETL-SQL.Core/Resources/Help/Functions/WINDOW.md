Window / analytic functions compute a value across a set of rows related to the current row without collapsing them. All require an OVER clause.

Syntax:
  function() OVER ([PARTITION BY cols] [ORDER BY cols] [frame_clause])

Ranking functions:
  ROW_NUMBER()        — unique sequential integer per partition; ties get different numbers
  RANK()              — same rank for ties; gaps follow (1, 2, 2, 4)
  DENSE_RANK()        — same rank for ties; no gaps (1, 2, 2, 3)
  NTILE(n)            — divide rows into n equal buckets; returns bucket number 1–n
  PERCENT_RANK()      — (rank - 1) / (rows - 1); result is 0.0–1.0
  CUME_DIST()         — cumulative distribution; fraction of rows <= current

Offset functions:
  LAG(col [, offset [, default]])   — value from offset rows before current (default offset=1)
  LEAD(col [, offset [, default]])  — value from offset rows after current
  FIRST_VALUE(col)                  — first value in the window frame
  LAST_VALUE(col)                   — last value in the window frame

Aggregate windows (running/moving):
  SUM(col) OVER (...)     — running or windowed sum
  AVG(col) OVER (...)     — running or windowed average
  COUNT(*) OVER (...)     — running count
  MIN(col) OVER (...)     — windowed minimum
  MAX(col) OVER (...)     — windowed maximum

Frame clause (optional; default is RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW):
  ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW    — cumulative
  ROWS BETWEEN 2 PRECEDING AND CURRENT ROW             — 3-row moving window
  ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING     — reverse cumulative
  ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING  — whole partition

```sql
SELECT
    sale_date,
    region,
    amount,
    -- Rank within each region by amount descending
    RANK()        OVER (PARTITION BY region ORDER BY amount DESC) AS rank_in_region,
    DENSE_RANK()  OVER (PARTITION BY region ORDER BY amount DESC) AS dense_rank,
    ROW_NUMBER()  OVER (ORDER BY sale_date)                       AS row_num,

    -- Running total per region
    SUM(amount) OVER (PARTITION BY region ORDER BY sale_date
                      ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total,

    -- 7-day moving average
    AVG(amount) OVER (ORDER BY sale_date
                      ROWS BETWEEN 6 PRECEDING AND CURRENT ROW)         AS moving_avg_7d,

    -- Day-over-day change
    amount - LAG(amount, 1, 0) OVER (PARTITION BY region ORDER BY sale_date) AS delta,

    -- Bucket into 4 performance tiers
    NTILE(4) OVER (ORDER BY amount DESC) AS quartile

FROM #daily_sales;
```
