# UNICODE

Returns the Unicode code point of the first character of a string.

## Syntax

```sql
UNICODE(string)
```

## Parameters

- **string** - Input string. Only the first character is evaluated.

## Returns

Returns the integer Unicode code point of the first character.

## Null Behavior

Returns `NULL` when `string` is `NULL` or empty.

## Remarks

- For ASCII-range characters, `UNICODE` and `ASCII` return identical values.
- For characters outside the Basic Multilingual Plane, returns the full code point value.
- To reverse the operation, use [`CHAR`](char.md).

## Examples

```sql
SELECT UNICODE('A') AS upper_a_code;
```

```sql
SELECT UNICODE(customer_name) AS first_character_code
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [ASCII](ascii.md)
- [CHAR](char.md)
