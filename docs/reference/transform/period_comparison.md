# PERIOD_COMPARISON

Calculates period-over-period differences and growth percentages (DoD, MoM, YoY) on sequential time-series data.

```sql
TRANSFORM #target
FROM #source
USING PERIOD_COMPARISON (
  DATE_COL = 'date_column',
  VALUE_COL = 'value_column',
  PERIOD = 'DAY' | 'MONTH' | 'YEAR',
  BY_GROUP = 'group_column[, ...]',
  DIFF_COL = 'diff_column_name',
  PCT_COL = 'pct_column_name'
);
```

- **DATE_COL = 'date_column'** — The column containing chronological dates or datetimes.
- **VALUE_COL = 'value_column'** — The numeric column to compare.
- **PERIOD = 'period_type'** — The comparison offset interval: `DAY`, `MONTH`, or `YEAR`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition/group by. Comparisons are calculated independently within each group.
- **DIFF_COL = 'diff_column_name'** — Output column name for difference (`current - prior`). Defaults to `'{VALUE_COL}_Diff'`.
- **PCT_COL = 'pct_column_name'** — Output column name for growth percentage (`(current - prior) / prior * 100`). Defaults to `'{VALUE_COL}_Pct'`.

## Examples

Calculates month-over-month sales difference and growth percentage:

```sql
TRANSFORM #sales_mom
FROM #monthly_sales
USING PERIOD_COMPARISON (
  DATE_COL = 'MonthStart',
  VALUE_COL = 'Revenue',
  PERIOD = 'MONTH'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
