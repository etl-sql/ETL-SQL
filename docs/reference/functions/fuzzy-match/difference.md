# DIFFERENCE

Returns a Soundex similarity score between two strings.

## Syntax

```sql
DIFFERENCE(s1, s2)
```

## Parameters

- **s1** - First string to compare.
- **s2** - Second string to compare.

## Returns

Returns an `INT` score from `0` through `4` comparing the two 4-character [`SOUNDEX`](soundex.md) codes position by position.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Remarks

- `4` means the Soundex codes are identical; `0` means none of the four positions match.
- A different first letter can never score a full `4`, since the Soundex code keeps the leading character.
- Use it for cheap phonetic ranking; for graded similarity use [`SIMILARITY`](similarity.md), and for raw edit distance use [`LEVENSHTEIN`](levenshtein.md).

## Examples

```sql
SELECT DIFFERENCE('Smith', 'Smythe') AS phonetic_score;
```

```sql
SELECT a.name, b.name, DIFFERENCE(a.name, b.name) AS soundex_score
FROM #source AS a
CROSS JOIN #reference AS b
WHERE DIFFERENCE(a.name, b.name) >= 3;
```

## References

- [Functions](../README.md)
- [SOUNDEX](soundex.md)
- [METAPHONE](metaphone.md)
- [SIMILARITY](similarity.md)
- [LEVENSHTEIN](levenshtein.md)
