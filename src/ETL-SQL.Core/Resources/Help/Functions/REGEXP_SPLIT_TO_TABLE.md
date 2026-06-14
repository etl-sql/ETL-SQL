# REGEXP_SPLIT_TO_TABLE
Table-valued function that splits a string into a table using a regular expression separator.

**Category:** Regex

## Syntax
`sql
SELECT * FROM REGEXP_SPLIT_TO_TABLE(string, pattern)
`

## Returns
TABLE â€” A table of split parts with a single column alue (VARCHAR).

## Example
`sql
SELECT * FROM REGEXP_SPLIT_TO_TABLE('a, b; c', '[,;]\s*');
`

## See Also
- Related: [STRING_SPLIT](STRING_SPLIT.md), [REGEXP_MATCHES](REGEXP_MATCHES.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
