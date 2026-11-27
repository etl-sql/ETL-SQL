# CLEAN_TEXT

Applies an ordered list of text cleaning and standardization rules to a string value.

## Syntax

```sql
CLEAN_TEXT(value [, rules])
```

## Parameters

- **value** — `STRING`. Input value to clean.
- **rules** — Optional comma-separated `STRING` of rule names applied in order. Defaults to `'STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM'`.
  - **TRIM** — Strips leading and trailing whitespace.
  - **COLLAPSE_WHITESPACE** — Collapses runs of whitespace (including tabs and newlines) into a single space.
  - **STRIP_CONTROL** — Removes non-whitespace control characters and zero-width formatting characters (`U+200B`, `U+200C`, `U+200D`, `U+FEFF`).
  - **NULL_IF_EMPTY** — Converts an empty string (`''`) to `NULL`.
  - **UPPER** — Converts text to uppercase (invariant culture).
  - **LOWER** — Converts text to lowercase (invariant culture).
  - **TITLE** / **INITCAP** — Capitalizes the first character of each word (invariant culture).
  - **PHONE** — Standardizes phone numbers (same engine as `STANDARDIZE(value, 'PHONE')`).
  - **EMAIL** — Standardizes email addresses (same engine as `STANDARDIZE(value, 'EMAIL')`).
  - **POSTAL_CODE** — Standardizes US/Canadian postal codes (same engine as `STANDARDIZE(value, 'POSTAL_CODE')`).

## Returns

Returns the cleaned `VARCHAR`, or `NULL` when `value` is `NULL`, `rules` is `NULL`, or `NULL_IF_EMPTY` produces an empty string.

## Examples

```sql
SELECT CLEAN_TEXT('  acme   trading  ', 'TRIM, COLLAPSE_WHITESPACE, TITLE') AS company_name;
-- 'Acme Trading'
```

```sql
SELECT CLEAN_TEXT('   ', 'TRIM, NULL_IF_EMPTY') AS maybe_null;
-- NULL
```

## References

- [Functions](../README.md)
- [STANDARDIZE](standardize.md)
- [CLEAN (TRANSFORM)](../../transform/clean.md)
- [CLEAN_STRING](clean_string.md)
