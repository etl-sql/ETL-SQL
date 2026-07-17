# SOUNDEX

Returns the Soundex phonetic encoding of a string.

## Syntax

```sql
SOUNDEX(string)
```

## Parameters

- **string** - Word or name to encode.

## Returns

Returns a 4-character Soundex code, such as `R163`.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

- Soundex encodes English pronunciation. Useful for fast phonetic blocking before `SIMILARITY` scoring.
- For more accurate encoding, use [`METAPHONE`](metaphone.md) or [`DMETAPHONE`](dmetaphone.md).
- To score the Soundex difference between two strings, use [`DIFFERENCE`](difference.md).

## Examples

```sql
SELECT SOUNDEX('Robert') AS soundex_code;
```

```sql
SELECT a.name AS source_name, b.name AS candidate_name
FROM #source AS a
JOIN #reference AS b
  ON SOUNDEX(a.name) = SOUNDEX(b.name);
```

## References

- [Standard Library](../standard-library.md)
- [METAPHONE](metaphone.md)
- [DMETAPHONE](dmetaphone.md)
- [DIFFERENCE](difference.md)
- [SIMILARITY](similarity.md)
