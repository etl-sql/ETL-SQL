# CLEAN_STRING

Normalizes whitespace in a string: replaces control characters with spaces, collapses runs of
whitespace into a single space, and trims the result.

## Syntax

```sql
CLEAN_STRING(str)
```

## Parameters

- **str** - String to normalize.

## Returns

Returns the normalized `VARCHAR`.

## Null Behavior

Returns `NULL` when `str` is `NULL`.

## Remarks

- Control characters (tabs, newlines, carriage returns, and other `char.IsControl` characters) become
  spaces before collapsing, so a multi-line field becomes a single clean line.
- Repeated whitespace of any kind collapses to one standard space; leading and trailing whitespace is
  removed.
- Use [`REMOVE_HIDDEN_CHARACTERS`](remove_hidden_characters.md) when you also need zero-width
  characters and non-breaking spaces stripped, and
  [`REMOVE_HTML_CHARACTERS`](remove_html_characters.md) to decode HTML entities and normalize
  typographic quotes and dashes.
- Common for cleaning free-text fields pasted from spreadsheets or scraped from documents before a
  join or comparison.

## Examples

```sql
SELECT CLEAN_STRING('  Acme   Trading
   Co.  ') AS company_name;
-- 'Acme Trading Co.'
```

```sql
-- Normalize before joining on a text key
SELECT s.*, r.region
FROM #staged s
JOIN #reference r ON CLEAN_STRING(s.company_name) = CLEAN_STRING(r.company_name);
```

## References

- [Functions](../README.md)
- [REMOVE_HIDDEN_CHARACTERS](remove_hidden_characters.md)
- [REMOVE_HTML_CHARACTERS](remove_html_characters.md)
- [TRIM](trim.md)
