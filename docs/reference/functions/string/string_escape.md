# STRING_ESCAPE

Escapes special characters in a string for safe embedding in a target format.

## Syntax

```sql
STRING_ESCAPE(text, type)
```

## Parameters

- **text** - String to escape.
- **type** - Target format. See [accepted values](#accepted-values).

## Returns

Returns `text` with special characters escaped for the specified format.

## Null Behavior

Returns `NULL` when `text` or `type` is `NULL`.

## Accepted Values

- **`'json'`** - Escapes quotes, backslashes, and control characters from `U+0000` through `U+001F` for embedding in JSON strings.

## Examples

```sql
SELECT STRING_ESCAPE(notes, 'json') AS safe_notes
FROM #records;
```

```sql
SELECT '{"message": "' + STRING_ESCAPE(body, 'json') + '"}' AS message_json
FROM #messages;
```

## References

- [Functions](../README.md)
- [QUOTENAME](quotename.md)
- [JSON_MODIFY](../json-xml/json_modify.md)
