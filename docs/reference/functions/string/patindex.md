# PATINDEX
Returns the 1-based starting position of the first occurrence of a wildcard pattern in a string.

**Category:** String

## Syntax
```sql
PATINDEX(pattern, string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `pattern` | `STRING` | A wildcard pattern using `%` (any characters) and `_` (single character) |
| `string` | `STRING` | The string to search within |

## Returns
`INT` — 1-based position of the first match, or `0` if no match is found.

## Remarks
- Uses SQL `LIKE`-style wildcards (`%`, `_`), not regex. Use [`REGEXP_INSTR`](../general/regexp_instr.md) for regex-based position searches.
- Case sensitivity follows `SET CASE_SENSITIVE`.

## Example
```sql
SELECT PATINDEX('%@%.%', 'user@example.com');  -- → 5
SELECT PATINDEX('%[0-9]%', 'abc123def');        -- → 4
SELECT * FROM #emails WHERE PATINDEX('%@%', email) = 0;  -- missing @ sign
```

## See Also
- [Standard Library — §3.2 Substrings & Search](../../../guides/getting-started.md#32-substrings--search)
- Related: [`CHARINDEX`](charindex.md), [`REGEXP_INSTR`](../general/regexp_instr.md)
