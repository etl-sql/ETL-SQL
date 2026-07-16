# CONCAT

Concatenates two or more strings into a single string.

## Syntax

```sql
CONCAT(string1, string2, ...)
```

## Parameters

- **string1** - First value to concatenate.
- **string2** - Second value to concatenate.
- **...** - Additional values to concatenate.

## Returns

Returns all arguments joined in order as a `STRING`.

## Null Behavior

Treats `NULL` arguments as empty strings.

## Remarks

- `NULL` arguments do not propagate `NULL`, unlike the `+` operator.
- Non-string arguments are implicitly coerced to `STRING`.

## Examples

```sql
SELECT CONCAT('Hello', ' ', 'World') AS greeting;
```

```sql
SELECT CONCAT(first_name, ' ', last_name) AS full_name
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [CONCAT_WS](concat_ws.md)
- [STRING_AGG](../aggregate/string_agg.md)
