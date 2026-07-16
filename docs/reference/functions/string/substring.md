# SUBSTRING
Returns a portion of a string starting at a given position.

**Category:** String

## Syntax
```sql
SUBSTRING(string, start, length)
SUBSTR(string, start, length)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string |
| `start` | `INT` | 1-based starting position. Negative values count from the end |
| `length` | `INT` | Number of characters to return |

## Returns
`STRING` — The extracted substring. Returns an empty string if `start` is beyond the end of `string`.

## Remarks
- Positions are **1-indexed** (first character = 1), matching SQL Server convention.
- `SUBSTR` is a direct alias for `SUBSTRING`.
- If `start + length` exceeds the string length, characters up to the end are returned without error.

## Example
```sql
SELECT SUBSTRING('Hello World', 7, 5);   -- → 'World'
SELECT SUBSTRING('Hello World', 1, 5);   -- → 'Hello'
SELECT SUBSTR(product_code, 1, 3) AS prefix FROM #products;
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../guides/getting-started.md#32-substrings--search)
- Related: [`LEFT`](left.md), [`RIGHT`](right.md), [`CHARINDEX`](charindex.md)
