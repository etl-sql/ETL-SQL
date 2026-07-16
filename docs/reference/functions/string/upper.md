# UPPER

Converts all characters in a string to uppercase.

## Syntax

```sql
UPPER(string)
```

## Parameters

- **string** - Input string to convert.

## Returns

Returns the input string with alphabetic characters converted to uppercase.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Examples

```sql
SELECT UPPER('hello world') AS display_text;
```

```sql
SELECT customer_id, UPPER(first_name) AS display_name
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [LOWER](lower.md)
- [INITCAP](../general/initcap.md)
