# UPPER
Converts all characters in a string to uppercase.

**Category:** String

## Syntax
```sql
UPPER(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The input string to convert |

## Returns
`STRING` — The input string with all alphabetic characters in uppercase.

## Example
```sql
SELECT UPPER('hello world');          -- → 'HELLO WORLD'
SELECT UPPER(first_name) AS Name FROM #customers;
```

## See Also
- [Standard Library — §3.1 Case & Whitespace](../../../../../Docs/Reference/Standard_Library.md#31-case--whitespace)
- Related: [`LOWER`](LOWER.md), [`INITCAP`](INITCAP.md)
