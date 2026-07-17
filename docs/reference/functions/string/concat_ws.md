# CONCAT_WS

Concatenates strings with a separator, automatically skipping NULL values.

## Syntax

```sql
CONCAT_WS(separator, string1, string2, ...)
```

## Parameters

- **separator** - String inserted between each non-`NULL` value.
- **string1** - First value to concatenate.
- **string2** - Second value to concatenate.
- **...** - Additional values to concatenate.

## Returns

Returns all non-`NULL` arguments joined with `separator`.

## Null Behavior

Skips `NULL` value arguments. Returns `NULL` when all value arguments are `NULL`.

## Remarks

- No separator is inserted for skipped `NULL` arguments.

## Examples

```sql
SELECT CONCAT_WS(', ', 'Alice', NULL, 'Bob') AS names;
```

```sql
SELECT CONCAT_WS(' ', first_name, middle_name, last_name) AS full_name
FROM #people;
```

## References

- [Functions](../README.md)
- [CONCAT](concat.md)
- [STRING_AGG](../aggregate/string_agg.md)
