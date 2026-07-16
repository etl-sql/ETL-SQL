# WITH (CTE)
Defines one or more Common Table Expressions (CTEs) scoped to the following SELECT statement.

## Syntax
```sql
WITH cte_name AS (
  SELECT ...
)
SELECT * FROM cte_name;
```

## Multiple CTEs
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

## Notes
- CTEs are non-recursive; ETL-SQL does not support `WITH RECURSIVE`.
- A CTE is only visible to the single statement that follows the `WITH` block.
- CTEs are evaluated lazily. They are inlined and may be executed multiple times if referenced more than once. For expensive CTEs, `SELECT ... INTO #temp` first.
- CTE names shadow any #temp table with the same name for the duration of that statement.
- See: SELECT, PARALLEL

References:
- [Grammar](../../../guides/getting-started.md)
