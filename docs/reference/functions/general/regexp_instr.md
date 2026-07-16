# REGEXP_INSTR
Returns the 1-based position of the first (or Nth) regex match in a string.

**Category:** Regex

## Syntax
```sql
REGEXP_INSTR(string, pattern)
REGEXP_INSTR(string, pattern, position, occurrence, option, flags)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to search |
| `pattern` | `STRING` | PCRE regular expression |
| `position` | `INT` | Optional: start position (default: 1) |
| `occurrence` | `INT` | Optional: which match to find (default: 1) |
| `option` | `INT` | Optional: `0` = start of match, `1` = end of match + 1 |
| `flags` | `STRING` | Optional: `i`, `m`, `s` |

## Returns
`INT` — 1-based position of the match, or `0` if not found.

## Example
```sql
SELECT REGEXP_INSTR('hello world', 'o');       -- → 5  (first 'o')
SELECT REGEXP_INSTR('hello world', 'o', 1, 2); -- → 8  (second 'o')
SELECT REGEXP_INSTR(text, '\d+') AS num_pos FROM #data;
```

## See Also
- [Standard Library — §3.7 Regex (PCRE)](../../../guides/getting-started.md#37-regex-pcre)
- Related: [`REGEXP_SUBSTR`](regexp_substr.md), [`CHARINDEX`](../string/charindex.md)
