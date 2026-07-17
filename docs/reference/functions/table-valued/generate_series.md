# GENERATE_SERIES

Returns a table of sequential numeric values within a range.

## Syntax

```sql
GENERATE_SERIES(start, stop)
GENERATE_SERIES(start, stop, step)
```

## Parameters

- **start** - First value in the series.
- **stop** - Last value in the series, inclusive.
- **step** - Optional increment between values. Defaults to `1`.

## Returns

Returns a table with a single `value` column, one row per value in the sequence.

## Null Behavior

Returns no rows when any required argument is `NULL`.

## Examples

```sql
SELECT value
FROM GENERATE_SERIES(1, 10);
```

```sql
SELECT DATEADD(DAY, value, '2026-01-01') AS calendar_date
FROM GENERATE_SERIES(0, 364);
```

```sql
SELECT r.value AS row, c.value AS col
FROM GENERATE_SERIES(1, 5) AS r
CROSS JOIN GENERATE_SERIES(1, 5) AS c;
```

## References

- [Standard Library](../standard-library.md)
- [SORT_LIST](../collections/sort_list.md)
- [APPEND_TO_LIST](../collections/append_to_list.md)
