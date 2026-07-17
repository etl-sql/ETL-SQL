# CHAR

Returns the character corresponding to an ASCII or Unicode code point.

## Syntax

```sql
CHAR(code)
```

## Parameters

- **code** - Numeric code point.

## Returns

Returns a single-character string for the given code point.

## Null Behavior

Returns `NULL` when `code` is `NULL` or out of range.

## Examples

```sql
SELECT CHAR(65) AS upper_a;
```

```sql
UPDATE #raw
SET notes = REPLACE(notes, CHAR(13), '');
```

## References

- [Functions](../README.md)
- [ASCII](ascii.md)
- [UNICODE](unicode.md)
