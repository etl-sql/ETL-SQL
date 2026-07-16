# JSON_GET
One JSON access step — an object field or array element — returned **as JSON**. This is the
function behind the `->` operator (PostgreSQL/MySQL/SQLite style).

**Category:** JSON

## Syntax
```sql
JSON_GET(json, key_or_index)
json -> key_or_index          -- operator form (preferred)
```

## Parameters
- **json** (`STRING`/`JSON`) — the JSON document (object or array).
- **key_or_index** — a string field name (for objects) or an integer index (for arrays; negative
  counts from the end, so `-1` is the last element). May be any expression, including a variable.

## Returns
`STRING` — the selected value serialised as JSON (strings keep their quotes), or `NULL` when the
key is missing, the index is out of range, the kinds mismatch, or the input is not valid JSON.

## Remarks
- Because the result is JSON, steps **chain**: `doc -> 'customer' -> 'address' ->> 'city'`.
- Use `->>` / `JSON_GET_TEXT` for the final step when you want the value as plain text.
- For JSONPath-style access in one call, use `JSON_VALUE(json, '$.a.b')` / `JSON_QUERY`.
- Null-propagating by design — combine with `??` for defaults: `doc ->> 'qty' ?? '0'`.

## Example
```sql
SELECT doc -> 'customer' -> 'address' ->> 'city' AS city FROM #orders;
SELECT '[10,20,30]' -> -1;      -- '30' (as JSON)
SELECT JSON_GET(@payload, @field);
```

References:
- [Standard Library — §11. JSON Functions](../../../guides/getting-started.md#11-json-functions)
- [Grammar §14.6 — JSON Access Operators](../../../guides/getting-started.md)
- Related: [`JSON_GET_TEXT`](json_get_text.md), [`JSON_VALUE`](../json-xml/json_value.md), [`JSON_QUERY`](../json-xml/json_query.md)
