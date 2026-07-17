# DMETAPHONE_ALT

Returns the alternate Double Metaphone phonetic key for a string.

## Syntax

```sql
DMETAPHONE_ALT(string)
```

## Parameters

- **string** - The input string to encode.

## Returns

Returns a `STRING` alternate phonetic key.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT DMETAPHONE_ALT('Schmidt') AS alternate_key;
```

```sql
SELECT customer_id, DMETAPHONE_ALT(last_name) AS last_name_alt_key
FROM #customers;
```

## References

- [Functions](../README.md)
- [DMETAPHONE](dmetaphone.md)
- [METAPHONE](metaphone.md)
