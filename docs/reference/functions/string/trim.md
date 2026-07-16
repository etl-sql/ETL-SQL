# TRIM
Removes leading and trailing whitespace (or specified characters) from a string.

**Category:** String

## Syntax
```sql
TRIM(string)
TRIM(BOTH | LEADING | TRAILING chars FROM string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to trim |
| `chars` | `STRING` | Optional: specific character(s) to remove instead of whitespace |

## Returns
`STRING` — The string with the specified characters removed from the specified side(s).

## Example
```sql
SELECT TRIM('  hello  ');                   -- → 'hello'
SELECT TRIM(LEADING '0' FROM '00123');      -- → '123'
SELECT TRIM(TRAILING '.' FROM 'value...');  -- → 'value'
```

## See Also
- [Standard Library — §3.1 Case & Whitespace](../../../guides/getting-started.md#31-case--whitespace)
- Related: [`LTRIM`](ltrim.md), [`RTRIM`](rtrim.md)
