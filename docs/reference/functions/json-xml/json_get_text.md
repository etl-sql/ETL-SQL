# JSON_GET_TEXT

One JSON access step returned **as text** - strings come back unquoted. This is the function behind
the `->>` operator (PostgreSQL/MySQL/SQLite style).

## Syntax

```sql
JSON_GET_TEXT(json, key_or_index)
json ->> key_or_index
```

## Parameters

- **json** - JSON document, object, or array.
- **key_or_index** - Field name for objects or integer index for arrays. Negative indexes count from the end.

## Returns

Returns the selected value as text. String values are unquoted, numbers and booleans return their literal text, and objects or arrays return raw JSON text.

## Null Behavior

Returns `NULL` when the selected value is JSON `null`, the key is missing, the index is out of range, the input shape does not match the requested access, or `json` is not valid JSON.

## Remarks

- Use `->` / `JSON_GET` for intermediate steps (keeps the value as JSON so steps chain); use `->>`
  for the final step where you want the plain value.
- Null-propagating by design - combine with `??` for defaults: `doc ->> 'qty' ?? '0'`.

## Examples

```sql
SELECT doc ->> 'name' AS name
FROM #people;
```

```sql
SELECT CAST(doc ->> 'qty' AS INT) * price AS total
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [Grammar](../../statements/grammar.md)
- [JSON_GET](json_get.md)
- [JSON_VALUE](json_value.md)
- [JSON_QUERY](json_query.md)
