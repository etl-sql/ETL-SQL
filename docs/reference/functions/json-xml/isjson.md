# ISJSON

Returns `1` when a string contains valid JSON, and `0` otherwise.

## Syntax

```sql
ISJSON(string)
```

## Parameters

- **string** - Value to test.

## Returns

Returns `1` when `string` is valid JSON; otherwise returns `0`.

## Null Behavior

Returns `0` when `string` is `NULL`.

## Examples

```sql
SELECT ISJSON('{"id": 1}') AS is_valid_json;
```

```sql
SELECT *
FROM #raw
WHERE ISJSON(payload) = 0;
```

## References

- [Functions](../README.md)
- [JSON_VALUE](json_value.md)
- [TRY_CAST](../conversion/try_cast.md)
