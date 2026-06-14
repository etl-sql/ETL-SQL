# DMETAPHONE_ALT
Returns the alternate Double Metaphone phonetic key for a string.

**Category:** Fuzzy Matching

## Syntax
`sql
DMETAPHONE_ALT(string)
`

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| string | VARCHAR / STRING | The input string to encode |

## Returns
STRING â€” The alternate phonetic key. Returns NULL if input is NULL.

## Example
`sql
SELECT DMETAPHONE_ALT('Schmidt'); -- â†’ 'XMT'
`

## See Also
- Related: [DMETAPHONE](DMETAPHONE.md), [METAPHONE](METAPHONE.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
