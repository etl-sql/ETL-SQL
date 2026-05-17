# RTRIM
Removes trailing (right-side) whitespace from a string.

**Category:** String

## Syntax
```sql
RTRIM(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to right-trim |

## Returns
`STRING` — The input string with trailing whitespace removed.

## Example
```sql
SELECT RTRIM('hello   ');   -- → 'hello'
SELECT RTRIM(address) AS address FROM #contacts;
```

## See Also
- [Standard Library — §3.1 Case & Whitespace](../../../../../Docs/Reference/Standard_Library.md#31-case--whitespace)
- Related: [`LTRIM`](LTRIM.md), [`TRIM`](TRIM.md)
