# RIGHT

Returns the rightmost N characters of a string.

## Syntax

```sql
RIGHT(string, count)
```

## Parameters

- **string** - Source string.
- **count** - Number of characters to return from the right.

## Returns

Returns the last `count` characters. If `count` exceeds the string length, returns the full string.

## Null Behavior

Returns `NULL` when `string` or `count` is `NULL`.

## Examples

```sql
SELECT RIGHT('Hello World', 5) AS suffix;
```

```sql
SELECT RIGHT('00' + TO_STR(id), 4) AS padded_id
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [LEFT](left.md)
- [SUBSTRING](substring.md)
