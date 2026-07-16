# ASCII

Returns the ASCII / Unicode code point of the first character of a string.

## Syntax

```sql
ASCII(string)
```

## Parameters

- **string** - Input string. Only the first character is evaluated.

## Returns

Returns the integer code point of the first character.

## Null Behavior

Returns `NULL` when `string` is `NULL` or empty.

## Examples

```sql
SELECT ASCII('A') AS upper_a_code;
```

```sql
SELECT ASCII(customer_code) AS leading_code
FROM #customers;
```

## Remarks

- For Unicode strings, returns the Unicode code point (same as [`UNICODE`](unicode.md)).
- To get the character for a code point, use [`CHAR`](char.md).

## References

- [Standard Library](../standard-library.md)
- [UNICODE](unicode.md)
- [CHAR](char.md)
