# LEFT

Returns the leftmost N characters of a string.

## Syntax

```sql
LEFT(string, count)
```

## Parameters

- **string** - Source string.
- **count** - Number of characters to return from the left.

## Returns

Returns the first `count` characters. If `count` exceeds the string length, returns the full string.

## Null Behavior

Returns `NULL` when `string` or `count` is `NULL`.

## Examples

```sql
SELECT LEFT('Hello World', 5) AS prefix;
```

```sql
SELECT LEFT(zip_code, 3) AS region
FROM #addresses;
```

## References

- [Functions](../README.md)
- [RIGHT](right.md)
- [SUBSTRING](substring.md)
