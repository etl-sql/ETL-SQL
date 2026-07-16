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

## DuckDB statement form
A cleaner statement syntax sits alongside the SQL-standard clause above (the standard form keeps working unchanged).

```sql
-- Dynamic: one output column per distinct quarter (no value list needed)
PIVOT #sales ON quarter USING SUM(amount);

-- Enumerated values + explicit row grouping
PIVOT #sales ON quarter IN ('Q1','Q2') USING SUM(amount) GROUP BY region;

-- Multiple aggregates -> columns Q1_total, Q1_cnt, Q2_total, Q2_cnt, ...
PIVOT #sales ON quarter USING SUM(amount) AS total, COUNT(*) AS cnt;

-- UNPIVOT all columns except some, without listing them
UNPIVOT #sales ON COLUMNS(* EXCLUDE (region, name)) INTO NAME quarter VALUE amount;

-- UNPIVOT an explicit list
UNPIVOT #sales ON q1, q2, q3 INTO NAME quarter VALUE amount;
```

- Output column names: single unnamed aggregate -> the pivot value (`Q1`); multiple `ON` columns -> joined with `_` (`2000_Q1`); multiple aggregates -> suffixed with the aggregate name (`Q1_total`).
- Omitting `GROUP BY` groups by every column not consumed by `ON` or the aggregates.
- `IN (...)` applies to a single `ON` column; omit it for dynamic discovery (and for multiple `ON` columns).

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

References:
- [Statements](../README.md)
