# DMETAPHONE

Returns the primary Double Metaphone phonetic key for a string.

## Syntax

```sql
DMETAPHONE(string)
```

## Returns

Returns a `STRING` phonetic key.

## Null Behavior

`DMETAPHONE(NULL)` returns `NULL`.

## Remarks

- Use `DMETAPHONE` for fuzzy matching names and other English-like text.
- Use [`DMETAPHONE_ALT`](dmetaphone_alt.md) to retrieve the alternate Double Metaphone key.
- Use [`SIMILARITY`](similarity.md) or [`LEVENSHTEIN`](levenshtein.md) for edit-distance style matching.

## Examples

```sql
SELECT DMETAPHONE('Schmidt') AS phonetic_key;
```

```sql
SELECT a.customer_id, b.customer_id AS possible_match
FROM #customers a
JOIN #customers b
  ON DMETAPHONE(a.last_name) = DMETAPHONE(b.last_name)
 WHERE a.customer_id <> b.customer_id;
```

## References

- [Standard Library](../standard-library.md)
- [DMETAPHONE_ALT](dmetaphone_alt.md)
- [METAPHONE](metaphone.md)
- [SIMILARITY](similarity.md)
