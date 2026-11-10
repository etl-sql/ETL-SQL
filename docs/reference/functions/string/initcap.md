# INITCAP

Capitalizes the first letter of each word and lowercases the rest.

## Syntax

```sql
INITCAP(string)
```

## Parameters

- **string** - Input string to title-case.

## Returns

Returns a string with each word capitalized and remaining characters lowercased.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

- Casing is the same on every server, whatever its locale: `i` and `I` are never the Turkish dotted or dotless forms.

## Examples

```sql
SELECT INITCAP('hello world') AS title_text;
```

```sql
SELECT INITCAP(customer_name) AS display_name
FROM #customers;
```

## References

- [Functions](../README.md)
- [UPPER](../string/upper.md)
- [LOWER](../string/lower.md)
