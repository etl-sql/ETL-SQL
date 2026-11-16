# LOWER

Converts all characters in a string to lowercase.

## Syntax

```sql
LOWER(string)
```

## Parameters

- **string** - Input string to convert.

## Returns

Returns the input string with alphabetic characters converted to lowercase.

## Null Behavior

Returns `NULL` when `string` is `NULL`.

## Remarks

- Casing is the same on every server, whatever its locale: `i` and `I` are never the Turkish dotted or dotless forms.

## Examples

```sql
SELECT LOWER('HELLO WORLD') AS normalized_text;
```

```sql
SELECT user_id, LOWER(email) AS normalized_email
FROM #users;
```

## References

- [Functions](../README.md)
- [UPPER](upper.md)
- [INITCAP](../string/initcap.md)
