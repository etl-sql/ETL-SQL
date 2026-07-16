# POSITION
Returns the 1-based position of the first occurrence of a substring. SQL-standard form.

**Category:** String

## Syntax
```sql
POSITION(find IN string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `find` | `STRING` | The substring to locate |
| `string` | `STRING` | The string to search within |

## Returns
`INT` — 1-based position of the first match, or `0` if not found.

## Remarks
- `POSITION` uses SQL-standard `IN` keyword syntax. Aliases: [`INSTR(string, find)`](instr.md), [`CHARINDEX(find, string)`](charindex.md).

## Example
```sql
SELECT POSITION('World' IN 'Hello World');   -- → 7
SELECT POSITION('@' IN email) AS at_pos FROM #contacts;
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../guides/getting-started.md#32-substrings--search)
- Related: [`CHARINDEX`](charindex.md), [`INSTR`](instr.md), [`PATINDEX`](patindex.md)
