# FILL_DATES

Fills missing rows in a time-series dataset at a configurable calendar or hourly interval.

```sql
TRANSFORM #target
FROM #source
USING FILL_DATES (
  DATE_COL = 'date_column',
  INTERVAL = 'HOUR' | 'DAY' | 'WEEK' | 'MONTH' | 'QUARTER' | 'YEAR',
  START_DATE = 'start_date',
  END_DATE = 'end_date',
  GAPS_FILL = default_value | 'FORWARD',
  BY_GROUP = 'group_column[, ...]'
);
```

- **DATE_COL = 'date_column'** — Name of the column containing dates or datetimes. Must be present in the source table and contain valid date values.
- **INTERVAL = 'interval'** — Optional step size for generated series points: `HOUR`, `DAY` (default), `WEEK`, `MONTH`, `QUARTER`, or `YEAR`.
- **START_DATE = 'start_date'** — Optional lower bound for the generated series. Defaults to the earliest date in each group.
- **END_DATE = 'end_date'** — Optional upper bound for the generated series. Defaults to the latest date in each group.
- **GAPS_FILL = default_value | 'FORWARD'** — Value to populate in missing columns for gap-filled rows, or `'FORWARD'` to carry the previous row's non-key values forward (`NULL` for leading gaps). Defaults to `0`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of group columns. When specified, gap filling is performed independently within each partition/group.

## Examples

Fills missing daily sales records per region, defaulting missing quantities to 0:

```sql
TRANSFORM #sales_filled
FROM #sales
USING FILL_DATES (
  DATE_COL = 'OrderDate',
  GAPS_FILL = 0,
  BY_GROUP = 'Region'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
