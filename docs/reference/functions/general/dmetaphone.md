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
- Related: [DMETAPHONE_ALT](dmetaphone_alt.md), [METAPHONE](metaphone.md)

References:
- [Standard Library](../../../guides/getting-started.md)
