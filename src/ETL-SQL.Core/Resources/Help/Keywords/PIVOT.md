# PIVOT / UNPIVOT
Rotates rows into columns (PIVOT) or columns into rows (UNPIVOT).

## PIVOT Syntax
```sql
SELECT * FROM #sales
PIVOT (
  SUM(Amount)
  FOR Month IN ('Jan', 'Feb', 'Mar', 'Apr')
) AS pvt;
```

## UNPIVOT Syntax
```sql
SELECT * FROM #wide
UNPIVOT (
  Amount FOR Month IN (Jan, Feb, Mar, Apr)
) AS unpvt;
```

## Dynamic PIVOT (values discovered at runtime)
```sql
-- Collect distinct values first, then pivot
SELECT DISTINCT Month INTO #months FROM #sales;

PIVOT #sales
  AGGREGATE SUM(Amount)
  FOR Month IN (SELECT Month FROM #months)
  INTO #pivoted;
```

## Notes
- Static PIVOT column values must be quoted string literals.
- Dynamic PIVOT uses a subquery to discover pivot values at runtime; results flow into a new #temp table via `INTO`.
- NULL cells in a PIVOT result indicate no matching rows; wrap with `COALESCE(..., 0)` if needed.
- UNPIVOT excludes NULL values by default.
- See: SELECT, GROUP BY, WITH