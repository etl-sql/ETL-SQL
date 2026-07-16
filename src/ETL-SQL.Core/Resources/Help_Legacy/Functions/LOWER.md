# LOWER
Converts all characters in a string to lowercase.

**Category:** String

## Syntax
```sql
LOWER(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The input string to convert |

## Returns
`STRING` — The input string with all alphabetic characters in lowercase.

## Example
```sql
SELECT LOWER('HELLO WORLD');          -- → 'hello world'
SELECT LOWER(email) AS email FROM #users;
```

## See Also
- [Standard Library — §3.1 Case & Whitespace](../../../../../Docs/Reference/Standard_Library.md#31-case--whitespace)
- Related: [`UPPER`](UPPER.md), [`INITCAP`](INITCAP.md)
