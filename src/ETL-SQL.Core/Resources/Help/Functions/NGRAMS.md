# NGRAMS
Table-valued function that returns a table of N-character grams from a string. Used with UNNEST for inverted-index blocking.

**Category:** Fuzzy Matching

## Syntax
`sql
SELECT * FROM NGRAMS(string, size)
`

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| string | VARCHAR / STRING | The text to slice |
| size | INT | Gram length (N) |

## Returns
TABLE â€” A table of generated grams with a single column alue (VARCHAR).

## Example
`sql
SELECT * FROM NGRAMS('hello', 2); -- â†’ 'he', 'el', 'll', 'lo'
`

## See Also
- Related: [NGRAM_TOKENS](NGRAM_TOKENS.md), [SIMILARITY](SIMILARITY.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
