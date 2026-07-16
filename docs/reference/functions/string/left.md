# LEFT
Returns the leftmost N characters of a string.

**Category:** String

## Syntax
```sql
LEFT(string, count)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string |
| `count` | `INT` | Number of characters to return from the left |

## Returns
`STRING` — The first `count` characters. If `count` exceeds the string length, the full string is returned.

## Example
```sql
SELECT LEFT('Hello World', 5);   -- → 'Hello'
SELECT LEFT(zip_code, 3) AS region FROM #addresses;
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../guides/getting-started.md#32-substrings--search)
- Related: [`RIGHT`](right.md), [`SUBSTRING`](substring.md)
