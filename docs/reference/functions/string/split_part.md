# SPLIT_PART
Returns the Nth segment of a string after splitting by a delimiter.

**Category:** String

## Syntax
```sql
SPLIT_PART(string, delimiter, part)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string to split |
| `delimiter` | `STRING` | The separator string |
| `part` | `INT` | 1-based index of the segment to return |

## Returns
`STRING` — The Nth segment. Returns an empty string if the part index exceeds the number of segments.

## Example
```sql
SELECT SPLIT_PART('a,b,c', ',', 2);         -- → 'b'
SELECT SPLIT_PART('2026-05-17', '-', 1);    -- → '2026'
SELECT SPLIT_PART(full_name, ' ', 1) AS first_name FROM #people;
```

## See Also
- [Standard Library — §3.3 Concatenation & Splitting](../../../guides/getting-started.md#33-concatenation--splitting)
- Related: [`STRING_SPLIT`](string_split.md), [`CHARINDEX`](charindex.md)
