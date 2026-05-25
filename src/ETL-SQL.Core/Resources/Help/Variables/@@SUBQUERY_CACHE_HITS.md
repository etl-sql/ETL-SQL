# @@SUBQUERY_CACHE_HITS
Number of scalar subquery results retrieved from the session cache rather than being re-evaluated. Indicates effective subquery memoization.

```sql
SET PROFILING = ON;

SELECT
    order_id,
    (SELECT MAX(amount) FROM #orders) AS max_amount
  INTO #flagged
  FROM #orders
  WHERE amount > 100;

PRINT 'Cache hits:   ' + @@SUBQUERY_CACHE_HITS;
PRINT 'Cache misses: ' + @@SUBQUERY_CACHE_MISSES;
```

A high hit ratio means the engine is avoiding repeated subquery execution. If hits are lower than expected, verify the subquery does not reference outer-row variables that prevent caching — correlated subqueries are always misses.

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
