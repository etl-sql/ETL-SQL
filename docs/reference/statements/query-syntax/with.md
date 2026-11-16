# WITH (CTE)

Defines one or more Common Table Expressions (CTEs) scoped to the following statement or query expression.

## Syntax

```sql
WITH [RECURSIVE] cte_name [(col1, col2)] AS (
  SELECT ...
)
SELECT * FROM cte_name;
```

## Examples

### Multiple CTEs

```sql
WITH
  base AS (
    SELECT RegionId, SUM(Sales) AS Total
    FROM #orders
    GROUP BY RegionId
  ),
  ranked AS (
    SELECT *, ROW_NUMBER() OVER (ORDER BY Total DESC) AS Rank
    FROM base
  )
SELECT * FROM ranked WHERE Rank <= 10;
```

### Recursive CTE and Subquery Scopes

```sql
WITH RECURSIVE seq(n) AS (
  SELECT 1
  UNION ALL
  SELECT n + 1 FROM seq WHERE n < 5
)
SELECT
  s.n,
  (WITH doubled AS (SELECT s.n * 2 AS val) SELECT val FROM doubled) AS twice
FROM (
  WITH filtered AS (SELECT n FROM seq WHERE n >= 2)
  SELECT n FROM filtered
) s
WHERE EXISTS (
  WITH bounds AS (SELECT 4 AS max_n)
  SELECT 1 FROM bounds WHERE s.n <= max_n
);
```

## Notes

- `WITH` and `WITH RECURSIVE` are valid at the start of a statement (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`) and at the start of any query expression: inside CTE bodies, derived tables, `JOIN` subqueries, scalar subqueries, `IN` / `EXISTS` subqueries, and parenthesized `UNION` / `INTERSECT` / `EXCEPT` branches.
- A CTE is visible only within the statement or query expression that declares it.
- Inner CTEs shadow any outer CTE or `#temp` table with the same name for the duration of their query expression, restoring the outer definition when the query expression finishes.

## References

- [SELECT](../dml/select.md)
- [Statements](../README.md)
