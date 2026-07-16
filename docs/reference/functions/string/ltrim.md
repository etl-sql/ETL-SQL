# LTRIM

Removes leading (left-side) whitespace from a string.

## Syntax

```sql
LTRIM(string)
```

## Parameters

- **string** - String to trim.

## Returns

Returns the input string with leading whitespace removed.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT LTRIM('  hello') AS trimmed_value;
```

```sql
SELECT row_id, LTRIM(raw_name) AS left_trimmed_name
FROM #data;
```

## References

- [Standard Library](../standard-library.md)
- [RTRIM](rtrim.md)
- [TRIM](trim.md)
