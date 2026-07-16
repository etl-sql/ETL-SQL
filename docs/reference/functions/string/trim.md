# TRIM

Removes leading and trailing whitespace (or specified characters) from a string.

## Syntax

```sql
TRIM(string)
TRIM(BOTH | LEADING | TRAILING chars FROM string)
```

## Parameters

- **string** - String to trim.
- **chars** - Optional characters to remove instead of whitespace.

## Returns

Returns the string with the specified characters removed from the requested side or sides.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT TRIM('  hello  ') AS trimmed_value;
```

```sql
SELECT TRIM(LEADING '0' FROM account_code) AS normalized_code
FROM #accounts;
```

## References

- [Standard Library](../standard-library.md)
- [LTRIM](ltrim.md)
- [RTRIM](rtrim.md)
