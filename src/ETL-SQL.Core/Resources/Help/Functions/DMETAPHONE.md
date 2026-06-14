# DMETAPHONE
Returns the primary Double Metaphone phonetic key of a string.

**Category:** Fuzzy Matching

## Syntax
`sql
DMETAPHONE(string)
`

## Returns
STRING â€” The primary phonetic key. Returns NULL if input is NULL.

## Example
`sql
SELECT DMETAPHONE('Schmidt'); -- â†’ 'XMT'
`

## See Also
- Related: [DMETAPHONE_ALT](DMETAPHONE_ALT.md), [METAPHONE](METAPHONE.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
