# RIGHT
Returns the rightmost N characters of a string.

**Category:** String

## Syntax
```sql
RIGHT(string, count)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string |
| `count` | `INT` | Number of characters to return from the right |

## Returns
`STRING` — The last `count` characters. If `count` exceeds the string length, the full string is returned.

## Example
```sql
SELECT RIGHT('Hello World', 5);    -- → 'World'
SELECT RIGHT('00' + TO_STR(id), 4) AS padded_id FROM #orders;
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../guides/getting-started.md#32-substrings--search)
- Related: [`LEFT`](left.md), [`SUBSTRING`](substring.md)
