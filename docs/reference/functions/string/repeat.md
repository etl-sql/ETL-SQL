# REPEAT

Repeats a string a specified number of times.

## Syntax

```sql
REPEAT(string, count)
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
SELECT REPEAT('abc', 3) AS repeated_value;
```

```sql
SELECT REPEAT('-', 40) AS separator_line;
```

## References

- [Standard Library](../standard-library.md)
- [REPLICATE](replicate.md)
- [LPAD](lpad.md)
- [RPAD](rpad.md)
