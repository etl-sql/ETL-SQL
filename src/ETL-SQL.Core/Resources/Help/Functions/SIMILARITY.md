# SIMILARITY
Returns a normalized similarity score (0.0–1.0) between two strings using the specified algorithm.

**Category:** Fuzzy

## Syntax
```sql
SIMILARITY(string1, string2)
SIMILARITY(string1, string2, mode)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string1` | `STRING` | First string |
| `string2` | `STRING` | Second string |
| `mode` | `STRING` | Optional: algorithm name — see Accepted Values |

## Returns
`DECIMAL` — Score in [0.0, 1.0]. `1.0` = identical strings; `0.0` = completely different.

## Accepted Values for `mode`
| Value | Best for |
| :--- | :--- |
| `'JAROWINKLER'` *(default)* | Person names, short identifiers |
| `'LEVENSHTEIN'` | Short strings with typos |
| `'TRIGRAM'` | General purpose, longer strings |
| `'JACCARD'` | Word presence matters more than order |
| `'TOKENSORT'` | Names where first/last may be swapped |

## Example
```sql
SELECT SIMILARITY('Smith', 'Smyth');                        -- → 0.943...
SELECT SIMILARITY('Robert Smith', 'Smith Robert', 'TOKENSORT'); -- → 1.0
SELECT a.id, b.id, SIMILARITY(a.name, b.name) AS score
  FROM #dirty a CROSS JOIN #reference b
  WHERE SIMILARITY(a.name, b.name) > 0.85;
```

## See Also
- [Standard Library — §16.2 SIMILARITY](../../../../../Docs/Reference/Standard_Library.md#162-similarity--normalized-similarity-score)
- Related: [`NORMALIZE`](NORMALIZE.md), [`LEVENSHTEIN`](LEVENSHTEIN.md), [`SOUNDEX`](SOUNDEX.md)
