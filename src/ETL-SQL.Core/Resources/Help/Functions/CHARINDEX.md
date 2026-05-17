# CHARINDEX
Returns the 1-based position of the first occurrence of a substring within a string.

**Category:** String

## Syntax
```sql
CHARINDEX(find, string)
CHARINDEX(find, string, start)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `find` | `STRING` | The substring to search for |
| `string` | `STRING` | The string to search within |
| `start` | `INT` | Optional: 1-based position to begin searching (default: 1) |

## Returns
`INT` — 1-based position of the first match, or `0` if not found.

## Example
```sql
SELECT CHARINDEX('World', 'Hello World');       -- → 7
SELECT CHARINDEX('o', 'Hello World', 6);        -- → 8  (search from pos 6)
SELECT CHARINDEX('@', email) AS at_pos FROM #users WHERE CHARINDEX('@', email) = 0;
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../../../Docs/Reference/Standard_Library.md#32-substrings--search)
- Related: [`PATINDEX`](PATINDEX.md), [`POSITION`](POSITION.md), [`INSTR`](INSTR.md)
