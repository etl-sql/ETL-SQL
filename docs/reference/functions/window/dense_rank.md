# DENSE_RANK

Assigns a rank to each row with no gaps for tied ranks.

## Syntax

```sql
DENSE_RANK()
  OVER (
    [PARTITION BY column_name [, ...]]
    ORDER BY sort_expression [ASC|DESC] [, ...]
)
```

## Returns

Returns a `BIGINT` rank value. Equal sort values share the same rank, and the next rank does not skip.

## Null Behavior

`DENSE_RANK` does not return `NULL`.

## Remarks

- `ORDER BY` inside `OVER (...)` is required.
- Use `DENSE_RANK` when tied rows should not create gaps in subsequent rank numbers.

## Examples

```sql
SELECT
  name,
  score,
  DENSE_RANK() OVER (ORDER BY score DESC) AS score_rank
FROM #leaderboard;
```

```sql
SELECT
  department,
  employee_name,
  salary,
  DENSE_RANK() OVER (PARTITION BY department ORDER BY salary DESC) AS department_salary_rank
FROM #employees;
```

## References

- [Functions](../README.md)
- [RANK](rank.md)
- [ROW_NUMBER](row_number.md)
