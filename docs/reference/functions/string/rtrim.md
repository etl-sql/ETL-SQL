# RTRIM

Removes trailing (right-side) whitespace from a string.

## Syntax

```sql
RTRIM(string)
```

## Parameters

- **string** - String to trim.

## Returns

Returns the input string with trailing whitespace removed.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT RTRIM('hello   ') AS trimmed_value;
```

```sql
SELECT contact_id, RTRIM(address) AS normalized_address
FROM #contacts;
```

## References

- [Standard Library](../standard-library.md)
- [LTRIM](ltrim.md)
- [TRIM](trim.md)
