# TO_DATE
Converts a string representation of a date/time to a standard DATETIME value.

**Category:** Date & Time

## Syntax
`sql
TO_DATE(string [, format])
`

## Returns
DATETIME â€” The parsed date. Returns NULL if string is NULL or parsing fails.

## Example
`sql
SELECT TO_DATE('2026-06-12'); -- â†’ '2026-06-12 00:00:00'
`

## See Also
- Related: [TO_TIMESTAMP](to_timestamp.md), [CAST](../conversion/cast.md)

References:
- [Standard Library](../../../guides/getting-started.md)
