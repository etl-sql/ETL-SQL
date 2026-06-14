# NGRAM_TOKENS
Table-valued function that returns 3-character grams of normalized tokens in a string. Used for fuzzy join blocking key generation.

**Category:** Fuzzy Matching

## Syntax
`sql
SELECT * FROM NGRAM_TOKENS(string)
`

## Returns
TABLE â€” A table containing a single column alue (VARCHAR) with the computed token 3-grams.

## Example
`sql
SELECT * FROM NGRAM_TOKENS('John Smith');
`

## See Also
- Related: [NGRAMS](NGRAMS.md), [NORMALIZE](NORMALIZE.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
