# STR
Formats a numeric value as a right-padded string of a specified length.

**Category:** String

## Syntax
```sql
STR(number, [length], [decimals])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` / `DECIMAL` | The numeric value to format |
| `length` | `INT` | Optional: total output length including decimal point and sign (default: 10) |
| `decimals` | `INT` | Optional: decimal places to include (default: 0) |

## Returns
`STRING` — Right-aligned numeric string padded with leading spaces. If the result exceeds `length`, asterisks (`*`) are returned.

## Example
```sql
SELECT STR(1234.567, 8, 2);   -- → '1234.57'
SELECT STR(42);                -- → '        42'
SELECT STR(amount, 12, 2) AS formatted_amount FROM #ledger;
```

## See Also
- [Standard Library — §3.4 Formatting & Padding](../../../guides/getting-started.md#34-formatting--padding)
- Related: [`FORMAT`](../general/format.md), [`TO_STR`](to_str.md)
