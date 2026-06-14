# REGEXP_COUNT
Returns the number of times a regular expression pattern matches within a string.

**Category:** Regex

## Syntax
`sql
REGEXP_COUNT(string, pattern)
`

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| string | VARCHAR / STRING | The text to search |
| pattern | VARCHAR / STRING | The regular expression pattern to search for |

## Returns
INT â€” The count of matches. Returns NULL if any argument is NULL.

## Example
`sql
SELECT REGEXP_COUNT('abc123xyz456', '\d+'); -- â†’ 2
`

## See Also
- Related: [REGEXP_LIKE](REGEXP_LIKE.md), [REGEXP_SUBSTR](REGEXP_SUBSTR.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
