# JSON_GET

One JSON access step - an object field or array element - returned **as JSON**. This is the
function behind the `->` operator (PostgreSQL/MySQL/SQLite style).

## Syntax

```sql
JSON_GET(json, key_or_index)
json -> key_or_index
```

## Parameters

- **json** - JSON document, object, or array.
- **key_or_index** - Field name for objects or integer index for arrays. Negative indexes count from the end.

## Returns

Returns the selected value serialized as JSON. String values remain quoted.

## Null Behavior

Returns `NULL` when the key is missing, the index is out of range, the input shape does not match the requested access, or `json` is not valid JSON.

## Remarks

- Because the result is JSON, steps **chain**: `doc -> 'customer' -> 'address' ->> 'city'`.
- Use `->>` / `JSON_GET_TEXT` for the final step when you want the value as plain text.
- For JSONPath-style access in one call, use `JSON_VALUE(json, '$.a.b')` / `JSON_QUERY`.
- Null-propagating by design - combine with `??` for defaults: `doc ->> 'qty' ?? '0'`.

## Examples

```sql
SELECT doc -> 'customer' -> 'address' ->> 'city' AS city
FROM #orders;
```

```sql
SELECT JSON_GET(@payload, @field) AS selected_json;
```

## References

- [Standard Library](../standard-library.md)
- [Grammar](../../statements/grammar.md)
- [JSON_GET_TEXT](json_get_text.md)
- [JSON_VALUE](json_value.md)
- [JSON_QUERY](json_query.md)
