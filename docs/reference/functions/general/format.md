# FORMAT
Formats a value using a .NET format string, returning a locale-aware string.

**Category:** System

## Syntax
```sql
FORMAT(value, format_string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value` | `ANY` | The value to format (numeric, date, or string) |
| `format_string` | `STRING` | A .NET standard or custom format string |

## Returns
`STRING` — The formatted string.

## Example
```sql
SELECT FORMAT(1234567.89, 'N2');         -- → '1,234,567.89'
SELECT FORMAT(0.1234, 'P1');             -- → '12.3%'
SELECT FORMAT(GETDATE(), 'yyyy-MM-dd');  -- → '2026-05-17'
SELECT FORMAT(GETDATE(), 'MMMM d, yyyy'); -- → 'May 17, 2026'
SELECT FORMAT(order_total, 'C2') AS total FROM #orders;
```

## See Also
- [Standard Library — §3.4 Formatting & Padding](../../../guides/getting-started.md#34-formatting--padding)
- Related: [`TO_STR`](../string/to_str.md), [`STR`](../string/str.md), [`CAST`](../conversion/cast.md)
