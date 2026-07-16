# LEVENSHTEIN

Computes the Levenshtein edit distance between two strings.

## Syntax

```sql
LEVENSHTEIN(string1, string2)
```

## Parameters

- **string1** - First string.
- **string2** - Second string.

## Returns

Returns an `INT` count of single-character insertions, deletions, or substitutions required to transform `string1` into `string2`.

## Null Behavior

Returns `NULL` when either input is `NULL`.

## Remarks

- Lower values are closer matches.
- Use [`SIMILARITY`](similarity.md) when a normalized `0` to `1` score is easier to compare.

## Examples

```sql
SELECT LEVENSHTEIN('kitten', 'sitting') AS edit_distance;
```

```sql
SELECT a.customer_id, b.customer_id AS possible_match
FROM #customers a
JOIN #customers b
  ON LEVENSHTEIN(a.normalized_name, b.normalized_name) <= 2
 WHERE a.customer_id <> b.customer_id;
```

## References

- [Standard Library](../standard-library.md)
- [SIMILARITY](similarity.md)
- [SOUNDEX](soundex.md)
