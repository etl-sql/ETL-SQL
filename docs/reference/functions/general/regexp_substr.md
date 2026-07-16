# REGEXP_SUBSTR
Returns the portion of a string matched by a regex pattern.

**Category:** Regex

## Syntax
```sql
REGEXP_SUBSTR(string, pattern)
REGEXP_SUBSTR(string, pattern, position, occurrence, flags)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to search |
| `pattern` | `STRING` | PCRE regular expression |
| `position` | `INT` | Optional: start position (default: 1) |
| `occurrence` | `INT` | Optional: which match to return (default: 1) |
| `flags` | `STRING` | Optional: `i`, `m`, `s` |

## Returns
`STRING` — The matched substring, or `NULL` if no match.

## Example
```sql
SELECT REGEXP_SUBSTR('Price: $42.99', '\$[\d.]+');     -- → '$42.99'
SELECT REGEXP_SUBSTR(notes, '\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}') AS phone
  FROM #contacts;
```

## See Also
- [Standard Library — §3.7 Regex (PCRE)](../../../guides/getting-started.md#37-regex-pcre)
- Related: [`REGEXP_LIKE`](regexp_like.md), [`REGEXP_REPLACE`](regexp_replace.md), [`REGEXP_INSTR`](regexp_instr.md)
