# TRY_PARSE
Safely converts a culture-formatted string to a type, returning NULL on failure.

**Category:** System

## Syntax
```sql
TRY_PARSE(string, type)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to parse |
| `type` | `TYPE` | The target data type |

## Returns
The parsed value, or `NULL` if parsing fails. Never raises an exception.

## Example
```sql
SELECT TRY_PARSE('May 17, 2026', DATE);   -- → 2026-05-17
SELECT TRY_PARSE('not a date', DATE);     -- → NULL
SELECT TRY_PARSE(raw_date, DATE) AS clean_date FROM #imported;
```

## See Also
- [Standard Library — §2. Type Conversion](../../../../../Docs/Reference/Standard_Library.md#2-type-conversion)
- Related: [`PARSE`](PARSE.md), [`TRY_CAST`](TRY_CAST.md)
