# REGEXP_MATCHES
Table-valued function that returns a table of all regex matches in a string.

**Category:** Regex

## Syntax
`sql
SELECT * FROM REGEXP_MATCHES(string, pattern)
`

## Returns
TABLE â€” A table of matching substring values.

## Example
`sql
SELECT * FROM REGEXP_MATCHES('apple, banana, cherry', '\w+');
`

## See Also
- Related: [REGEXP_SUBSTR](regexp_substr.md), [REGEXP_SPLIT_TO_TABLE](regexp_split_to_table.md)

References:
- [Standard Library](../../../guides/getting-started.md)
