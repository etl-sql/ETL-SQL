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
- Related: [REGEXP_SUBSTR](REGEXP_SUBSTR.md), [REGEXP_SPLIT_TO_TABLE](REGEXP_SPLIT_TO_TABLE.md)