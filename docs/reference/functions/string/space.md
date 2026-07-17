# SPACE

Returns a string of N space characters.

## Syntax

```sql
SPACE(count)
```

## Parameters

- **count** - Number of space characters to return.

## Returns

Returns a string containing exactly `count` spaces.

## Null Behavior

Returns `NULL` when `count` is `NULL`. Returns an empty string when `count <= 0`.

## Examples

```sql
SELECT SPACE(5) AS five_spaces;
```

```sql
SELECT name + SPACE(20 - LEN(name)) AS padded_name
FROM #items;
```

## References

- [Functions](../README.md)
- [REPLICATE](replicate.md)
- [STR](str.md)
