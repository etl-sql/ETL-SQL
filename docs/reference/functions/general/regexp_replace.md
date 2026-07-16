# REGEXP_REPLACE
Replaces occurrences of a regex pattern in a string.

**Category:** Regex

## Syntax
```sql
REGEXP_REPLACE(string, pattern, replacement)
REGEXP_REPLACE(string, pattern, replacement, position, occurrence, flags)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The source string |
| `pattern` | `STRING` | PCRE regular expression |
| `replacement` | `STRING` | Replacement string. Use `\1`, `\2` etc. for capture group backreferences |
| `position` | `INT` | Optional: start position (default: 1) |
| `occurrence` | `INT` | Optional: which match to replace (0 = all, default: 0) |
| `flags` | `STRING` | Optional: `i`, `m`, `s` |

## Returns
`STRING` — The string with matching occurrences replaced.

## Example
```sql
SELECT REGEXP_REPLACE('Hello World', 'o', '0');           -- → 'Hell0 W0rld'
SELECT REGEXP_REPLACE(phone, '[^\d]', '');                -- strip non-digits
SELECT REGEXP_REPLACE(ssn, '(\d{3})-\d{2}-(\d{4})', 'XXX-XX-\2'); -- partial mask
```

## See Also
- [Standard Library — §3.7 Regex (PCRE)](../../../guides/getting-started.md#37-regex-pcre)
- Related: [`REGEXP_LIKE`](regexp_like.md), [`REGEXP_SUBSTR`](regexp_substr.md), [`REPLACE`](../string/replace.md)
