# GENERATE_SERIES
Returns a table of sequential numeric values within a range.

**Category:** System

## Syntax
```sql
GENERATE_SERIES(start, stop)
GENERATE_SERIES(start, stop, step)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `start` | `INT` / `DECIMAL` | First value in the series |
| `stop` | `INT` / `DECIMAL` | Last value (inclusive) |
| `step` | `INT` / `DECIMAL` | Optional: increment between values (default: 1) |

## Returns
Table with a single `value` column, one row per value in the sequence.

## Example
```sql
-- Generate integers 1–10
SELECT value FROM GENERATE_SERIES(1, 10);

-- Generate even numbers
SELECT value FROM GENERATE_SERIES(0, 20, 2);

-- Create a date spine
SELECT DATEADD(DAY, value, '2026-01-01') AS calendar_date
FROM GENERATE_SERIES(0, 364);

-- Cross join for a grid
SELECT r.value AS row, c.value AS col
FROM GENERATE_SERIES(1, 5) r CROSS JOIN GENERATE_SERIES(1, 5) c;
```

## See Also
- [Standard Library — §10. Collection & List Functions](../../../../../Docs/Reference/Standard_Library.md#10-collection--list-functions)
- Related: [`SORT_LIST`](SORT_LIST.md), [`APPEND_TO_LIST`](APPEND_TO_LIST.md)
