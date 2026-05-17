# CONCAT
Concatenates two or more strings into a single string.

**Category:** String

## Syntax
```sql
CONCAT(string1, string2, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string1` | `STRING` | First string (or value coerced to string) |
| `string2` | `STRING` | Second string |
| `...` | `STRING` | Additional strings (variadic) |

## Returns
`STRING` — All arguments joined in order.

## Remarks
- `NULL` arguments are treated as empty strings — they do not propagate `NULL` (unlike the `+` operator).
- Non-string arguments are implicitly coerced to `STRING`.

## Example
```sql
SELECT CONCAT('Hello', ' ', 'World');         -- → 'Hello World'
SELECT CONCAT(first_name, ' ', last_name) AS full_name FROM #customers;
SELECT CONCAT('ID-', id, '-', status) FROM #orders;
```

## See Also
- [Standard Library — §3.3 Concatenation & Splitting](../../../../../Docs/Reference/Standard_Library.md#33-concatenation--splitting)
- Related: [`CONCAT_WS`](CONCAT_WS.md), [`STRING_AGG`](STRING_AGG.md)
