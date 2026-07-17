# SIMILARITY

Returns a normalized similarity score between two strings using the specified algorithm.

## Syntax

```sql
SIMILARITY(string1, string2)
SIMILARITY(string1, string2, mode)
```

## Parameters

- **string1** - First string.
- **string2** - Second string.
- **mode** - Optional algorithm name. See [accepted values](#accepted-values-for-mode).

## Returns

Returns a `DECIMAL` score from `0.0` through `1.0`. `1.0` means identical strings; `0.0` means completely different strings.

## Null Behavior

Returns `NULL` when either string argument is `NULL`.

## Accepted Values for `mode`

- **`'JAROWINKLER'`** - Default. Best for person names and short identifiers.
- **`'LEVENSHTEIN'`** - Best for short strings with typos.
- **`'TRIGRAM'`** - General-purpose option for longer strings.
- **`'JACCARD'`** - Best when word presence matters more than order.
- **`'TOKENSORT'`** - Best for names where first and last tokens may be swapped.

## Examples

```sql
SELECT SIMILARITY('Smith', 'Smyth') AS score;
```

```sql
SELECT a.id, b.id, SIMILARITY(a.name, b.name) AS score
FROM #dirty AS a
CROSS JOIN #reference AS b
WHERE SIMILARITY(a.name, b.name) > 0.85;
```

## References

- [Functions](../README.md)
- [NORMALIZE](normalize.md)
- [LEVENSHTEIN](levenshtein.md)
- [SOUNDEX](soundex.md)
