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

## Remarks

- Casing is the same on every server, whatever its locale: `i` and `I` are never the Turkish dotted or dotless forms.

## Examples

```sql
SELECT UPPER('hello world') AS display_text;
```

```sql
SELECT customer_id, UPPER(first_name) AS display_name
FROM #customers;
```

## References

- [Functions](../README.md)
- [LOWER](lower.md)
- [INITCAP](../string/initcap.md)
