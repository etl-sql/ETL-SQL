# @@SUBQUERY_CACHE_MISSES
Number of scalar subquery evaluations that could not be served from the cache and required a full execution. Paired with @@SUBQUERY_CACHE_HITS to assess caching effectiveness.

```sql
SELECT
    id,
    (SELECT name FROM #ref WHERE id = outer.id) AS name
  INTO #result
  FROM #outer AS outer;

PRINT 'Cache misses (correlated — expected): ' + @@SUBQUERY_CACHE_MISSES;
```

Correlated subqueries (those referencing the outer query's current row) are always cache misses because their result changes per row. Rewrite them as JOINs to eliminate the per-row evaluation overhead.

References:
- [Standard Library](../../guides/getting-started.md)
