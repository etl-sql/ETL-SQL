# METAPHONE

Returns the English phonetic code (Metaphone key) of a string.

## Syntax

```sql
METAPHONE(string)
```

## Parameters

- **string** - The input string to encode.

## Returns

Returns a `STRING` phonetic key.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT METAPHONE('Jackson') AS phonetic_key;
```

```sql
SELECT customer_id, METAPHONE(last_name) AS last_name_key
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [SOUNDEX](soundex.md)
- [DMETAPHONE](dmetaphone.md)
