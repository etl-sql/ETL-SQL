# INITCAP
Capitalizes the first letter of each word and lowercases the rest.

**Category:** String

## Syntax
```sql
INITCAP(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The input string to title-case |

## Returns
`STRING` — Each word capitalized, remaining characters lowercased. Word boundaries are spaces and common punctuation.

## Example
```sql
SELECT INITCAP('hello world');        -- → 'Hello World'
SELECT INITCAP('JOHN DOE') AS Name;  -- → 'John Doe'
```

## See Also
- [Standard Library — §3.1 Case & Whitespace](../../../../../Docs/Reference/Standard_Library.md#31-case--whitespace)
- Related: [`UPPER`](UPPER.md), [`LOWER`](LOWER.md)
