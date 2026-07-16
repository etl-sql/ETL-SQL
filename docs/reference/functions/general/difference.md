# DIFFERENCE
Returns a Soundex similarity score between two strings.

**Category:** Fuzzy

## Syntax
```sql
DIFFERENCE(s1, s2)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `s1` | `STRING` | First string to compare |
| `s2` | `STRING` | Second string to compare |

## Returns
`INT` — A score from `0` to `4` comparing the two 4-character [`SOUNDEX`](soundex.md) codes position by position: `4` means the codes are identical (strongly similar sounding), `0` means none of the four positions match. Returns `NULL` if either argument is `NULL`.

## Remarks
- A different first letter can never score a full `4`, since the Soundex code keeps the leading character.
- Use it for cheap phonetic ranking; for graded similarity use [`SIMILARITY`](similarity.md) (0–1) and for raw edit distance use [`LEVENSHTEIN`](levenshtein.md).

## Example
```sql
SELECT DIFFERENCE('Smith', 'Smythe');   -- → 4  (both encode to S530)
SELECT DIFFERENCE('Robert', 'Rupert');  -- → 4  (same Soundex code)
SELECT DIFFERENCE('Smith', 'Jones');    -- → 2  (different initials)
```

## See Also
- [Standard Library — §16.4 Phonetic Encoding Functions](../../../guides/getting-started.md#164-phonetic-encoding-functions)
- Related: [`SOUNDEX`](soundex.md), [`METAPHONE`](metaphone.md), [`SIMILARITY`](similarity.md), [`LEVENSHTEIN`](levenshtein.md)
