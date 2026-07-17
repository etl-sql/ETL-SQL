# LEN

Returns the number of characters in a string, or the number of items in a LIST.

## Syntax

```sql
LEN(string)
LENGTH(string)
```

## Parameters

- **string** - String or list value to measure.

## Returns

Returns an `INT` character count for strings or item count for lists. Trailing spaces are not counted.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

- `LEN` and `LENGTH` are interchangeable aliases.
- For byte-level length, use [`DATALENGTH`](datalength.md) instead.

## Examples

```sql
SELECT LEN('hello') AS character_count;
```

```sql
DECLARE @ids LIST = (1, 2, 3);
SELECT LEN(@ids) AS item_count;
```

## References

- [Functions](../README.md)
- [DATALENGTH](datalength.md)
- [CHAR_LENGTH](char_length.md)
