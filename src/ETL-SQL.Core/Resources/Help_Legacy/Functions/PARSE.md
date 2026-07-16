# PARSE
Converts a string to a date, time, or numeric type using culture-aware parsing.

**Category:** System

## Syntax
```sql
PARSE(string, type)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The culture-formatted string to parse |
| `type` | `TYPE` | The target data type (`DATE`, `DATETIME`, `INT`, `DECIMAL`, etc.) |

## Returns
The parsed value in `type`. Raises an exception if parsing fails (use [`TRY_PARSE`](TRY_PARSE.md) for safe parsing).

## Remarks
- More flexible than `CAST` for locale-specific formats (e.g., `'May 17, 2026'`, `'17/05/2026'`).
- For dates, recognizes month names and various delimiters.

## Example
```sql
SELECT PARSE('May 17, 2026', DATE);       -- → 2026-05-17
SELECT PARSE('17/05/2026', DATE);         -- → 2026-05-17
SELECT PARSE('1,234.56', DECIMAL);        -- → 1234.56
```

## See Also
- [Standard Library — §2. Type Conversion](../../../../../Docs/Reference/Standard_Library.md#2-type-conversion)
- Related: [`TRY_PARSE`](TRY_PARSE.md), [`CAST`](CAST.md), [`TRY_CAST`](TRY_CAST.md)
