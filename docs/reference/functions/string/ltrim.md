# LTRIM
Removes leading (left-side) whitespace from a string.

**Category:** String

## Syntax
```sql
LTRIM(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to left-trim |

## Returns
`STRING` — The input string with leading whitespace removed.

## Example
```sql
SELECT LTRIM('  hello');   -- → 'hello'
SELECT LTRIM('  ' + name) FROM #data;
```

## See Also
- [Standard Library — §3.1 Case & Whitespace](../../../guides/getting-started.md#31-case--whitespace)
- Related: [`RTRIM`](rtrim.md), [`TRIM`](trim.md)
