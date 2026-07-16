# QUALIFY
Filters the results of window functions. Evaluated after window functions have been computed.

## Syntax
```sql
SELECT columns,
       WINDOW_FUNCTION() OVER (PARTITION BY partition_col ORDER BY sort_col) AS alias
FROM table_name
QUALIFY alias <= threshold;
```

## Example
Find the top two highest-paid employees in each department:
```sql
SELECT employee_id, name, department, salary,
       RANK() OVER (PARTITION BY department ORDER BY salary DESC) as salary_rank
FROM hr.employees
QUALIFY salary_rank <= 2;
```

## Notes
- `QUALIFY` is analogous to `HAVING`, but filters window function results rather than standard group-by aggregates.
- It avoids the need to wrap queries in a CTE or subquery just to filter on window outputs (like `ROW_NUMBER()` or `RANK()`).
- You can filter on the window function alias, or specify the window function directly in the `QUALIFY` expression.

References:
- [Statements](../README.md)
