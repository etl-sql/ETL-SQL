# FORMAT
Converts a value to a string using a .NET format pattern.

Syntax:
  FORMAT(value, 'format_string')

Parameters:
  value         — the date, numeric, or other value to format
  format_string — a standard or custom .NET format string (e.g., 'yyyy-MM-dd', 'N2', 'P1')

```sql
-- Format a date
SELECT FORMAT(GETDATE(), 'yyyy-MM-dd');    -- '2025-03-15'

-- Format a number with thousands separators and 2 decimals
SELECT FORMAT(1234567.89, 'N2');           -- '1,234,567.89'

-- Format as percentage
SELECT FORMAT(0.175, 'P1');                -- '17.5%'
```
