# REGEXP_LIKE
Returns 1 if a string matches a PCRE regular expression pattern, 0 otherwise.

**Category:** Regex

## Syntax
```sql
REGEXP_LIKE(string, pattern)
REGEXP_LIKE(string, pattern, flags)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The string to test |
| `pattern` | `STRING` | PCRE regular expression |
| `flags` | `STRING` | Optional: modifier flags (`i` = case-insensitive, `m` = multiline, `s` = dotall) |

## Returns
`BIT` — `1` if the string matches the pattern; `0` otherwise.

## Example
```sql
SELECT REGEXP_LIKE('hello@example.com', '^[^@]+@[^@]+\.[^@]+$');  -- → 1 (valid email)
SELECT REGEXP_LIKE('Hello', 'hello', 'i');                          -- → 1 (case-insensitive)
SELECT * FROM #emails WHERE REGEXP_LIKE(email, '@gmail\.com$') = 0; -- non-Gmail
```

## See Also
- [Standard Library — §3.7 Regex (PCRE)](../../../../../Docs/Reference/Standard_Library.md#37-regex-pcre)
- Related: [`REGEXP_SUBSTR`](REGEXP_SUBSTR.md), [`REGEXP_REPLACE`](REGEXP_REPLACE.md), [`REGEXP_INSTR`](REGEXP_INSTR.md)
