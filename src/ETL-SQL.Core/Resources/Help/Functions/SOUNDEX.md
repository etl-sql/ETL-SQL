# SOUNDEX
Returns the Soundex phonetic encoding of a string.

**Category:** Fuzzy

## Syntax
```sql
SOUNDEX(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The word or name to encode |

## Returns
`STRING` — A 4-character Soundex code (e.g., `'R163'`).

## Remarks
- Soundex encodes English pronunciation. Useful for fast phonetic blocking before `SIMILARITY` scoring.
- For more accurate encoding, use [`METAPHONE`](METAPHONE.md) or [`DMETAPHONE`](DMETAPHONE.md).
- To score the Soundex difference between two strings, use [`DIFFERENCE`](DIFFERENCE.md).

## Example
```sql
SELECT SOUNDEX('Robert');    -- → 'R163'
SELECT SOUNDEX('Rupert');    -- → 'R163'  (same code — phonetically similar)
SELECT * FROM #names a JOIN #names b AS SOUNDEX(a.name) = SOUNDEX(b.name);
```

## See Also
- [Standard Library — §16.4 Phonetic Encoding Functions](../../../../../Docs/Reference/Standard_Library.md#164-phonetic-encoding-functions)
- Related: [`METAPHONE`](METAPHONE.md), [`DMETAPHONE`](DMETAPHONE.md), [`DIFFERENCE`](DIFFERENCE.md), [`SIMILARITY`](SIMILARITY.md)
