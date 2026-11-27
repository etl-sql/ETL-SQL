# STANDARDIZE

Standardizes a string value into a canonical format for common analyst fields (`PHONE`, `EMAIL`, `POSTAL_CODE`).

## Syntax

```sql
STANDARDIZE(value, format)
```

## Parameters

- **value** — `STRING`. Input value to standardize.
- **format** — `STRING`. Target format name (case-insensitive):
  - **PHONE** — Extracts digits and formats 10-digit numbers as `XXX-XXX-XXXX`, 11-digit NANP numbers starting with `1` as `+1-XXX-XXX-XXXX`, and other international numbers with a leading `+` as `+<digits>`. Returns `NULL` when no digits are present.
  - **EMAIL** — Strips control characters, trims whitespace, and lowercases using invariant culture. Returns `NULL` when empty.
  - **POSTAL_CODE** — Strips control characters, trims and uppercases, formats 9-digit US ZIP+4 as `XXXXX-XXXX`, and formats 6-character Canadian postal codes as `A1A 1A1`. Returns `NULL` when empty.

## Returns

Returns the standardized `VARCHAR`, or `NULL` when `value` or `format` is `NULL` (or when the cleaned value is empty).

## Examples

```sql
SELECT
  STANDARDIZE('(555) 123-4567', 'PHONE') AS phone,
  STANDARDIZE('  Alice@Example.COM ', 'EMAIL') AS email,
  STANDARDIZE('k1a0b1', 'POSTAL_CODE') AS postal;
-- '555-123-4567', 'alice@example.com', 'K1A 0B1'
```

## References

- [Functions](../README.md)
- [CLEAN_TEXT](clean_text.md)
- [CLEAN (TRANSFORM)](../../transform/clean.md)
