# TOP_N_OTHERS

Reduces high-cardinality values by aggregating low-volume categories into a single row. Useful for keeping pie and bar charts clean.

```sql
TRANSFORM #target
FROM #source
USING TOP_N_OTHERS (
  N = n_value,
  VALUE_COL = 'value_column',
  CATEGORY_COL = 'category_column',
  OTHERS_LABEL = 'others_label',
  AGGREGATE = 'SUM' | 'AVG' | 'COUNT' | 'MIN' | 'MAX',
  BY_GROUP = 'group_column[, ...]'
);
```

- **N = n_value** — The number of top categories to keep.
- **VALUE_COL = 'value_column'** — The numeric column containing values used to determine the top categories.
- **CATEGORY_COL = 'category_column'** — The column containing category labels.
- **OTHERS_LABEL = 'others_label'** — The label to use for the aggregated row containing all other categories. Defaults to `'Others'`.
- **AGGREGATE = 'aggregate_function'** — The aggregation function to apply when grouping other categories. Defaults to `'SUM'`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition by. Top categories are determined and kept independently within each partition/group.

## Examples

Keeps the top 3 countries by sales volume, aggregating all other countries under 'All Others':

```sql
TRANSFORM #top_sales_countries
FROM #sales_data
USING TOP_N_OTHERS (
  N = 3,
  VALUE_COL = 'Sales',
  CATEGORY_COL = 'Country',
  OTHERS_LABEL = 'All Others'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
