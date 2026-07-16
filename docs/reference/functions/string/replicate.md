# REPLICATE

Repeats a string a specified number of times.

## Syntax

```sql
REPLICATE(string, count)
```

## Parameters

- **string** - String to repeat.
- **count** - Number of times to repeat `string`.

## Returns

Returns the repeated string. Returns an empty string when `count` is `0` or negative.

## Null Behavior

Returns `NULL` when `string` or `count` is `NULL`.

## Examples

```sql
SELECT REPLICATE('ab', 3) AS repeated_value;
```

```sql
SELECT REPLICATE('0', 5 - LEN(id)) + id AS padded_id
FROM #items;
```

## References

- [Standard Library](../standard-library.md)
- [REPEAT](repeat.md)
- [SPACE](space.md)
- [STR](str.md)
