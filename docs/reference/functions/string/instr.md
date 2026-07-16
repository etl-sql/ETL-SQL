# INSTR
Returns the 1-based position of the first occurrence of a substring. Alias for POSITION.

**Category:** String

## Syntax
```sql
INSTR(string, find)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to search within |
| `find` | `STRING` | The substring to locate |

## Returns
`INT` — 1-based position of the first match, or `0` if not found.

## Remarks
- `INSTR` is an alias for `POSITION`. `CHARINDEX` accepts the same arguments in reversed order: `CHARINDEX(find, string)`.

## Example
```sql
SELECT INSTR('Hello World', 'World');   -- → 7
SELECT INSTR(url, '?') AS query_start FROM #requests;
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../guides/getting-started.md#32-substrings--search)
- Related: [`POSITION`](position.md), [`CHARINDEX`](charindex.md)
