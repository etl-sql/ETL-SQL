# METAPHONE
Returns the English phonetic code (Metaphone key) of a string.

**Category:** Fuzzy Matching

## Syntax
`sql
METAPHONE(string)
`

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| string | VARCHAR / STRING | The input string to encode |

## Returns
STRING â€” The Metaphone phonetic key. Returns NULL if input is NULL.

## Example
`sql
SELECT METAPHONE('Jackson'); -- â†’ 'JKSN'
`

## See Also
- Related: [SOUNDEX](soundex.md), [DMETAPHONE](dmetaphone.md)

References:
- [Standard Library](../../../guides/getting-started.md)
