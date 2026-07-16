# LEVENSHTEIN
Computes the Levenshtein distance (edit distance) between two strings.

**Category:** Fuzzy Matching

## Syntax
`sql
LEVENSHTEIN(string1, string2)
`

## Returns
INT â€” The minimum number of single-character edits (insertions, deletions, or substitutions) required to change string1 into string2.

## Example
`sql
SELECT LEVENSHTEIN('kitten', 'sitting'); -- â†’ 3
`

## See Also
- Related: [SIMILARITY](similarity.md), [SOUNDEX](soundex.md)

References:
- [Standard Library](../../../guides/getting-started.md)
