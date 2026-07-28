# INTERPOLATE

Fills missing numeric values between known data points in a series.

```sql
TRANSFORM #target
FROM #source
USING INTERPOLATE (
  VALUE_COL = 'value_column',
  ORDER_COL = 'order_column',
  METHOD = 'LINEAR' | 'FORWARD' | 'BACKWARD',
  BY_GROUP = 'group_column[, ...]'
);
```

- **VALUE_COL = 'value_column'** — The numeric column containing nulls/missing values to interpolate.
- **ORDER_COL = 'order_column'** — The column used to sort the values chronologically or sequentially before interpolating. Can be a numeric or date/datetime type.
- **METHOD = 'method'** — The interpolation method:
  - `LINEAR` (default) — Computes linear progression values between the nearest non-null neighbors.
  - `FORWARD` (or `FORWARD_FILL`) — Performs a Last Observation Carried Forward (LOCF) fill.
  - `BACKWARD` (or `BACKWARD_FILL`) — Performs a Next Observation Carried Backward (NOCB) fill.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition/group by. Interpolation is performed independently within each group.

## Examples

Fills missing temperatures in a time series using linear interpolation:

```sql
TRANSFORM #temp_interpolated
FROM #weather_readings
USING INTERPOLATE (
  VALUE_COL = 'Temperature',
  ORDER_COL = 'ReadingTime',
  METHOD = 'LINEAR',
  BY_GROUP = 'StationId'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
